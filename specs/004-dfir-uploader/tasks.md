# Tasks — DFIR_UPLOADER_UI (KAPE-MEDIA v5.3.0)

Lista ordenada por dependencia. Cada task tiene ID para trazabilidad con spec.md y plan.md.

## T-01: Project skeleton (DONE 2026-10-08)

- [x] Crear `DfirUploaderUi/DfirUploaderUi.csproj` (mirror KapeUi.csproj, TargetFramework=net452).
- [x] Crear `DfirUploaderUi/app.manifest` con `requireAdministrator` y supportedOS Win7/Win8/Win10/Win11.
- [x] Crear `DfirUploaderUi/AppInfo.cs` con assembly attrs + `Version="5.3.0.0"`.
- [x] Crear `DfirUploaderUi/Program.cs` con entry point + `ApplicationVersion` constant.

## T-02: Hash + SigV4 + JSON + Audit (DONE 2026-10-08)

- [x] `DfirUploaderUi/HashUtil.cs`: `ComputeSha256Streaming(path, ct)` con FileStream 1 MiB buffer + SHA256.Create().
- [x] `DfirUploaderUi/SigV4Signer.cs`: `SignRequest(method, endpoint, uri, query, headers, mode, payloadBytes, ak, sk, region, service, sessionToken)`. Implementa canonical request + string to sign + signing key derivation + HMAC-SHA256. Cubre modos Empty, Unsigned, Streaming, Sha256.
- [x] `DfirUploaderUi/ConfigFetcher.cs`: `FetchAsync(url, ct, log)` con HttpClient (cert validation default) + JavaScriptSerializer + validacion de campos + validacion de `expires_at > now+30min`.
- [x] `DfirUploaderUi/AuditLog.cs`: clase `AuditLog` con `HostInfo` + `FileEntry`. Serializa con JavaScriptSerializer. Soporta `AddFile()` / `MarkFileResult()` / `WriteToDisk()`.

## T-03: S3 uploader (DONE 2026-10-08)

- [x] `DfirUploaderUi/S3Uploader.cs`: `UploadFileAsync(path, key, size, sha)` que decide single PUT (< 8 MiB) vs multipart (>= 8 MiB).
- [x] Single PUT: lee archivo a byte[], firma con `Sha256` mode, PUT con `application/octet-stream`.
- [x] Multipart: CreateMultipartUpload (POST ?uploads) → UploadPart loop con `Unsigned` mode → CompleteMultipartUpload (POST ?uploadId con XML body listando part numbers + ETags).
- [x] AbortMultipartUpload en catch.
- [x] `VerifyHeadAsync(key)`: HEAD signed, devuelve Content-Length.
- [x] Progress reporting via `IProgress<UploadProgress>`.

## T-04: Form principal (DONE 2026-10-08)

- [x] `DfirUploaderUi/DfirUploaderMain.cs`: Form 980x720 con 5 secciones (config, metadata, file selector, upload controls, log).
- [x] Sync button → ConfigFetcher.FetchAsync.
- [x] Health button → HEAD probe con key inexistente (404 esperado = OK).
- [x] Browse + Rescan → recursive scan, CheckedListBox con formato "{size} {path}".
- [x] ItemCheck handler → recalcula total size.
- [x] Upload button → SHA-256 streaming + S3Uploader.UploadFileAsync + AuditLog bookkeeping.
- [x] Cancel button → CancellationTokenSource.Cancel() (abort multipart automatico en catch).
- [x] Save log button → SaveFileDialog + File.WriteAllText.
- [x] FormClosing → confirma cancel si hay upload en curso, libera _cts.
- [x] Pre-flight CheckAdminOrWarn via WindowsPrincipal.
- [x] Audit sidecar upload al final (best-effort).

## T-05: Build verification (DONE 2026-10-08)

- [x] `dotnet build DfirUploaderUi/DfirUploaderUi.csproj -c Release` → 0 errors, 0 warnings.
- [x] Binario generado: 62 KB (objetivo <= 200 KB, super cumplido).
- [x] SHA-256 binario: `2b0c5345399a36fe25f215bcc2d4a4490f562285608771f0502b64dd9eefbd37`.

## T-06: Build pipeline integration (DONE 2026-10-08)

- [x] Modificar `build.sh` para compilar `DfirUploaderUi/DfirUploaderUi.csproj` y copiar binario a `staging/`.
- [x] Binario copiado a `staging/DFIR_UPLOADER_UI.exe` (62 KB).

## T-07: Spec-kit + docs

- [x] Crear `specs/004-dfir-uploader/spec.md` (este spec).
- [x] Crear `specs/004-dfir-uploader/plan.md` (plan completo en `/Users/rbnetto/.minimax/.../artifacts/plan.md`).
- [x] Crear `specs/004-dfir-uploader/tasks.md` (este archivo).
- [x] Crear `specs/004-dfir-uploader/research.md` (research about Mega S4, SigV4, AWS S3 multipart, cert validation).
- [x] Crear `specs/004-dfir-uploader/config-template.json` (template que el operador edita a mano).
- [x] Crear `docs/CONFIG-SCHEMA.md` (documentacion de cada campo del config).

## T-08: ISO docs

- [ ] Modificar `staging/VERSION.txt` con entrada v5.3.0 + changelog.
- [ ] Modificar `staging/SHA256SUMS.txt` con hash del nuevo binario.
- [ ] Modificar `staging/README.txt` agregar Opcion D (subida de evidencia).
- [ ] Modificar `staging/MANUAL_OPERATIVO.txt` agregar seccion 11 (subida con DFIR_UPLOADER). Renumerar la actual seccion 11 (CHECKLIST) a 12.
- [ ] Modificar `KAPE-MEDIA-INFO.txt` agregar bloque "DFIR_UPLOADER_UI.exe detalles" (~40 lineas).

## T-09: ISO regen + drill E2E (FOLLOW-UP, requiere Windows)

- [ ] `bash build.sh 5.3.0` regenera ISO con `KAPE-MEDIA-v5.3.0.iso`.
- [ ] Verificar SHA-256 final del ISO.
- [ ] Drill E2E en Windows VM:
  - [ ] Doble clic DFIR_UPLOADER_UI.exe → UAC prompt aparece.
  - [ ] Sync con URL valida → log dice `[+] Config OK`.
  - [ ] Health → HEAD 200/404 esperado.
  - [ ] Browse a `E:\DFIR-OUTPUT\` → checkboxes poblados.
  - [ ] Upload archivo 1 KB → single PUT, HEAD verify OK.
  - [ ] Upload archivo 100 MB → multipart, 13 partes, HEAD verify OK.
  - [ ] Cancel mid-upload (50%) → AbortMultipartUpload llamado, no quedan multipart huerfanos.
  - [ ] Audit log escrito en disco + sidecar en bucket.
- [ ] Verificar en consola S3 que los archivos aparecen con los keys correctos.

## T-10: Cleanup

- [ ] Borra el .bak si quedo (DfirUploaderMain.cs.bak o similar).
- [ ] Verifica que no quedan referencias a codigo viejo (comma-syntax errors).
- [ ] Confirma que SHA-256SUMS.txt actualizado y consistente con el binario real.