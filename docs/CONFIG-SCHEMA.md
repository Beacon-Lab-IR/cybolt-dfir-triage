# DFIR_UPLOADER_UI — Config Schema (v1)

## Overview

El `config.json` es el archivo remoto que el operador (Beacon-Lab) hostea en
HTTPS y le pasa al cliente la URL. El cliente la ingresa en `DFIR_UPLOADER_UI.exe`
+ click `🔄 Sync`. La GUI trae el JSON, valida cert HTTPS + expiracion, y
procede a upload con esas credenciales.

**Importante**: el JSON contiene `access_key_id` y `secret_access_key`. Esas credenciales dan acceso de escritura al bucket. El operador:

1. **Crea IAM key con policy scoped** a `s3:PutObject` + `AbortMultipartUpload`
   sobre `arn:aws:s3:::<bucket>/<prefix-cliente>/*` ONLY.
2. **Sube el config** a un endpoint HTTPS (gist de GitHub, `beaconlab.us/uploads/<token>.json`).
3. **Le pasa la URL al cliente** (por Slack, ticket, mail, etc).
4. **Post-incidente**: borra el config del endpoint + rota la IAM key en la consola.

El .exe **nunca persiste** el config en disco. Solo vive en memoria durante la corrida.

## Schema completo

```json
{
  "version": 1,
  "bucket": "<bucket-name>",
  "endpoint": "https://<endpoint>",
  "region": "<aws-region>",
  "prefix": "<key-prefix-with-trailing-slash>",
  "expires_at": "<ISO-8601-UTC>",
  "access_key_id": "<IAM-access-key-id>",
  "secret_access_key": "<IAM-secret-access-key>",
  "session_token": "<optional-sts-token>",
  "chunk_size_bytes": 8388608
}
```

## Campos

### `version` (int, required)

Version del schema. Hoy `1`. Si cambia el formato del JSON en el futuro,
subir este numero y el .exe va a rechazar configs con version != 1.

### `bucket` (string, required)

Nombre del bucket S3-compatible. Ej: `inc-lena`. **No** incluir el endpoint
completo aca — eso va en `endpoint`.

### `endpoint` (string, required, HTTPS only)

URL completa del endpoint S3. **Debe ser HTTPS** (cert validation obligatoria).
El .exe rechaza con error si es `http://`.

Ejemplos validos:
- AWS S3: `https://s3.us-east-1.amazonaws.com`
- Mega S4: `https://inc-lena.s3.g.megas4.com`
- Wasabi: `https://s3.us-east-1.wasabisys.com`
- Backblaze B2 (S3-compatible): `https://s3.us-west-002.backblazeb2.com`
- MinIO self-hosted: `https://minio.example.com:9000`

### `region` (string, required)

Region S3. Ej: `us-east-1`. **No se usa** para el endpoint (el endpoint ya
incluye la region o no), sino para la firma SigV4 (`<date>/<region>/s3/aws4_request`).

### `prefix` (string, required, ends with `/`)

Prefijo bajo el cual se suben los artefactos. **Debe terminar en `/`**. Si no
termina en `/`, el .exe lo agrega automaticamente.

Recomendado: `clients/<cliente>/<incidente-id>/` (ej:
`clients/acme-corp/incident-2026-10-08/`). Esto facilita cleanup post-incidente
(un `aws s3 rm --recursive` sobre el prefix).

### `expires_at` (string, required, ISO-8601 UTC)

Fecha/hora de expiracion del config en formato ISO-8601 UTC. Ej:
`2026-10-08T23:00:00Z`. El .exe rechaza el config si faltan menos de **30 minutos**
para la expiracion. Esto da un margen para que la corrida termine antes de
que el config quede invalido.

Recomendado: expirar 4-8 horas despues del inicio del drill. No mas de 24h.

### `access_key_id` (string, required)

IAM Access Key ID. Empieza tipicamente con `AKIA` (IAM user) o `ASIA` (STS temp).

### `secret_access_key` (string, required)

IAM Secret Access Key. Mantener secreta. **El operador la unica vez que la
ve** es cuando la crea o la rota en la consola IAM. Despues vive solo en el
config.json hosteado.

### `session_token` (string, optional)

Solo si usas credenciales temporales STS (AWS STS, IAM role, EC2 instance
profile, etc). Si usas IAM user con key permanente, dejar `null`.

Si esta presente, el .exe lo agrega como `x-amz-security-token` header en
todas las requests firmadas.

### `chunk_size_bytes` (int, optional, default 8388608)

Tamanho de cada parte en multipart upload en bytes. Default **8 MiB**
(8388608 bytes). Minimo S3 = 5 MiB. Maximo = 5 GiB.

Recomendaciones:
- Default 8 MiB esta OK para bandwidth ~10-50 Mbps.
- Subir a 16-32 MiB si bandwidth >= 100 Mbps y archivos > 1 GB.
- Bajar a 5-6 MiB si tenes poca memoria RAM en la VM cliente (cada parte se
  bufferea en memoria durante el PUT).

## IAM policy recomendada para el key

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
      "arn:aws:s3:::<bucket>/<prefix-cliente>/*",
      "arn:aws:s3:::<bucket>"
    ]
  }]
}
```

Reemplazar `<bucket>` y `<prefix-cliente>` con los valores reales. Esto limita
el key a solo escribir en el prefix del cliente actual — no puede tocar otros
prefixes del bucket ni otros buckets.

**Importante**: NO incluir `s3:DeleteObject` en la policy. La limpieza la hace
el operador a mano, no el .exe.

## Generacion del config

El operador edita a mano el archivo, completa los campos, y lo sube. Ejemplo
de comando para gist de GitHub:

```bash
# Editar el template
cp config-template.json config.json
$EDITOR config.json

# Validar JSON
python3 -c "import json; json.load(open('config.json'))"

# Subir como gist secreto (cualquier con link lo ve)
gh gist create --public=false config.json
# Devuelve URL tipo https://gist.github.com/<user>/<id>
# Para URL raw: https://gist.githubusercontent.com/<user>/<id>/raw/<file>.json

# O subir a beaconlab.us
scp config.json user@beaconlab.us:/var/www/uploads/$(uuidgen).json
```

El operador le pasa al cliente la URL raw (gist o beaconlab.us). El cliente
la pega en la GUI + click Sync.

## Cleanup post-incidente

```bash
# Borrar el gist
gh gist delete <gist-id>

# O borrar el archivo en beaconlab.us
ssh user@beaconlab.us rm /var/www/uploads/<token>.json

# Rotar la IAM key en consola (AWS / Mega S4 / etc)
# La key queda invalidada, configs viejos en cualquier lugar quedan inutilizables
aws iam delete-access-key --access-key-id AKIA...

# Borrar el contenido del bucket
aws s3 rm s3://<bucket>/<prefix-cliente>/ --recursive
```

## Que NO hacer

- **NO** commitear el config.json con valores reales al repo (git history
  publico = IAM key leak). El `.gitignore` debe excluirlo.
- **NO** compartir el config.json por canales inseguros (HTTP, mail sin TLS).
- **NO** poner la IAM key con `s3:*` (admin total). Usar la policy scoped de arriba.
- **NO** dar el mismo config a dos clientes distintos (uno ve lo que el otro
  subio). Cada cliente = config nuevo con prefix distinto.
- **NO** dejar el config en el endpoint mas alla de la expiracion.