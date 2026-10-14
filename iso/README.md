# Rockwell FactoryTalk — KAPE Triage 2-PASS (DFIR)

Bundle para hacer triage de un host Windows con Rockwell FactoryTalk (HMI server,
engineering workstation, o PC con FT View ME) usando KAPE en dos pasadas.

**La USB puede caer en cualquier letra.** El .cmd acepta la letra como param.

---

## Setup (en cualquier USB)

Poner el bundle completo en una carpeta llamada `KAPE\` en la raíz de la USB:

```
E:\KAPE\                              (cualquier letra, D, E, F, G, etc.)
├── kape.exe                              ← de kape.zip oficial
├── KAPE_TRIAGE_ROCKWELL.cmd              ← este bundle, entry point
├── README.md
├── RockwellFactoryTalk.tkape             ← del gist
├── FactoryTalkDiagnostics.tkape          ← del gist
├── FactoryTalkViewSE_ME.tkape            ← del gist
├── FactoryTalkServices.tkape             ← del gist
├── Targets\                              ← de kape.zip (carpeta completa)
│   ├── Compound\KapeTriage.tkape
│   ├── Basic\*  (etc, lo que traiga kape.zip)
│   └── Logs\*
└── Modules\                              ← de kape.zip (carpeta completa)
    ├── KapeTriage.mkape
    ├── MFTECmd.mkape
    ├── EvtxECmd.mkape
    └── ...
