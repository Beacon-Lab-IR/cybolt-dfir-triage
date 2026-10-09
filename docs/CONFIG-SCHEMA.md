# DFIR_UPLOADER_UI — Config Schema (v2, multi-protocolo)

## Overview

El `config.json` es el archivo que el operador (Beacon-Lab) hospeda y le
pasa al cliente. La GUI trae el JSON (vía HTTPS URL **o** archivo local),
valida cert HTTPS + expiracion, autodetecta protocolo y procede.

**Importante**: el JSON contiene credenciales. **NO** commitear al repo
con valores reales.

v2 (actual): multi-protocolo. **Auto-detecta** del URL scheme o de los
campos presentes. Compatible hacia atras con v1 (S3-only).

## Ubicacion del config

El operador puede hostearlo de **dos formas**:

1. **HTTPS URL** (recomendado): gist de GitHub o `beaconlab.us/uploads/<token>.json`.
2. **Archivo local** (alternativa): grabado en USB / pendrive / carpeta
   compartida. La GUI lo lee directo del disco si la URL no empieza con `https://`.

   Ejemplo de path local que el operador le pasa al cliente:
   `E:\DFIR-OUTPUT\config.json` o `\\fileserver\transfer\config.json` (UNC).

## Schema completo

```json
{
  "version": 2,
  "protocol": "s3" | "ftp" | "sftp" | "smb",

  "expires_at": "<ISO-8601-UTC>",

  // === S3 ===
  "bucket": "<bucket-name>",
  "endpoint": "https://<endpoint>",
  "region": "<aws-region>",
  "prefix": "<key-prefix-with-trailing-slash>",
  "access_key_id": "<IAM-access-key-id>",
  "secret_access_key": "<IAM-secret-access-key>",
  "session_token": "<optional-sts-token>",
  "chunk_size_bytes": 8388608,

  // === FTP ===
  "ftp_host": "<hostname>",
  "ftp_port": 21,
  "ftp_username": "<user>",
  "ftp_password": "<password>",
  "ftp_path": "/remote/path/",
  "ftp_passive": true,
  "ftp_ssl": false,

  // === SFTP ===
  "sftp_host": "<hostname>",
  "sftp_port": 22,
  "sftp_username": "<user>",
  "sftp_password": "<password>" | null,
  "sftp_key_path": null,
  "sftp_path": "/remote/path/",

  // === SMB ===
  "smb_share": "\\\\server\\share",
  "smb_username": "DOMAIN\\user",
  "smb_password": "<password>",
  "smb_path": "uploads\\incident-2026-10-08\\"
}
```

## Campos comunes

### `version` (int, required)

Version del schema. Hoy `2`. Si cambia, subir este numero.

### `protocol` (string, optional, auto-detect)

`s3` | `ftp` | `sftp` | `smb`. Si falta, autodetect del URL scheme:
- `s3://` o `https://` → `s3`
- `sftp://` → `sftp`
- `ftp://` o `ftps://` → `ftp`
- `smb://` o `\\server\share` → `smb`

O por presencia de campos especificos (ftp_host, sftp_host, smb_share).

### `expires_at` (string, required, ISO-8601 UTC)

Fecha/hora de expiracion. Margen minimo 30 min. Recomendado 4-8 horas.

## Protocolo S3 (campos requeridos si protocol=s3)

- **`bucket`**: nombre del bucket.
- **`endpoint`**: URL HTTPS del endpoint S3. **Debe ser HTTPS**.
- **`region`**: region AWS (ej `us-east-1`). Usada para SigV4.
- **`prefix`**: prefijo del cliente, debe terminar en `/`.
- **`access_key_id`**: IAM Access Key ID.
- **`secret_access_key`**: IAM Secret Access Key.
- **`session_token`**: opcional, para STS temp creds.
- **`chunk_size_bytes`**: default 8388608 (8 MiB).

