@echo off
REM ==============================================================================
REM  KAPE_TRIAGE.cmd - Adquisicion DFIR unificada, VM01 o VM02
REM  Caso: ransomware Windows sobre VMware ESXi - sin red, offline.
REM ==============================================================================
REM  USO: doble clic sobre este archivo desde el Explorador de Windows.
REM       O bien desde cmd.exe:  KAPE_TRIAGE.cmd
REM
REM  FLUJO:
REM    1. Auto-detecta donde esta montado el CD (esta misma carpeta).
REM    2. Auto-detecta el VMDK de salida por etiqueta "DFIR_OUTPUT".
REM    3. Pregunta unicamente: 1 (VM01) o 2 (VM02).
REM    4. Pide elevacion a Administrador si no la tiene.
REM    5. Ejecuta KAPE target KapeTriage contra C:.
REM    6. Genera ZIP, SHA-256, NOTES automaticamente.
REM
REM  REQUISITOS:
REM    - El VMDK DFIR-OUTPUT.vmdk debe estar conectado y formateado como NTFS
REM      con etiqueta "DFIR_OUTPUT" (solo se inicializa UNA vez, en VM01).
REM    - KapeTriage debe existir en Targets/Compound/ (se valida antes).
REM
REM  SALIDAS:
REM    - <DFIR_OUTPUT>:\VM<0X>-KAPE-<YYYYMMDD>.zip
REM    - <DFIR_OUTPUT>:\VM<0X>-KAPE-<YYYYMMDD>.zip.sha256
REM    - <DFIR_OUTPUT>:\NOTES\VM<0X>-NOTES.txt
REM ==============================================================================

setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul 2>&1

color 0A
title KAPE Triage - Adquisicion DFIR

echo.
echo  ===============================================================
echo    KAPE Triage  -  Adquisicion DFIR automatica (offline)
echo    Caso: ransomware Windows sobre VMware ESXi
echo  ===============================================================
echo.

