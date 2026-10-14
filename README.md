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

1. Detecta el entorno (letras de unidad, RAM, etc)
2. Stagea kape.exe y Targets/ a disco writable
3. Corre KapeTriage con --vss para incluir Shadow Copies
4. Captura RAM (opt-in, default OFF)
5. Calcula SHA-256 del ZIP resultante
6. Muestra el resultado y opciones de upload

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
