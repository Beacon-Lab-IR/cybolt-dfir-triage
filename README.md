# CyboltDFIR Triage

Wizard de adquisición forense para Windows. KAPE 1.3.0.2 + RAM capture
opt-in + uploader integrado, en un solo bundle.

## Quickstart

```bash
./build.sh                  # build v0.1.0 (default)
./build.sh 6.0.0            # build con version especifica

# Output: dist/CyboltDFIR-Triage-v<VERSION>.zip
```

## Componentes

- `DfirTriageUi/` — C# WinForms wizard (CYBOLT_DFIR_TRIAGE.exe)
- `kape.zip` — fuente de Targets/ y Modules/ (verificado por SHA-256)
- `targets-full.txt` — lista de targets a procesar (filtrado defensivo)
- `Tools/Memory/winpmem_mini_x64_rc2.exe` — RAM capture
- `VERSION.txt` — version del bundle

## Uso

El operador conecta el bundle a una VM/host Windows (USB, CD-ROM) y
ejecuta `CYBOLT_DFIR_TRIAGE.exe`. El wizard:

1. Bienvenida — opciones (captura RAM opt-in, cmd wrapper)
2. **Parametros de entrada/salida** — elegís el **disco de entrada**
   (ComboBox con los discos fijos y removibles; default `C:`) y la
   **carpeta de salida** (TextBox + `Browse…`). La carpeta se autodetecta
   (label `DFIR_OUTPUT` → USB removible → `C:\DFIR-OUTPUT`) pero siempre
   podés cambiarla. Todo lo que produce el wizard (staging, ZIP, `.raw`,
   resumen) queda en esa carpeta.
3. KapeTriage — stagea `kape.exe` + `Targets/` a la carpeta de salida y
   corre KAPE con `--vss` (incluye Shadow Copies)
4. RAM Capture (opt-in, default OFF)
5. Resumen final con SHA-256 de cada output

### Parámetros

| Parámetro | Dónde | Default | Notas |
|---|---|---|---|
| Disco de entrada | Paso 2, ComboBox | `C:` | Discos fijos + removibles. `--tsource` de KAPE |
| Carpeta de salida | Paso 2, TextBox + Browse | autodetectada | `FolderBrowserDialog`, acepta escribir a mano |
| Captura RAM | Paso 1, checkbox | OFF | Opt-in |
| cmd wrapper | Paso 1, checkbox | OFF | Para servers con spam de Console Title |

El `Browse…` es un `FolderBrowserDialog` nativo: deja navegar, crear
carpeta nueva (`ShowNewFolderButton`) y escribir la ruta a mano. El
`Re-detectar` del paso 2 respeta la carpeta elegida a mano y solo
reevalúa CD-ROM, discos de entrada y RAM.

## Releases

- [GitHub Releases](https://github.com/Beacon-Lab-IR/cybolt-dfir-triage/releases)
- Cada release genera `CyboltDFIR-Triage-v<VERSION>.zip`

## Variantes

Este proyecto es self-contained. Para el bundle completo (Triage + Uploader
dentro de un ISO booteable), ver:

- [Beacon-Lab-IR/cybolt-dfir-iso](https://github.com/Beacon-Lab-IR/cybolt-dfir-iso)
- [Beacon-Lab-IR/cybolt-dfir-uploader](https://github.com/Beacon-Lab-IR/cybolt-dfir-uploader)

## Licencia

By Cybolt, MIT License.
