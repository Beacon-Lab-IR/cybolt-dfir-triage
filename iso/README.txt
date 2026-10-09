================================================================================
  CYBOLT-DFIR-TRIAGE - Adquisicion DFIR con doble clic
  Caso: ransomware sobre hypervisor (PVE / VMware ESXi) - VMs Windows
  afectadas, sin red durante la adquisicion.
  v5.3.0 (wizard unificado KapeTriage + RAM Capture + upload separado).
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
  1.  Sube CYBOLT-DFIR-TRIAGE-v5.3.0.iso al datastore del hipervisor.
  2.  En la VM afectada: monta la ISO como CD/DVD virtual.
  3.  Conecta DFIR-OUTPUT.vmdk como segundo disco. Si es la primera vez
      o el disco es nuevo: inicialo como GPT, particion NTFS, label
      "DFIR_OUTPUT". Si ya esta inicializado de una corrida anterior,
      conectalo y listo.
  4.  Enciende la VM. Espera a que cargue Windows.
  5.  Abre el Explorador de archivos, ve al CD/DVD ("CYBOLT-DFIR-TRIAGE").
  6.  OPCION A (visual, recomendada - wizard unificado):
        Doble clic en CYBOLT_DFIR_TRIAGE.exe
        - Windows muestra UAC. Acepta con "Si" (corre como Administrador).
        - El wizard te guia por 5 pasos:
            1. Bienvenida (opciones: capturar RAM, cmd wrapper)
            2. Deteccion del entorno (CD-ROM, output drive, RAM)
            3. KapeTriage (run KAPE, genera ZIP)
            4. RAM Capture (run winpmem, genera .raw) - opcional
            5. Resumen final con SHA-256 de cada output
        - Al terminar, los outputs quedan en E:\DFIR-OUTPUT\.

      OPCION B (consola, fallback para SSH / RDP limitado):
        Doble clic en CHECK_COMPAT.cmd para verificar requisitos.
        Si todo OK, doble clic en KAPE_TRIAGE.cmd.
        Aceptar UAC. Aceptar el resumen. Esperar.

  7.  Apaga la VM y desconecta DFIR-OUTPUT.vmdk (Remove from VM, NO delete).
  8.  Para entregar la evidencia al operador:
      - Opcion 1: descarga del datastore el VMDK + ZIPs (como en versiones previas).
      - Opcion 2: en una VM con internet, attach el VMDK y corre
        DFIR_UPLOADER_UI.exe para subir via S3/FTP/SFTP/SMB segun el
        config que te paso el operador.

CONTENIDO DE LA ISO
-------------------
  CYBOLT_DFIR_TRIAGE.exe   Wizard unificado WinForms (.NET 4.5.2) - v5.3.0
                           (KapeTriage + RAM Capture en 5 pasos)
  DFIR_UPLOADER_UI.exe      GUI separada para subida a S3/FTP/SFTP/SMB - v5.3.0
  KAPE_TRIAGE.cmd          Script batch equivalente (mismo flujo, headless)
  CHECK_COMPAT.cmd         Verificador de requisitos. Genera
                           CHECK_COMPAT-REPORT.txt.
  autorun.inf              Documenta el arranque automatico.
  kape.exe                 Kroll KAPE v1.3.0.2.
  Targets\                 Catalogo completo de KapeFiles Targets.
  Documentation\           EULA y notas del paquete original.
  MANUAL_OPERATIVO.txt     Manual paso a paso en espanol.
  SHA256SUMS.txt           Hashes SHA-256 de los archivos criticos.
  Tools\kape.zip           Distribucion KAPE completa original (553 MB).
  Tools\Memory\winpmem_mini_x64_rc2.exe   Binario de captura de RAM
                           (Velocidex WinPmem v4.0.rc1, 527 KB, x64).
  Tools\Memory\CAPTURE_RAM.cmd           Wrapper batch standalone de
                           captura de RAM (fallback si la GUI no aplica).
  LICENSE                  MIT License - Copyright (c) 2026 Cybolt.

QUE GENERA AL TERMINAR
----------------------
  E:\{HOSTNAME}-KAPE-{YYYYMMDD}-{HHMMSS}.zip          (KapeTriage output)
  E:\{HOSTNAME}-KAPE-{YYYYMMDD}-{HHMMSS}.zip.sha256   (hash del ZIP)
  E:\RAM-{HOSTNAME}-{YYYYMMDD}-{HHMMSS}.raw           (RAM capture, si se ejecuto)
  E:\RAM-{HOSTNAME}-{YYYYMMDD}-{HHMMSS}.raw.sha256    (hash del .raw)
  E:\_kape_stage\                                      (staging + Modules\bin)

Antes de subir la ISO al datastore:
  macOS / Linux :  shasum -a 256 CYBOLT-DFIR-TRIAGE-v5.3.0.iso
  Windows       :  certutil -hashfile CYBOLT-DFIR-TRIAGE-v5.3.0.iso SHA256
El hash esperado esta en SHA256SUMS.txt.

================================================================================
Para el procedimiento completo, abra MANUAL_OPERATIVO.txt en esta misma ISO.
No improvise: la VM debe estar sin red durante todo el procedimiento.
Si tiene dudas, DETENGASE y consulte al equipo de respuesta.
================================================================================