### IAM policy recomendada (S3)

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": ["s3:PutObject", "s3:GetObject", "s3:ListBucket",
               "s3:AbortMultipartUpload", "s3:ListMultipartUploadParts"],
    "Resource": [
      "arn:aws:s3:::<bucket>/<prefix-cliente>/*",
      "arn:aws:s3:::<bucket>"
    ]
  }]
}
```

## Protocolo FTP (campos requeridos si protocol=ftp)

- **`ftp_host`**: hostname o IP del server FTP.
- **`ftp_port`**: default 21. Para FTPS implicito usar 990.
- **`ftp_username`**: usuario.
- **`ftp_password`**: password.
- **`ftp_path`**: directorio destino (ej `/uploads/incident-2026-10-08/`).
  Si falta, default `/`. **Debe terminar en `/`**.
- **`ftp_passive`**: default true. Passive mode recomendado para NAT/firewall.
- **`ftp_ssl`**: default false. true = FTPS explicito (TLS en puerto 21).
  Nota: FTPS pasivo con TLS es el standard recomendado hoy (FTP plano es
  inseguro, no usar para datos de clientes).

## Protocolo SFTP (campos requeridos si protocol=sftp)

- **`sftp_host`**: hostname o IP.
- **`sftp_port`**: default 22.
- **`sftp_username`**: usuario.
- **`sftp_password`** O **`sftp_key_path`**: al menos uno. key_path tiene
  prioridad si ambos estan. key_path es ruta local al archivo de private
  key (RSA/Ed25519), formato OpenSSH o PuTTY ppk.
- **`sftp_path`**: directorio destino en el server.

### Requisitos runtime

`sftp.exe` debe estar en PATH. Windows 10+ lo trae via "OpenSSH Client"
(Optional Features). Alternativa: PuTTY `psftp.exe` si el operador lo
staged en la ISO bajo `Tools\SFTP\psftp.exe` (no incluido por default).

## Protocolo SMB (campos requeridos si protocol=smb)

- **`smb_share`**: UNC path del share, ej `\\fileserver\uploads$`.
- **`smb_username`**: usuario con acceso, formato `DOMAIN\user`.
- **`smb_password`**: password.
- **`smb_path`**: subdirectorio dentro del share. Default `\`.

### Implementacion

La GUI hace `net use Z: \\server\share /user:DOMAIN\user password`,
despues `File.Copy` al UNC path, al final `net use Z: /delete`. La letra
Z: es la primera libre de Z a F.

### Consideraciones

- `net use` requiere shell interactivo. El .exe WinForms corre como usuario
  con shell, OK.
- SMB 2.0+ solamente (Windows 7+).
- Credenciales en linea de comandos de `net use` son visibles via WMI
  (`Get-WmiObject Win32_Process`). Para DFIR drill aceptable; para prod
  usar Kerberos.

## Ejemplos completos

### S3 (AWS o Mega S4)

```json
{
  "version": 2,
  "expires_at": "2026-10-08T23:00:00Z",
  "bucket": "inc-lena",
  "endpoint": "https://inc-lena.s3.g.megas4.com",
  "region": "us-east-1",
  "prefix": "clients/acme/incident-2026-10-08/",
  "access_key_id": "AKIA...",
  "secret_access_key": "...",
  "chunk_size_bytes": 8388608
}
```

### FTP (servidor plain FTP o FTPS)

```json
{
  "version": 2,
  "expires_at": "2026-10-08T23:00:00Z",
  "ftp_host": "ftp.example.com",
  "ftp_port": 21,
  "ftp_username": "uploader",
  "ftp_password": "...",
  "ftp_path": "/uploads/acme/2026-10-08/",
  "ftp_passive": true,
  "ftp_ssl": true
}
```

### SFTP

```json
{
  "version": 2,
  "expires_at": "2026-10-08T23:00:00Z",
  "sftp_host": "sftp.example.com",
  "sftp_port": 22,
  "sftp_username": "uploader",
  "sftp_key_path": "C:\\Users\\lab\\.ssh\\id_ed25519",
  "sftp_path": "/uploads/acme/2026-10-08/"
}
```

### SMB (file share corporativo)

```json
{
  "version": 2,
  "expires_at": "2026-10-08T23:00:00Z",
  "smb_share": "\\\\fileserver\\uploads$",
  "smb_username": "LAB\\uploader",
  "smb_password": "...",
  "smb_path": "acme\\2026-10-08\\"
}
```

## Generacion del config

```bash
# Editar el template
cp config-template.json config.json
$EDITOR config.json

# Validar JSON
python3 -c "import json; json.load(open('config.json'))"

# Subir a gist (o beaconlab.us, o dejarlo en USB para carga local)
gh gist create --public=false config.json
```

## Cleanup post-incidente

- Borrar config remoto / archivo USB.
- Rotar credenciales (IAM key, FTP password, SSH key, SMB password).
- Borrar contenido del destino (`aws s3 rm`, `rm` en FTP/SFTP, UNC `del`).