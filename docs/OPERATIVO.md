# PROCEDIMIENTO OPERATIVO — CyboltDFIR Triage

Adquisición de evidencia DFIR en equipos Windows comprometidos
(ransomware / intrusión / actividad interna).

> Este documento es para el **técnico que ejecuta** la adquisición.
> Si sos analista y recibís la evidencia, andá directo a la §6 (cadena de
> custodia) y §7 (verificación).
>
> Para development, ver `AGENTS.md`.

---

## 0. Versión del paquete

```
CYBOLT-DFIR-TRIAGE v0.4.0
```

| Componente | Qué es |
|---|---|
| `CYBOLT_DFIR_TRIAGE.exe` | Wizard de adquisición (KAPE + RAM capture opt-in) |
| `kape.exe` + `Targets/` + `Modules/` | KAPE 1.3.0.2 (Kroll Cyber) — 657 targets |
| `Tools/Memory/winpmem_mini_x64_rc2.exe` | Captura de RAM (Velocidex WinPmem v4.0.rc1) |
| `targets-full.txt` | Lista de targets a adquirir (la consume el wizard) |
| `VERSION.txt`, `LICENSE`, `README.md` | Metadatos |

**Requiere:** Windows 7 SP1 / Server 2008 R2 o superior, con **.NET Framework
4.5.2** instalado (en Win7: KB2934520).

---

## 1. Conceptos: qué acquisition y por qué

El wizard hace **dos** capturas, y son independientes:

| Captura | Qué preserva | Cuándo |
|---|---|---|
| **KapeTriage** | Evidencia persistente: event logs, registro, MFT, prefetch, scheduled tasks, browser, RMM, USB | **Siempre** |
| **RAM Capture** | Evidencia volátil: procesos en ejecución, conexiones, clipboard, claves DPAPI, malware desencriptado | **Solo si la marcás** |

La captura de RAM está **opt-in y apagada por defecto**. Marcála si:

- Sospeer de malware que se desencripta en memoria
- Necesitás saber qué procesos/conexiones estaban activos
- El incidente es interno (la actividad viva es la evidencia)

Si no la marcás, el bundle de KapeTriage igual es suficiente para el triage
inicial. **Perdés** los volátiles: es una decisión del operador, no un default
del Analyst.

---

## 2. Preparación

### 2.1 Material

- El bundle `CyboltDFIR-Triage-v0.2.0.zip` descomprimido en un medio
  writable (USB formateada **NTFS**, o disco externo).
- Un medio de salida **distinto** para la evidencia (segunda USB o disco
  externo). Ideal: que el medio de la evidencia no sea visible en el equipo
  objetivo hasta que el wizard lo use.
- El hash del bundle, para verificarlo antes de usarlo.

### 2.2 Verificación de integridad (ANTES de usarlo)

```bash
shasum -a 256 CyboltDFIR-Triage-v0.2.0.zip
# Linux: sha256sum CyboltDFIR-Triage-v0.2.0.zip
```

Compará con el SHA-256 publicado en las notas del GitHub Release. Si **no**
coincide, **no lo ejecutes**: bajalo de nuevo.

### 2.3 Snapshot (solo hipervisor)

Antes de tocar nada:

1. **Snapshot de la VM** (VMware: `Snapshot` > `Take Snapshot`; Proxmox:
   `qm snapshot <vmid> pre-triage`).
2. **Apagá la red** de la VM. En VMware: *Settings* > *Network Adapter* >
   *Connected* → desmarcar. En Proxmox: `qm set <vmid> --net0 <net>,link=down`.
3. Dejá el disco de salida (VMDK USB-attached o disco virtual) conectado.

> **Nunca** trabajes sobre la VM original sin snapshot. La adquisición
> escribe en el medio de salida, pero un snapshot es la red de seguridad.

---

## 3. Flujo A — VM (VMware / Proxmox / Hyper-V)

```
[OPERADOR]                          [VÍCTIMA / VM]              [ANÁLISIS]
   │                                       │                        │
   │ 1. snapshot + red OFF                 │                        │
   │──────────────────────────────────────►│                        │
   │ 2. montar bundle (USB/CD)             │                        │
   │──────────────────────────────────────►│                        │
   │ 3. adjuntar disco de salida          │                        │
   │──────────────────────────────────────►│                        │
   │ 4. encender VM                       │                        │
   │──────────────────────────────────────►│                        │
   │                                       │ 5. wizard (5 pasos)   │
   │                                       │────────────────►       │
   │                                       │   ZIP + SHA-256       │
   │                                       │   (+ .raw opcional)   │
   │ 6. shutdown limpio                   │                        │
   │◄──────────────────────────────────────│                        │
   │ 7. detach disco de salida            │                        │
   │──────────────────────────────────────►│ (queda en el host)    │
   │ 8. attach disco a VM de análisis     │                        │
   │──────────────────────────────────────────────────────────────►│
```

