# Research — DFIR_UPLOADER_UI

## R-1: Mega S4 Object Storage validation

**Date**: 2026-10-08
**Source**: direct probes against `inc-lena.s3.g.megas4.com`

### TLS cert

```
subject= /CN=*.g.s4.mega.io
issuer= /C=US/O=Let's Encrypt/CN=YR1
notBefore=Oct  5 21:15:34 2026 GMT
notAfter=Jan  3 21:15:33 2027 GMT
```

Valid Let's Encrypt cert covering `*.g.s4.mega.io` (subdomain of `s4.mega.io`). Expires **3 enero 2027**.

### API behavior

```
HEAD /                                 → 400 (sin auth)
HEAD /test-discovery-probe-does-not-exist
                                       → 400 (sin auth, mensaje SigV4 required)
HEAD /test-discovery-probe  con SigV4 falso → 403 (firma validada, signature rejected)
GET /?list-type=2                      → 400 con XML:
   <Error><Code>InvalidRequest</Code>
   <Message>Please use AWS Signature Version 4 for authentication</Message>
   <Resource>/inc-lena</Resource></Error>
```

**Conclusiones**:
- Bucket es S3-compatible (formato de errores AWS, headers `x-amz-*`).
- SigV4 enforcement es real (HEAD con auth falso → 403, no 400).
- Endpoint es Mega S4 Object Storage (Mega.nz), no Mega.nz WebDAV.
- Decisión de un solo backend (S3 puro) en el .exe — confirmado.

## R-2: AWS SigV4 manual implementation

**Source**: https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
**C# implementation**: `DfirUploaderUi/SigV4Signer.cs`

### Algorithm

1. **Canonical request** = `HTTPMethod\nCanonicalURI\nCanonicalQueryString\nCanonicalHeaders\nSignedHeaders\nHexEncode(Hash(RequestPayload))`
2. **String to sign** = `AWS4-HMAC-SHA256\nTimestamp\nCredentialScope\nHexEncode(Hash(CanonicalRequest))`
3. **Signing key** = HMAC-SHA256 derivado en cascada:
   - `kSecret = "AWS4" + secretKey` (UTF-8)
   - `kDate = HMAC-SHA256(kSecret, YYYYMMDD)`
   - `kRegion = HMAC-SHA256(kDate, region)`
   - `kService = HMAC-SHA256(kRegion, "s3")`
   - `kSigning = HMAC-SHA256(kService, "aws4_request")`
4. **Signature** = `HexEncode(HMAC-SHA256(kSigning, StringToSign))`
5. **Authorization header** = `AWS4-HMAC-SHA256 Credential=<ak>/<scope>, SignedHeaders=<list>, Signature=<sig>`

### Payload hash modes for S3

| Operation | Mode | Hash value |
|---|---|---|
| Single PUT < 8 MiB | `Sha256` | hex(SHA256(bytes)) |
| CreateMultipartUpload | `Empty` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| UploadPart | `Unsigned` | `UNSIGNED-PAYLOAD` |
| CompleteMultipartUpload | `Empty` | `e3b0c4...` |
| AbortMultipartUpload | `Empty` | `e3b0c4...` |
| HEAD verify | `Empty` | `e3b0c4...` |

### Gotchas

- **Canonical headers must be sorted lowercase**. Usamos `SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)`.
- **Headers values trimmed, no multi-line**. Si hay whitespace interno, RFC no lo soporta en SigV4 (algunos servicios son permisivos).
- **`Host` header se calcula del endpoint URL** (host:port si no es default 443).
- **`x-amz-content-sha256` SIEMPRE presente** (incluso si es empty hash). S3 lo requiere.
- **`x-amz-security-token` solo si session token presente** (credenciales STS).
- **Canonical URI no se URL-encode doble**. AWS usa el path tal cual del request (URI-escaped ONCE).

## R-3: S3 multipart upload

**Source**: https://docs.aws.amazon.com/AmazonS3/latest/API/API_Operations_Amazon_Simple_Storage_Service.html

### Endpoints

- `POST /<bucket>/<key>?uploads` → CreateMultipartUpload. Body XML `<CreateMultipartUpload>`. Returns `<InitiateMultipartUploadResult><Bucket>...</Bucket><Key>...</Key><UploadId>...</UploadId></InitiateMultipartUploadResult>`.
- `PUT /<bucket>/<key>?partNumber=N&uploadId=X` → UploadPart. Body = part bytes. Returns ETag header.
- `POST /<bucket>/<key>?uploadId=X` → CompleteMultipartUpload. Body XML:
  ```xml
  <CompleteMultipartUpload>
    <Part><PartNumber>1</PartNumber><ETag>"abc..."</ETag></Part>
    <Part><PartNumber>2</PartNumber><ETag>"def..."</ETag></Part>
  </CompleteMultipartUpload>
  ```
  Returns `<CompleteMultipartUploadResult><Location>...</Location><Bucket>...</Bucket><Key>...</Key><ETag>"..."-N</ETag></CompleteMultipartUploadResult>`.
- `DELETE /<bucket>/<key>?uploadId=X` → AbortMultipartUpload. Returns 204 No Content.

### Limits

- Max parts per upload: 10000. Con chunk de 8 MiB → max 80 GB. OK para `.raw` 32 GB.
- Min part size (except last): 5 MiB. Con chunk de 8 MiB cumplimos.
- Max part size: 5 GiB. No aplica.

### ETag semantics

- Single PUT ETag = MD5(body).
- Multipart ETag = `MD5(MD5(part1) || MD5(part2) || ... || MD5(partN))-N`.
- Multipart ETag **NO** es SHA-256 del archivo completo. Por eso no comparamos ETag con SHA-256 — están en dominios diferentes.