REM ------------------------------------------------------------------------------
REM  0. Auto-elevacion a Administrador si no la tenemos.
REM     KAPE necesita admin local para acceder a SYSTEM/SECURITY hives y a
REM     los .evtx protegidos. Sin elevacion, la adquisicion sera incompleta.
REM ------------------------------------------------------------------------------
net session >nul 2>&1
if errorlevel 1 (
    echo  [INFO] Sin privilegios de administrador. Solicitando elevacion...
    echo.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

REM ------------------------------------------------------------------------------
REM  0.5 Pre-flight compat check (opcional pero recomendado).
REM      Llama a CHECK_COMPAT.cmd en la misma carpeta. Si devuelve errores
REM      bloqueantes, abortamos. El operador puede saltarlo manualmente
REM      pasando el parametro /SKIPCOMPAT.
REM ------------------------------------------------------------------------------
set "SKIP_COMPAT=0"
if /i "%~1"=="/SKIPCOMPAT" set "SKIP_COMPAT=1"
if /i "%~1"=="-SKIPCOMPAT" set "SKIP_COMPAT=1"

if "!SKIP_COMPAT!"=="0" (
    if exist "%~dp0CHECK_COMPAT.cmd" (
        echo  [0/6] Pre-flight: verificando requisitos del sistema...
        echo.
        call "%~dp0CHECK_COMPAT.cmd"
        set "CC_RC=%ERRORLEVEL%"
        echo.
        if !CC_RC! GEQ 2 (
            echo  [ABORTADO] CHECK_COMPAT detecto errores bloqueantes.
            echo  Revise CHECK_COMPAT-REPORT.txt en el CD-ROM.
            echo  Si esta seguro de continuar, re-ejecute con /SKIPCOMPAT.
            echo.
            pause
            exit /b 2
        )
    ) else (
        echo  [WARN] CHECK_COMPAT.cmd no encontrado. Continuando sin pre-flight.
    )
) else (
    echo  [INFO] Pre-flight saltado por parametro /SKIPCOMPAT.
)

REM ------------------------------------------------------------------------------
REM  1. Auto-detectar la ruta del CD donde estamos.
REM     %~dp0 siempre devuelve la carpeta que contiene este .cmd, sin importar
REM     en que letra este montado el ISO.
REM ------------------------------------------------------------------------------
set "ISO_DIR=%~dp0"
if "%ISO_DIR:~-1%"=="\" set "ISO_DIR=%ISO_DIR:~0,-1%"
set "KAPE=%ISO_DIR%\kape.exe"
set "TARGET_DIR=%ISO_DIR%\Targets"
set "MODULE_DIR=%ISO_DIR%\Modules"

echo  [1/6] CD-ROM detectado en: %ISO_DIR%

if not exist "%KAPE%" (
    echo.
    echo  [ERROR] No se encuentra kape.exe en %ISO_DIR%
    echo  Verifique que la ISO KAPE-MEDIA esta montada como CD/DVD virtual.
    echo.
    pause
    exit /b 1
)

REM ------------------------------------------------------------------------------
REM  2. Auto-detectar la unidad de salida buscando el label "DFIR_OUTPUT".
REM ------------------------------------------------------------------------------
echo  [2/6] Buscando VMDK de salida (label "DFIR_OUTPUT") ...
set "OUT_LET="
for /f "tokens=2 delims==" %%a in ('
    wmic logicaldisk where "VolumeName='DFIR_OUTPUT'" get DeviceID /value 2^>nul
') do (
    if not "%%a"=="" set "OUT_LET=%%a"
)

if "%OUT_LET%"=="" (
    echo.
    echo  [ERROR] No se encuentra ninguna unidad con etiqueta "DFIR_OUTPUT".
    echo  Esto significa que el VMDK DFIR-OUTPUT.vmdk no esta conectado,
    echo  no esta formateado, o su etiqueta NTFS es diferente.
    echo.
    echo  Unidades visibles ahora:
    wmic logicaldisk get DeviceID,VolumeName,FileSystem,Size,FreeSpace
    echo.
    echo  Pasos esperados (solo la primera vez, en VM01):
    echo    1. Conectar DFIR-OUTPUT.vmdk como disco adicional en la VM.
    echo    2. diskmgmt.msc -^> Initialize Disk (GPT) -^> New Simple Volume
    echo       -^> NTFS -^> Volume label: DFIR_OUTPUT
    echo    3. Reintentar este script.
    echo.
    pause
    exit /b 1
)

REM  Quitar los dos puntos finales del DeviceID (wmic devuelve "E:",
REM  queremos "E" para componer rutas como E:\VM01-KAPE-... sin doble :).
if "%OUT_LET:~-1%"==":" set "OUT_LET=%OUT_LET:~0,-1%"

echo       ^>^> Unidad de salida: %OUT_LET%: (label "DFIR_OUTPUT")

if not exist "%OUT_LET%:\NOTES" mkdir "%OUT_LET%:\NOTES" 2>nul
if not exist "%OUT_LET%:\Modules" mkdir "%OUT_LET%:\Modules" 2>nul

REM ------------------------------------------------------------------------------
REM  3. Preguntar la VM (1 o 2).
REM ------------------------------------------------------------------------------
echo.
echo  [3/6] Identifique la VM que va a procesar:
echo         1 ^>^> VM01
echo         2 ^>^> VM02
echo.
set "VM_CHOICE="
:ASK_VM
set "VM_CHOICE="
set /p "VM_CHOICE=Numero de VM [1/2]: "
if /i "%VM_CHOICE%"=="1" set "VM_NUM=01" & goto VM_OK
if /i "%VM_CHOICE%"=="2" set "VM_NUM=02" & goto VM_OK
echo  [ERROR] Opcion invalida. Escriba 1 o 2.
goto ASK_VM
:VM_OK
echo       ^>^> VM%VM_NUM% seleccionada.

REM ------------------------------------------------------------------------------
REM  4. Auto-detectar fecha y hostname para nombres de archivo.
REM ------------------------------------------------------------------------------
for /f "tokens=2 delims==" %%a in ('wmic os get localdatetime /value') do set "DT=%%a"
set "FECHA=%DT:~0,8%"
set "HOSTNAME=%COMPUTERNAME%"
set "VM_NAME=VM%VM_NUM%-%HOSTNAME%"

set "OUT_BASE=%OUT_LET%:\VM%VM_NUM%-KAPE-%FECHA%"
set "OUT_ZIP=%OUT_LET%:\VM%VM_NUM%-KAPE-%FECHA%.zip"
set "NOTES=%OUT_LET%:\NOTES\VM%VM_NUM%-NOTES.txt"

echo  [4/6] Fecha=%FECHA%  Hostname=%HOSTNAME%
echo       ^>^> ZIP de salida: %OUT_ZIP%
echo       ^>^> NOTES:         %NOTES%
echo.

REM ------------------------------------------------------------------------------
REM  5. Validar target KapeTriage y mostrar resumen antes de ejecutar.
REM ------------------------------------------------------------------------------
echo  [5/6] Validando target "KapeTriage" ...
if not exist "%TARGET_DIR%\Compound\KapeTriage.tkape" (
    echo.
    echo  [ERROR] No se encuentra %TARGET_DIR%\Compound\KapeTriage.tkape
    echo  La ISO puede estar corrupta o el catalogo de Targets esta incompleto.
    pause
    exit /b 1
)

REM  Comprobacion adicional: kape.exe debe poder resolver el target.
"%KAPE%" --target KapeTriage --tlist 2>nul | findstr /I "KapeTriage" >nul
if errorlevel 1 (
    echo.
    echo  [WARN] KAPE no devolvio info del target. Continuando de todos modos...
)

echo.
echo  ===============================================================
echo    RESUMEN - revise antes de continuar
echo  ===============================================================
echo    Origen    : C:\
echo    ISO       : %ISO_DIR%
echo    Salida    : %OUT_LET%:\  (label DFIR_OUTPUT)
echo    VM        : VM%VM_NUM%  (%HOSTNAME%)
echo    Target    : KapeTriage
echo    ZIP       : VM%VM_NUM%-KAPE-%FECHA%.zip
echo    SHA-256   : se calculara al terminar
echo    NOTAS     : NOTES\VM%VM_NUM%-NOTES.txt
echo  ===============================================================
echo.
set "CONFIRM="
set /p "CONFIRM=Escribir SI para continuar (SI/no): "
if /i not "%CONFIRM%"=="SI" (
    echo  [ABORTADO] No se ejecuto KAPE.
    pause
    exit /b 2
)

REM ------------------------------------------------------------------------------
REM  6. Inicializar NOTES y arrancar KAPE.
REM ------------------------------------------------------------------------------
echo  [6/6] Iniciando KAPE ...
echo.

(
    echo ============================================================
    echo  KAPE Triage - Notas de adquisicion
    echo ============================================================
    echo  VM              : VM%VM_NUM%
    echo  Hostname        : %HOSTNAME%
    echo  Operador        : %USERNAME%
    echo  Fecha           : %FECHA%
    echo  ISO montada en : %ISO_DIR%
    echo  Salida en       : %OUT_LET%:\  (label DFIR_OUTPUT)
    echo  Target          : KapeTriage
    echo  Source          : C:\
    echo  Inicio          : %DATE% %TIME%
    echo  Comando:
    echo    "%KAPE%" --tsource C: --target KapeTriage
    echo        --tdest "%OUT_BASE%"
    echo        --targetdir "%TARGET_DIR%"
    echo        --zip "%OUT_ZIP%"
    echo        --mdest "%OUT_LET%:\Modules"
    echo        --moduledir "%MODULE_DIR%"
    echo        --hv nc,vm --sync $Null --gui
    echo ============================================================
    echo.
) > "%NOTES%"

"%KAPE%" ^
    --tsource C: ^
    --target KapeTriage ^
    --tdest "%OUT_BASE%" ^
    --targetdir "%TARGET_DIR%" ^
    --zip "%OUT_ZIP%" ^
    --mdest "%OUT_LET%:\Modules" ^
    --moduledir "%MODULE_DIR%" ^
    --hv nc,vm ^
    --sync $Null ^
    --gui
set RC=%ERRORLEVEL%

echo.
echo  ===============================================================
echo  KAPE termino con codigo de salida %RC%.
echo  ===============================================================
echo  Fin: %DATE% %TIME% >> "%NOTES%"
echo  KapeExitCode: %RC% >> "%NOTES%"

if exist "%OUT_ZIP%" (
    echo.
    echo  Calculando SHA-256 ...
    certutil -hashfile "%OUT_ZIP%" SHA256 > "%OUT_ZIP%.sha256.txt" 2>nul

    REM  certutil en Windows espanol devuelve:
    REM    Hash SHA256 de <archivo>:
    REM    <hash 64 hex chars>
    REM    CertUtil: -hashfile comando completado correctamente.
    REM  Filtramos SOLO la linea de 64 hex chars para obtener el hash.
    set "HASH="
    for /f "delims=" %%h in ('
        type "%OUT_ZIP%.sha256.txt" 2^>nul ^| findstr /R "^[a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9]"
    ') do (
        if not defined HASH set "HASH=%%h"
    )
    del "%OUT_ZIP%.sha256.txt" 2>nul

    if defined HASH (
        echo  !HASH!  VM%VM_NUM%-KAPE-%FECHA!.zip > "!OUT_ZIP!.sha256"
        echo.
        echo  SHA-256: !HASH!
        echo  Guardado en: !OUT_ZIP!.sha256
        echo  SHA-256: !HASH! >> "!NOTES!"
        for %%F in ("%OUT_ZIP%") do (
            echo  ZIPSize: %%~zF bytes >> "!NOTES!"
        )
    ) else (
        echo  [WARN] No se pudo calcular SHA-256 automaticamente.
        echo  Calculelo manualmente con:
        echo    certutil -hashfile "%OUT_ZIP%" SHA256
        echo  CalculoManual: REQUERIDO >> "%NOTES%"
    )

    echo.
    echo  Tamano del ZIP:
    dir "%OUT_ZIP%"
    echo.
    echo  Archivos generados en %OUT_LET%:\:
    echo    %OUT_ZIP%
    echo    %OUT_ZIP%.sha256
    echo    %NOTES%
) else (
    echo.
    echo  [ERROR] No se genero el ZIP. Revise %NOTES% y los logs de KAPE.
    echo  ZipGenerado: NO >> "%NOTES%"
)

echo.
echo  ===============================================================
echo   PROXIMOS PASOS
echo  ===============================================================
echo   1. Apague la VM desde la consola ESXi (Power ^> Shut down).
echo   2. Edit settings ^> disco de salida ^>
echo        "Remove from virtual machine" (NO "delete from datastore").
echo   3. Si va a procesar la otra VM (VM%VM_NUM% ya hecho):
if "%VM_NUM%"=="01" (
    echo        - Vuelva a conectar el mismo VMDK DFIR-OUTPUT.vmdk en VM02.
    echo        - Vuelva a montar la ISO como CD/DVD en VM02.
    echo        - Ejecute este mismo script y elija opcion 2.
) else (
    echo        - Si solo quedaba VM02, desconecte la ISO y el VMDK ya.
)
echo   4. Descargue del datastore:
echo        - DFIR-OUTPUT.vmdk       (descriptor, ~KB)
echo        - DFIR-OUTPUT-flat.vmdk  (datos, varios GB)
echo        - *.sha256                (hashes)
echo        - NOTES\*.txt             (notas)
echo.
echo  ===============================================================
echo   Adquisicion de VM%VM_NUM% completada.
echo  ===============================================================
echo.
pause
endlocal & exit /b %RC%