```

> **Si NO tenés `Targets\` ni `Modules\`** (no extrajiste kape.zip), Pass 1 se
> auto-skippea con warning. Pass 2 igual corre.

---

## Uso

### Desde la USB (doble click)

Navegá a `E:\KAPE\` en el Explorador y doble click sobre
`KAPE_TRIAGE_ROCKWELL.cmd`. Listo.

### Desde cmd (como Administrador)

```cmd
E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd                  (usa la carpeta del .cmd)
E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E               (E: -> E:\KAPE)
E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E:              (lo mismo, con :)
E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E:\OtraCarpeta  (path custom)
```

**El parámetro puede ser:**
- (vacío) → usa la carpeta del propio .cmd
- Una letra de unidad (`E` o `E:`) → se expande a `E:\KAPE`
- Un path completo → se usa tal cual

---

## Lo que hace

| Pass | Target | Necesita | Output |
|---|---|---|---|
| **1** | `KapeTriage` (oficial) | `Targets\` + `Modules\` del kape.zip | `<HOST>-KAPE-<FECHA>.zip` + `.sha256` |
| **2** | `RockwellFactoryTalk` (custom) | Los 4 `.tkape` al lado del .cmd | `<HOST>-FTV-<FECHA>.zip` + `.sha256` |

Ambas pasadas usan `--vss` (Volume Shadow Copy snapshot) para copiar archivos
locked (event logs abiertos, displays `.ggfx` en uso por el HMI server, etc).

Pass 1 además corre los modules EZ Tools (MFTECmd, EvtxECmd, RECmd, PECmd, etc)
sobre los artefactos colectados — salida en CSVs listos para Timeline Explorer.

Si Pass 1 falla (falta `Targets\`), Pass 2 sigue corriendo.

---

## Outputs

Quedan en la **misma carpeta** donde está el .cmd:

```
E:\Forensics\KAPE\
├── <HOST>-KAPE-YYYYMMDD.zip         ← Pass 1 (si corrio)
├── <HOST>-KAPE-YYYYMMDD.zip.sha256
├── <HOST>-FTV-YYYYMMDD.zip          ← Pass 2 (siempre)
├── <HOST>-FTV-YYYYMMDD.zip.sha256
└── Modules\                         ← Output CSVs de los modules EZ Tools (Pass 1)
```

Archivos temporales de KAPE van a `C:\_kape_tmp\KAPE` y `C:\_kape_tmp\FTV`
(esto acelera la copia en USBs lentas).

---

## Validación en analyst workstation

```bash
cd /ruta/al/usb
sha256sum -c <HOST>-KAPE-YYYYMMDD.zip.sha256
sha256sum -c <HOST>-FTV-YYYYMMDD.zip.sha256
```

Ambos deben decir `OK`.

---

## Qué colecta cada target

**Pass 1 — KapeTriage (oficial):** event logs, registry hives, prefetch,
scheduled tasks, $MFT, browser history, LNK, JumpLists, recycle bin, services,
SRUM, etc + processing via MFTECmd/EvtxECmd/RECmd/PECmd/etc.

**Pass 2 — RockwellFactoryTalk (custom):**

- `C:\ProgramData\Rockwell\FTV\Logs\` — FactoryTalk Diagnostics
- `C:\ProgramData\Rockwell\Activation\` — Activation Manager
- `C:\Users\Public\Documents\RSView Enterprise\SE\HMI projects\` — FT View SE
- `C:\Users\Public\Documents\RSView Enterprise\ME\Logs\` — FT View ME (PC)
- `C:\Windows\Logs\` — FT View ME (PanelView Plus 7/6, Win CE)
- `C:\Program Files (x86)\Rockwell Software\FactoryTalk\` — FTSP services
- `C:\Program Files (x86)\Rockwell Software\FactoryTalk Linx\` — FT Linx
- `C:\Program Files\Rockwell Software\FactoryTalk Historian\` — FT Historian SE
- `C:\Users\Public\Public Documents\Rockwell Automation\Batch\` — FT Batch
- `C:\inetpub\wwwroot\*ViewPoint*` — FT ViewPoint

CVEs cubiertos: CVE-2024-45824, CVE-2024-7513, CVE-2025-24479/24480,
CVE-2025-9063/9064, CVE-2025-9066, CVE-2025-13036/44019/36539, CVE-2026-16675.

---

## Troubleshooting

| Error | Causa | Fix |
|---|---|---|
| `kape.exe no encontrado` | El .cmd no está junto a kape.exe | Mover el .cmd a la carpeta donde está kape.exe |
| `Faltan archivos: RockwellFactoryTalk.tkape...` | No están los 4 .tkape al lado | Descargar del gist y copiar al lado del .cmd |
| `[1/2] SKIP - KapeTriage.tkape no encontrado` | No extrajiste `Targets\` del kape.zip | Re-extraer kape.zip completo (con `Targets\` y `Modules\`) |
| `Access denied` en HMI projects | CVE-2024-7513 (permisos default) | Normal. El target hace raw read; lo que se pueda copiar queda. |
| PanelView Plus 7/6 con Win CE | KAPE no maneja drives Win CE | Montar SD card o usar FTP server del panel |
| AV/Defender borra kape.exe | EDR agresivo | `powershell Add-MpPreference -ExclusionPath "C:\_kape_tmp"` antes de correr |

---

## SHA-256 del bundle v2.5 (2026-10-12)

```
KAPE_TRIAGE_ROCKWELL.cmd    ce6fef2d4707a6c6dac79c8d892298d9ba22e86de76a8572b011638bfcf98f7d
RockwellFactoryTalk.tkape   7bf07ca97dfbc3d04f4a417704a78e02a7299663b8d389e5f72d51585cd496cd
FactoryTalkDiagnostics.tkape a03d150f9cf4464f146176d8ffdd42dbdc8c1d3e3d9f694aa52e9b5c5de7e6e0
FactoryTalkViewSE_ME.tkape  e004180bf314fdfb584479de9a29eba2d8a489e309c997cea36930cf97a8ae81
FactoryTalkServices.tkape   70cbc06be31f78a02d01188286a62d0f04e5ff4f9f1f01d66bccc59e5ed002aa
```

Origen: Cybolt DFIR — `Beacon-Lab-IR/cybolt-dfir-triage` (private).