## R-4: WinForms .NET 4.5.2 limitations

### What's missing vs newer .NET

- `TextBox.PlaceholderText` — added in .NET 5. .NET 4.5.2 TextBox no lo tiene.
- `DateTimePicker` no tiene formato `Custom` tan flexible. Usamos `Short`.
- `CheckedListBox` no soporta `DataSource`. Iteramos items manual.
- `BackgroundWorker` con `ProgressChanged` requiere `Invoke` en `IProgress<T>` lambda (no `BackgroundWorker.ReportProgress`).

### Threading

- WinForms requiere STA. Entry point `[STAThread]`.
- Cross-thread updates a `Control` deben usar `Invoke()` o `BeginInvoke()`. Para eventos de UI thread, no hace falta.
- `async void` event handlers estan bien — WinForms los espera como void-returning.

## R-5: Config storage options

| Option | Pros | Cons | Recommendation |
|---|---|---|---|
| GitHub gist (public) | Sin infra, gratis, link corto, borrable | Public en search (cualquier con link ve) | OK si IAM key tiene policy muy scoped |
| GitHub gist (secret) | No aparece en search | Cualquier con link lo ve igual, gist es guessable | Marginal gain |
| `beaconlab.us/uploads/<token>.json` | Operador controla ciclo de vida, puede agregar auth (bearer token en header) | Requiere infra del operador | **Recomendado** |
| S3 self-hosted (mismo bucket) | Sin infra adicional, cifrado at rest | Riesgo si config y evidencia en mismo bucket | No recomendado |

**Decisión final**: soportar cualquiera via URL. El operador decide donde hostear.

## R-6: IAM policy template (para operator)

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Sid": "DFIRUploaderScopedWrite",
    "Effect": "Allow",
    "Action": [
      "s3:PutObject",
      "s3:GetObject",
      "s3:ListBucket",
      "s3:AbortMultipartUpload",
      "s3:ListMultipartUploadParts"
    ],
    "Resource": [
      "arn:aws:s3:::inc-lena/clients/<client-id>/<incident-id>/*",
      "arn:aws:s3:::inc-lena"
    ]
  }]
}
```

IAM key TTL: 24-48 horas. El operador la crea cuando arranca el drill, la borra cuando termina.

## R-7: Why no AWSSDK?

Considered. Tradeoffs:

| Aspect | Manual SigV4 | AWSSDK.S3 |
|---|---|---|
| Binary size | ~150 KB | ~5 MB (AWSSDK.S3 NuGet + deps) |
| Compile time | Fast | Slow (NuGet restore) |
| SigV4 correctness | Risk (manual) | Library maintained |
| Cancellation | Direct | Library + propagation |
| Multipart upload | Manual | Library |
| Pro | Small binary, no deps | Battle-tested |
| Con | Bug risk in canonical request | Bloats ISO 25x |

For DFIR drill (lab scenarios) we accept the manual risk. For prod, AWSSDK is the right call. Documented for v5.4+ upgrade path.

## R-8: Audit log design

### Local copy (`E:\DFIR-OUTPUT\upload-audit-<ts>.json`)

Kept on disk for the operator's records. Survives even if S3 upload fails. Provides ground truth for the analyst.

### Sidecar in bucket (`<prefix>/audit-<ts>.json`)

Uploaded best-effort after main uploads. Tied to the operator's records even if local disk is wiped.

### Schema (v1)

```json
{
  "schema_version": 1,
  "started_utc": "2026-10-08T22:05:30Z",
  "completed_utc": "2026-10-08T22:45:18Z",
  "operator": "LAB\\Administrator",
  "case_id": "RANSOMWARE-DC-2026-10-08",
  "incident_date": "2026-10-08",
  "notes": "Captured after DC was found encrypting file shares.",
  "bucket": "inc-lena",
  "endpoint": "https://inc-lena.s3.g.megas4.com",
  "prefix": "clients/acme-corp/incident-2026-10-08/",
  "host": {
    "name": "DC01",
    "user": "LAB\\Administrator",
    "os": "Microsoft Windows Server 2019 Datacenter 10.0.x"
  },
  "files": [
    {
      "local_path": "E:\\DFIR-OUTPUT\\KapeTriage-WIN10-A-20261008.zip",
      "s3_key": "clients/acme-corp/incident-2026-10-08/KapeTriage-WIN10-A-20261008.zip",
      "size_bytes": 801326105,
      "sha256": "8b4c0fc8ec9022e3...",
      "etag": "\"a4d516b6fcaf3b5b1d4ee709ce86f8eab-2\"",
      "upload_id": "abc...",
      "parts": 2,
      "started_utc": "2026-10-08T22:05:32Z",
      "completed_utc": "2026-10-08T22:08:15Z",
      "status": "OK",
      "error": null
    }
  ]
}
```

## R-9: Future v5.4+ improvements

Documented for backlog, NOT in scope v5.3.0:

1. **AWSSDK.S3 replacement**: replace manual SigV4 with `AWSSDK.S3` NuGet. Tradeoff: binario grows ~5 MB.
2. **Resumable uploads**: if app crashes mid-upload, on next run resume from `ListParts`.
4. **Multi-band correlation**: serializable AES-256 + password-encrypted audit log.
3. **GUI delete-after-upload**: opt-in checkbox that deletes local after audit log is written.
5. **Bucket browser**: read-only file listing (no delete).
6. **Mega.nz native API path**: separate implementation for users on classic Mega (not S4).
7. **Multi-config profiles**: save last 5 URLs in memory + dropdown for re-use.
8. **Server-side encryption toggle**: SSE-KMS key selector per-incident.