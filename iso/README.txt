================================================================================
  KAPE-MEDIA - Adquisicion DFIR con doble clic
  Caso: ransomware sobre hypervisor (PVE / VMware ESXi) - VMs Windows
  afectadas, sin red durante la adquisicion.
  v5.2.18 (incluye GUI de captura de RAM con deteccion robusta de exito pese a exit != 0).
================================================================================

COMPATIBILIDAD SOPORTADA
------------------------
  Windows 11 (cualquier build actual)
  Windows 10 (1809+)
  Windows 8.1, Windows 8
  Windows 7 SP1 (con .NET 4.5.2 instalado)
  Windows Server 2022 / 2019 / 2016 / 2012 R2 / 2012
  Windows Server 2008 R2 SP1 (con .NET 4.5.2 instalado)

LO QUE TIENES QUE HACER (VERSION CORTA)
----------------------------------------
  1.  Sube KAPE-MEDIA-v5.2.18.iso al datastore del hipervisor (carpeta iso/).
  2.  En la VM afectada: monta la ISO como CD/DVD virtual.
  3.  Conecta DFIR-OUTPUT.vmdk como segundo disco. Si es la primera vez
      o el disco es nuevo: inicialo como GPT, particion NTFS, label
      "DFIR_OUTPUT". Si ya esta inicializado de una corrida anterior,
      conectalo y listo.
  4.  Enciende la VM. Espera a que cargue Windows.
  5.  Abre el Explorador de archivos, ve al CD/DVD ("KAPE-MEDIA").
  6.  Elige UNA de estas dos opciones:

      OPCION A (visual, recomendada):
        Doble clic en KAPE_TRIAGE_UI.exe
        - Windows muestra UAC. Acepta con "Si" (corre como Administrador).
        - Espera a que termine el pre-flight (veras OK en verde).
        - ComboBox "Particion de salida" debe mostrar E: (DFIR_OUTPUT).
          ComboBox "Particion fuente" debe mostrar C:.
        - Click "Ejecutar adquisicion".
        - Espera. Al terminar aparece el SHA-256 y la ruta del ZIP.

      OPCION A.5 (opcional - captura de RAM ANTES del triage):
        Doble clic en RAM_CAPTURE_UI.exe
        - Misma UAC. Misma auto-deteccion.
        - Click "Iniciar captura de RAM".
        - Espera 5-15 min. Al terminar aparece tamano + SHA-256.
        - Si la GUI no aplica (RDP limitado), usar D:\Tools\Memory\
          CAPTURE_RAM.cmd desde cmd.exe como Administrator (Opcion B-RAM).

      OPCION B (consola, fallback):
        Doble clic en CHECK_COMPAT.cmd para verificar requisitos.
        Si todo OK, doble clic en KAPE_TRIAGE.cmd.
        Aceptar UAC. Aceptar el resumen. Esperar.

  7.  Apaga la VM y desconecta DFIR-OUTPUT.vmdk (Remove from VM, NO delete).
  8.  Descarga del datastore: DFIR-OUTPUT.vmdk + DFIR-OUTPUT-flat.vmdk
      + los archivos dentro (ZIPs, hashes, NOTES).

CONTENIDO DE LA ISO
-------------------
  KAPE_TRIAGE_UI.exe       UI visual WinForms (.NET 4.5.2) - v5.2.13
  RAM_CAPTURE_UI.exe       GUI captura RAM con winpmem (WinForms) - v5.2.18
  KAPE_TRIAGE.cmd          Script batch equivalente (mismo flujo)
  CHECK_COMPAT.cmd         Verificador de requisitos (recomendado
                           ejecutar primero). Diagnostica: SO,
                           .NET Framework, PowerShell, wmic, certutil,
                           CD accesible, VMDK accesible, espacio libre.
                           Genera CHECK_COMPAT-REPORT.txt.
  autorun.inf              Documenta el arranque automatico (Win10/11
                           lo ignora por politica; doble clic sigue OK).
  kape.exe                 Kroll KAPE v1.3.0.2.
  Targets\                 Catalogo completo de KapeFiles Targets.
  Documentation\           EULA y notas del paquete original.
  MANUAL_OPERATIVO.txt     Manual paso a paso en espanol.
  SHA256SUMS.txt           Hashes SHA-256 de los archivos criticos.
  Tools\kape.zip           Distribucion KAPE completa original (553 MB).
                           Contiene kape.exe + Targets + Modules/bin/
                           (binarios de parseo: PECmd, MFTECmd, EvtxECmd,
                           JLECmd, AmcacheParser, etc.). NO es necesario
                           para adquisicion con --target, pero esta
                           disponible como referencia o si necesitas
                           algun binario de parseo in situ.
  Tools\Memory\winpmem_mini_x64_rc2.exe   Binario de captura de RAM
                           (Velocidex WinPmem v4.0.rc1, 527 KB, x64).
  Tools\Memory\CAPTURE_RAM.cmd           Wrapper batch standalone de
                           captura de RAM (fallback si la GUI no aplica).
  LICENSE                  MIT License - Copyright (c) 2026 Cybolt.

QUE HACE KAPE_TRIAGE_UI.exe (paso a paso)
-----------------------------------------
  0.  Pide elevacion a Administrador via manifest requireAdministrator.
  1.  Auto-detecta la unidad del CD (la carpeta de la ISO) y el volumen
      con label "DFIR_OUTPUT" para la salida.
  2.  Pre-flight: chequea wmic, certutil, kape.exe, Targets/, espacio.
  3.  Stage: copia D:\kape.exe -> E:\_kape_stage\kape.exe. KAPE escribe
      Modules\bin y .kape debajo del directorio donde esta el .exe. Como
      E: es escribible, no falla con Access denied.
  4.  Calcula hostname + fecha + hora para nombres de archivo unicos.
  5.  Muestra resumen (Source, Target, ISO, Output, ZIP) en el panel.
  6.  Pide confirmacion con el boton "Ejecutar".
  7.  Ejecuta kape.exe desde E:\_kape_stage\ con --tsource C:
      --target KapeTriage --tdest {out_base} --targetdir D:\Targets
      --zip {out_zip} --hv nc,vm --sync $Null --gui.
  8.  Calcula SHA-256 del ZIP con certutil y lo guarda al lado.
  9.  Escribe notas en E:\NOTES\{HOSTNAME}-NOTES-{YYYYMMDD}.txt.

QUE GENERA AL TERMINAR
----------------------
  E:\{HOSTNAME}-KAPE-{YYYYMMDD}-{HHMMSS}.zip          (resultado principal)
  E:\{HOSTNAME}-KAPE-{YYYYMMDD}-{HHMMSS}.zip.sha256   (hash del ZIP)
  E:\NOTES\{HOSTNAME}-NOTES-{YYYYMMDD}.txt            (contexto de la corrida)
  E:\NOTES\{HOSTNAME}-LOG-{YYYYMMDD}.txt              (log completo)
  E:\_kape_stage\                                     (staging + Modules\bin)

Antes de subir la ISO al datastore:
  macOS / Linux :  shasum -a 256 KAPE-MEDIA-v5.2.18.iso
  Windows       :  certutil -hashfile KAPE-MEDIA-v5.2.18.iso SHA256
El hash esperado esta en SHA256SUMS.txt.

================================================================================
Para el procedimiento completo, abra MANUAL_OPERATIVO.txt en esta misma ISO.
No improvise: la VM debe estar sin red durante todo el procedimiento.
Si tiene dudas, DETENGASE y consulte al equipo de respuesta.
================================================================================