**Puntos críticos**

- El medio del **bundle** (kape.exe, Targets/) y el medio de la **evidencia**
  son roles distintos. El wizard stagea el primero al segundo, así que
  ambos medios deben ser escribibles.
- **Shutdown limpio**, nunca "Power Off". Un power off destructivo deja el
  filesystem en estado inconsistente y puede perder evidencia.

---

## 4. Flujo B — Host físico

En un host físico el operador está en la misma máquina que el usuario
(sospechoso o no). Cambian las prioridades:

1. **Aislar la red primero**: desconectar el cable de red, o apagar el
   puerto en el switch. Es lo primero, antes de abrir el wizard.
2. **Verificar si hay alguien en la sesión**: `query session`. Si hay una
   sesión activa de un usuario que no sos vos, evaluá el riesgo antes de
   continuar. `kape.exe` es visible en el Administrador de tareas.
3. **Si podés correrlo sin que el usuario lo note**, hacelo desde la sesión
   del technician (`psexec -s -i` para SYSTEM, sin ventana en la sesión del
   usuario). La adquisición delega en un proceso visible; eso es una
   decisión consciente, no un default.
4. **RAM antes que KAPE** (si vas a capturar RAM): la RAM es lo primero que
   se pierde y lo primero que cambia.
5. **Apagá con shutdown limpio** al terminar, o dejá el equipo encendido
   hasta que el operador indique.

---

## 5. Uso del wizard (paso a paso)

### Paso 1 — Bienvenida

- **Capturar RAM después de KapeTriage** → dejalo desmarcado salvo que
  (§1) digas lo contrario. Se puede volver atrás con `< Back` para cambiarlo.
- **Lanzar vía cmd.exe wrapper** → marcalo solo si el equipo es un server y
  KAPE escupe spam de "Unable to update Console Title" (ruido del runtime
  .NET, no afecta la captura).

Click **Next**.

### Paso 2 — Detección y parámetros

Acá se define **qué se adquisición** y **dónde se deja**:

| Campo | Qué es | Default |
|---|---|---|
| **Disco de entrada (input)** | Volumen del que KAPE copia (`--tsource`) | `C:` |
| **Carpeta de salida (output)** | Carpeta donde se escribe ZIP, `.raw` y staging | autodetectada |

**Carpeta de salida** — el default sugerido es, en orden:

1. el primer disco removible (USB) → `<usb>\DFIR-OUTPUT`
2. `C:\DFIR-OUTPUT`

No se exige un disco con un label particular: cualquier carpeta writable
sirve. Si el default no es el que querés, click **Browse…** (podés crear una
carpeta nueva) o escribila a mano en el TextBox. **Importante:** la carpeta
tiene que estar en un medio **distinto** al del sistema, o la evidencia se
mezcla con el disco en adquisición.

El resto del panel muestra la carpeta del bundle (`kape.exe` junto al `.exe`),
RAM física y espacio libre de la carpeta de salida. **Next** queda bloqueado
si falta el bundle, el disco de entrada o la carpeta de salida.

### Paso 3 — KapeTriage

Opcional: click en el link **comando ▸** para ver/editar los args de KAPE
(default = set full de `targets-full.txt` + `--vss`, con paths reales).
**Reset** vuelve al default. Si el panel está abierto, ▶ Ejecutar usa lo
editado; si está cerrado, el default full.

Click **▶ Ejecutar KapeTriage**. Dura entre 10 y 40 minutos según el disco.

Durante la corrida el log muestra los targets procesados y los **skipeados**
(los que no existen en el bundle — el filtro es defensivo, no rompe la
corrida).

Al terminar: path del ZIP, tamaño, SHA-256 y tiempo.

### Paso 4 — RAM Capture (solo si la marcaste)

Click **▶ Ejecutar RAM Capture**. 5–30 minutos según la RAM física.

Criterio de éxito: el `.raw` mide **≥ 95% de la RAM física**, aunque `winpmem`
salga con exit code != 0. Es un comportamiento esperado (bug conocido de
WinPmem) y **no** significa que la captura falló — el wizard lo reporta como
éxito y calcula el SHA-256 igual.

### Paso 5 — Resumen

