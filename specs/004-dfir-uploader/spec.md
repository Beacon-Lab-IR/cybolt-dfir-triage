# Spec — DFIR_UPLOADER_UI (KAPE-MEDIA v5.3.0)

## Contexto

`KAPE-MEDIA-v5.x.iso` se entrega a clientes en drills DFIR para adquisicion offline de VMs Windows afectadas. Hasta v5.2.20, el flujo termina con la captura (KAPE ZIP + RAM .raw) en `E:\DFIR-OUTPUT\` (label `DFIR_OUTPUT`, montado en `DFIR-OUTPUT.vmdk`). La entrega al equipo DFIR se hace **a mano**: detach del VMDK, attach a una VM con internet, subida manual a Mega.nz o similar.

**Problema**: ese ultimo paso es friccion alta, requiere que el operador intervenga en la maquina del cliente o que coordine una sesion remota. El resultado: o se sube sin verificar (cadena de custodia fragil) o no se sube (entrega lenta, evidencia esperando en disco dias).

## Goal

Nueva GUI `DFIR_UPLOADER_UI.exe` que el cliente corre en su propia VM con la ISO montada + `DFIR-OUTPUT.vmdk` adjuntado. La GUI sube los artefactos capturados a un bucket S3-compatible **con cadena de custodia verificable**. El operador (Beacon-Lab) controla las credenciales desde un config JSON remoto, rotable, que el cliente nunca ve en claro.

## User stories

### US-1 — Operador entrega el ISO al cliente

El operador le pasa al cliente `KAPE-MEDIA-v5.3.0.iso`. El cliente monta la ISO en su VM y adjunta `DFIR-OUTPUT.vmdk` como segundo disco (mismo flujo que v5.2.x).

### US-2 — Operador prepara el config remoto

Antes de empezar el drill, el operador edita a mano `config-template.json` con valores reales y lo sube a un endpoint HTTPS (gist de GitHub, `beaconlab.us/uploads/<token>.json`). El config incluye:
- Access Key ID + Secret Access Key de un IAM user con policy scoped a `s3:PutObject` sobre `clients/<id>/*` del bucket.
- Bucket, endpoint, region.
- Prefijo del cliente (ej: `clients/acme-corp/incident-2026-10-08/`).
- `expires_at` — vence a las pocas horas.

### US-3 — Cliente corre KAPE y captura como siempre

Sin cambios respecto a v5.2.20: doble clic `KAPE_TRIAGE_UI.exe` → KapeTriage. Doble clic `RAM_CAPTURE_UI.exe` (opcional) → captura RAM.

### US-4 — Cliente sube la evidencia con doble clic

Doble clic `DFIR_UPLOADER_UI.exe`. Prompt UAC. UI carga:
1. Operador (cliente en este caso, pero quien esta frente a la VM) ingresa la URL del config que le paso el operador.
2. Click `🔄 Sync`. UI trae el JSON, valida cert HTTPS + expiracion, muestra bucket + prefix + minutos restantes.
3. Click `✓ Health`. UI hace un HEAD contra el bucket con las credenciales — confirma que el bucket acepta auth (404 en key inexistente es OK).
4. Operador llena metadata del incidente (case ID, operador, fecha, notas).
5. Operador click `📁 Browse` → selecciona `E:\DFIR-OUTPUT\`. UI escanea recursivamente.
6. Operador desmarca archivos que no quiere subir. Marca `Total: N archivos, X GB`.
7. Operador click `▶ Upload`. UI barra de progreso global + status por archivo + log en vivo.
8. Al terminar: UI muestra "X OK / Y failed / Z cancelled". Audit log escrito en `E:\DFIR-OUTPUT\upload-audit-<ts>.json` y subido al bucket como sidecar.

### US-5 — Operador limpia post-incidente

El operador:
1. Descarga el ZIP de evidencia + .raw + audit del bucket via `aws s3 sync` o `mc mirror`.
2. Verifica hashes contra `audit.json`.
3. Borra el config remoto (gist delete o `rm uploads/<token>.json`).
4. Rota la IAM key en consola AWS / Mega S4 console.
5. Borra el contenido del prefix `clients/<id>/<incident>/` una vez entregado al cliente final.

## Constraints

- Binario ≤ 200 KB. Sin AWSSDK, sin NuGet. SigV4 manual en C# puro.
- Compatible con Win7 SP1 / Server 2008 R2 SP1 (con .NET 4.5.2 instalado) hasta Win11 / Server 2022. `TargetFramework=net452`.
- Manifest `requireAdministrator`. UI corre con privilegios elevados.
- HTTPS obligatorio para el config (no HTTP, no IPs crudos sin cert).
- Sin persistencia de credenciales: nada en registry, nada en `%LOCALAPPDATA%`, nada en disco. Las credenciales viven en memoria solo durante la corrida.
- Cert validation obligatoria. Nunca `--no-verify-ssl` ni `ServerCertificateValidationCallback = true`.
- ISO no-booteable, mismo formato que v5.2.20.

## Success criteria

1. Build `dotnet build DfirUploaderUi.csproj -c Release` → 0 errors, 0 warnings.
2. Binario ≤ 200 KB.
3. Drill E2E: cliente (o el operador) genera config, crea ZIPs dummy en `E:\DFIR-OUTPUT\`, ejecuta el .exe, ve progreso, ve audit log escrito local + sidecar en bucket.
4. Cancel mid-upload: `AbortMultipartUpload` llamado, no quedan multipart huérfanos en el bucket.
5. SHA-256 calculado correctamente (verificable con `certutil -hashfile` o re-calculo con el .exe).
6. Audit log escrito + subido, contiene todos los archivos con sus hashes + ETags + status.

## Out of scope

- Delete-after-upload desde la GUI. El operador lo hace a mano desde la consola del bucket.
- Bucket browser / file manager UI.
- Multi-bucket / multi-config por corrida.
- Encriptacion client-side. SSE-S3 / SSE-KMS server-side.
- Mega.nz native API (WebDAV). Solo S3-compatible.
- Mac/Linux client. Solo Windows.
- Persistencia de credenciales en disco.
- Rotacion automatica de keys.
- GUI para editar el config (se edita con cualquier editor externo).