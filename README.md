# KAPE-MEDIA

[![Version](https://img.shields.io/badge/version-v5.2.18-blue.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)]()
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20%7C%20Server%202012%2B-lightgrey.svg)]()
[![KAPE](https://img.shields.io/badge/KAPE-1.3.0.2-orange.svg)]()

ISO offline no-booteable para adquisicion DFIR (Digital Forensics and
Incident Response) en VMs Windows comprometidas. Contiene KAPE 1.3.0.2,
winpmem, scripts de recoleccion y dos GUIs WinForms (.NET 4.5.2).

Desarrollado por **Cybolt**. Licencia MIT.

---

## Que hace

KAPE-MEDIA automatiza la captura de evidencia forense de una VM Windows
infectada (ransomware, intrusion, etc.) y la entrega en un disco de
salida dedicado que viaja entre VMs como contenedor forense. Todo se
ejecuta sin red, preservando la cadena de custodia.

### Componentes principales

| Archivo | Tamano | Descripcion |
|---|---|---|
| `kape.exe` | 6.7 MB | Kroll KAPE 1.3.0.2 CLI |
| `KAPE_TRIAGE_UI.exe` | 43 KB | GUI WinForms v5.2.13 para KapeTriage (registry, $MFT, event logs, prefetch, Amcache, scheduled tasks, $LogFile, $UsnJrnl) |
| `RAM_CAPTURE_UI.exe` | 33 KB | GUI WinForms v5.2.18 para captura de RAM con winpmem (procesos vivos, conexiones, credenciales) |
| `Tools/Memory/winpmem_mini_x64_rc2.exe` | 527 KB | Velocidex WinPmem v4.0.rc1, firmado Microsoft |
| `Tools/Memory/CAPTURE_RAM.cmd` | 4.5 KB | Fallback batch si la GUI no aplica |
| `Tools/kape.zip` | 553 MB | Distribucion KAPE completa original (incluye Modules/bin/ con EvtxECmd, MFTECmd, PECmd, etc.) |
| `MANUAL_OPERATIVO.txt` | ~22 KB | Manual operativo en espanol |
| `CHECK_COMPAT.cmd` | ~14 KB | Pre-flight de compatibilidad |
| `KAPE_TRIAGE.cmd` | ~14 KB | Equivalente batch de la GUI de triage |
| `LICENSE` | 1 KB | MIT License (Copyright (c) 2026 Cybolt) |
| `VERSION.txt` | ~13 KB | Changelog y componentes |
| `SHA256SUMS.txt` | 1 KB | Hashes de archivos criticos |

Tamano total del ISO: **~562 MiB** (588,883,968 bytes).

---

## Quick start

### 1. Subir ISO al datastore del hipervisor

Web UI de vCenter/ESXi (recomendado):

1. Datacenter -> [cluster] -> [nodo] -> datastore1
2. Storage -> Upload -> ISO file
3. Seleccionar `KAPE-MEDIA-v5.2.18.iso`

### 2. Adjuntar a la VM objetivo

Con la VM objetivo **prendida** (sin reboot):

1. vCenter: VM-A -> Edit Settings
2. New device -> CD/DVD Drive -> Datastore ISO File -> `KAPE-MEDIA-v5.2.18.iso`
3. New device -> Existing Hard Disk -> `DFIR-OUTPUT.vmdk` (SCSI 0:1)
4. Network Adapter 1 -> Connected: **desconectado**

### 3. Ejecutar la captura

Dentro de la VM, como Administrator:

1. Abrir `D:\RAM_CAPTURE_UI.exe` -> Iniciar captura de RAM (~5-15 min)
2. Abrir `D:\KAPE_TRIAGE_UI.exe` -> Ejecutar adquisicion (~10-30 min)
3. Anotar contexto en `E:\NOTES\`

### 4. Apagar y mover disco

1. Shutdown limpio desde dentro (cmd admin: `shutdown /s /t 0`) o desde vCenter (Shut Down Guest OS). **Nunca power-off forzado.**
2. vCenter: VM-A -> Edit Settings -> Remove SCSI Hard Disk 2 (Remove from VM, NO Delete)
3. Adjuntar el mismo disco a la siguiente VM objetivo (repetir pasos 3-4 para cada VM)
4. Finalmente mover a una VM de custodia o datastore IR

Para mas detalle (incluyendo troubleshooting y cleanup), ver `MANUAL_OPERATIVO.txt`
incluido en el ISO o el repo.

---

## Verificacion de integridad

```bash
# Hash del ISO (debe coincidir con VERSION.txt)
shasum -a 256 KAPE-MEDIA-v5.2.18.iso
# Esperado: 51f4a3172073f10e230c9eae273a57266b9e2c45f502ccb555c13af06f6d9978

# Hash de los archivos dentro del ISO
# Montar la ISO primero, luego:
shasum -a 256 kape.exe KAPE_TRIAGE_UI.exe RAM_CAPTURE_UI.exe \
              Tools/kape.zip Tools/Memory/winpmem_mini_x64_rc2.exe \
              Tools/Memory/CAPTURE_RAM.cmd LICENSE
```

Todos los hashes estan listados en `SHA256SUMS.txt`.

---

## Builds (opcional, para reproducir)

El proyecto incluye el codigo fuente de las dos GUIs:

- `KapeUi/Program.cs` - GUI de KapeTriage (~1600 lineas, .NET 4.5.2)
- `RamCaptureUi/RamCaptureMain.cs` - GUI de captura de RAM (~700 lineas, .NET 4.5.2)

Para compilar:

```bash
dotnet build KapeUi/KapeUi.csproj -c Release
dotnet build RamCaptureUi/RamCaptureUi.csproj -c Release
```

Los binarios resultantes van a `bin/Release/`. Copiar al staging y
regenerar el ISO con:

```bash
hdiutil makehybrid -joliet -iso -no-emul-boot -no-boot \
    -o KAPE-MEDIA-v5.2.18.iso staging/    # macOS
```

(En Linux: `genisoimage -R -J -o KAPE-MEDIA-v5.2.18.iso staging/`)

---

## Compatibilidad

- **Windows 11 / 10 / 8.1 / 8**: OK
- **Windows 7 SP1**: OK si .NET 4.5.2 instalado
- **Server 2022 / 2019 / 2016 / 2012 R2 / 2012**: OK
- **Server 2008 R2 SP1**: OK si .NET 4.5.2 instalado
- **XP / Vista**: NO SOPORTADO

---

## Licencia

MIT License - Copyright (c) 2026 Cybolt.

Ver archivo `LICENSE` para el texto completo. Se permite uso comercial,
modificacion, distribucion y uso privado. La unica obligacion es mantener
el aviso de copyright.

Atribucion esperada: **"By Cybolt, MIT License"**.

---

## Contacto

- **Sitio**: [cybolt.io](https://cybolt.io) (referencial)
- **Issues**: abrir ticket en el repo
- **Email**: ver perfil del maintainer

Para adquisiciones DFIR de emergencia o respuestas a incidentes
mayores, contactar al equipo IR de Cybolt directamente.