Paths, tamaños y SHA-256 de cada artefacto. **Anotá estos hashes**: son la
cadena de custodia (§6).

---

## 6. Cadena de custodia

Al terminar, registrá:

| Campo | Dónde está |
|---|---|
| Hostname del equipo | Paso 5 del wizard (primera línea) |
| Fecha/hora de la adquisición | Paso 5 del wizard |
| Versión del bundle | `VERSION.txt` del bundle |
| SHA-256 del ZIP de KapeTriage | Paso 5 del wizard |
| SHA-256 del `.raw` (si hubo) | Paso 5 del wizard |
| Hash del bundle usado | Release de GitHub (§2.2) |
| Quién ejecutó / quién autorizó | Tu registro de incidente |

El wizard **no** escribe un log en disco: el log completo queda en el textbox
de la UI. **Copiá el log a un archivo antes de cerrar** si lo vas a necesitar
en el ticket.

### Entrega de la evidencia

**Preferido — VMDK (VM):** detach del disco de salida de la VM y re-attach en
la VM de análisis. Preserva el filesystem tal cual (journal de NTFS,
timestamps de $MFT, ADS). Cero copias, cadena de custodia impecable.

**Alternativa — medio extraíble (físico):** desconectar el medio y entregarlo
en mano o por courier.

**Subida remota:** en una VM/equipo **con red**, usar
`cybolt-dfir-uploader` (repo hermano) para subir el ZIP a S3 / FTP / SFTP /
SMB según el config que te pase el operador. Nunca subas desde el equipo
comprometido.

---

## 7. Verificación

En la máquina de análisis, contra el artefacto recibido:

```bash
# macOS / Linux
shasum -a 256 HOST-KAPE-20261026-120000.zip

# Windows (PowerShell)
Get-FileHash .\HOST-KAPE-20261026-120000.zip -Algorithm SHA256
```

El hash tiene que coincidir **exactamente** con el anotado en §6. Si no
coincide, **no lo abras**: escalá al responsable del incidente. Puede ser un
error de transferencia o una alteración — no se puede distinguir sin el
registro de la cadena de custodia.

---

## 8. Troubleshooting

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| "KAPE no generó ZIP" | El ZIP se escribe dentro de `--tdest`; verificá que la carpeta de salida exista y sea escribible | Revisá la carpeta de salida. Si el `.zip` no está, KAPE falló: mirá el log de KAPE en el textbox |
| "Access is denied: [X:]" al arrancar | `kape.exe` en medio read-only | El wizard stagea solo. Si lo corriste a mano, copiá `kape.exe` + `Targets/` a `C:\KAPE\` |
| Exit code != 0 en winpmem | Comportamiento conocido de WinPmem | **No es error** si el `.raw` mide ≥ 95% de la RAM. El wizard ya lo trata como éxito |
| Spam "Unable to update Console Title" | Runtime .NET sin consola válida | Marcá el checkbox *cmd.exe wrapper* en el paso 1 |
| Next bloqueado en el paso 2 | Falta bundle (`kape.exe`), disco de entrada o carpeta de salida | El bundle se detecta en la carpeta del `.exe`. Re-descomprimí el ZIP completo |
| La carpeta de salida no es la deseada | Default apunta al primer USB o a `C:\DFIR-OUTPUT` | Usá **Browse…** o escribí la ruta a mano |
| El ZIP tarda mucho | Disco grande o `--vss` procesando shadow copies | Normal. 10–40 min. No lo mates |
| Espacio insuficiente | La RAM no entra en el medio de salida | Usá un medio más grande o desactivá la captura de RAM |
| KAPE ignora targets | Un target del `.txt` no existe en el bundle | Es esperado: el filtro los skipea y lo reporta en el log. No es error |

---

## 9. Checklist de entrega

```
[ ] Hash del bundle verificado contra el Release
[ ] Snapshot tomado (si es VM) / red aislada (físico)
[ ] Bundle y medio de salida en dispositivos distintos
[ ] Disco de entrada correcto elegido en el paso 2
[ ] Carpeta de salida correcta elegida en el paso 2
[ ] RAM capture: marcada o descartada conscientemente
[ ] Wizard terminó OK (paso 5)
[ ] SHA-256 de cada artefacto anotado
[ ] Log de la consola copiado a archivo
[ ] Shutdown limpio
[ ] Evidencia entregada por VMDK o medio extraíble
[ ] Hash verificado en la máquina de análisis
[ ] Config remoto y credenciales rotados (si hubo subida)
```

---

Licencia: MIT, "Copyright (c) 2026 Cybolt".
