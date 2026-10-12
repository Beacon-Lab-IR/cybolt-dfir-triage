@echo off
REM ============================================================
REM  RUN-KAPE.bat - Cybolt DFIR acquisition wrapper
REM  Companion to targets-full.txt (operator-caused incident IR)
REM
REM  LAYOUT POR DEFECTO (todo en la misma USB, letra D:):
REM    D:\KAPE\                              <- instalacion KAPE
REM    ├── kape.exe
REM    ├── RUN-KAPE.bat                      <- este archivo
REM    ├── targets-full.txt
REM    ├── Targets\
REM    ├── Compound\
REM    └── Modules\
REM    D:\%COMPUTERNAME%-YYYYMMDD-HHMMSS\    <- bundle de salida (sibling)
REM
REM  REQUISITOS:
REM    - kape.exe, targets-full.txt, Targets\, Compound\, Modules\
REM      deben estar en la MISMA carpeta que este .bat
REM    - Ejecutar como Administrador (clic derecho -> Ejecutar como administrador)
REM    - USB de evidencia en D:\ (NTFS o exFAT, >= 25 GB libres)
REM    - NO stagear a C:\. Todo corre desde la USB.
REM
REM  USO:
REM    1. Conectar la USB de evidencia
REM    2. Verificar que la carpeta X:\KAPE\ este completa (ver layout arriba)
REM    3. Abrir CMD como Administrador
REM    4. Ejecutar:
REM         X:\KAPE\RUN-KAPE.bat              <- si la USB esta en D:
REM         X:\KAPE\RUN-KAPE.bat E            <- si esta en E: (o F:, G:, ...)
REM
REM  El primer argumento es la letra de la USB de salida. Default: D
REM
REM  AUTOR: Cybolt DFIR (Beacon-Lab-IR)
REM  Repo:  github.com/Beacon-Lab-IR/cybolt-dfir-triage
REM ============================================================
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul 2>&1

REM --- Auto-detectar directorio del script ---
set "KAPE_DIR=%~dp0"
if "%KAPE_DIR:~-1%"=="\" set "KAPE_DIR=%KAPE_DIR:~0,-1%"

set "KAPE_EXE=%KAPE_DIR%\kape.exe"
set "TARGETS_LIST=%KAPE_DIR%\targets-full.txt"
set "TARGET_DIR=%KAPE_DIR%\Targets"
set "MODULES_DIR=%KAPE_DIR%\Modules"

REM --- Parametros de adquisicion ---
set "TSOURCE=C:\"
set "OUT_LET=D"
if not "%~1"=="" (
    set "OUT_LET=%~1"
    REM Strip trailing : y slash si el analista paso "E:" o "E:\"
    if "%OUT_LET:~-1%"==":" set "OUT_LET=%OUT_LET:~0,-1%"
    if "%OUT_LET:~-1%"=="\" set "OUT_LET=%OUT_LET:~0,-1%"
)

REM --- Fijar cwd al directorio del script (paths relativas funcionan) ---
cd /d "%KAPE_DIR%"

REM --- Generar timestamp YYYYMMDD-HHMMSS ---
for /f "tokens=2 delims==" %%a in ('wmic os get localdatetime /value') do set "DT=%%a"
set "STAMP=%DT:~0,8%-%DT:~8,6%"
set "OUT_DIR=%OUT_LET%:\%COMPUTERNAME%-%STAMP%\"

color 0A
cls
echo.
echo  ==============================================================
echo    KAPE Triage  -  Cybolt DFIR (operator-caused IR)
echo  ==============================================================
echo.
echo    Directorio KAPE : %KAPE_DIR%
echo    kape.exe        : %KAPE_EXE%
echo    targets list    : %TARGETS_LIST%
echo    Source          : %TSOURCE%
echo    Destino         : %OUT_DIR%
echo.

REM --- Verificar que el medio sea escribible (no CD-ROM) ---
echo  [*] Verificando que el medio sea escribible...
set "TEST_FILE=%KAPE_DIR%\.write_test_%RANDOM%"
copy /Y NUL "%TEST_FILE%" >nul 2>&1
if errorlevel 1 (
    echo.
    echo  [ERROR] El directorio %KAPE_DIR% es de solo lectura (CD-ROM?).
    echo  KAPE necesita escribir archivos de estado junto a kape.exe.
    echo.
    echo  SOLUCION: copiar toda la carpeta KAPE\ a una USB writable:
    echo    xcopy /E /I /Y %KAPE_DIR% D:\KAPE\
    echo  Luego correr desde D:\KAPE\RUN-KAPE.bat
    echo.
    pause
    exit /b 1
)
del "%TEST_FILE%" 2>nul

REM --- Validar archivos requeridos ---
echo  [*] Validando archivos requeridos...
set "MISSING=0"
if not exist "%KAPE_EXE%" (
    echo    [FALTA] %KAPE_EXE%
    set "MISSING=1"
)
if not exist "%TARGETS_LIST%" (
    echo    [FALTA] %TARGETS_LIST%
    set "MISSING=1"
)
if not exist "%TARGET_DIR%" (
    echo    [FALTA] %TARGET_DIR%
    set "MISSING=1"
)
if "%MISSING%"=="1" (
    echo.
    echo  [ERROR] Faltan archivos. Verifique la estructura:
    echo    %KAPE_DIR%\kape.exe
    echo    %KAPE_DIR%\targets-full.txt
    echo    %KAPE_DIR%\Targets\*.tkape
    echo    %KAPE_DIR%\Compound\*.tkape
    echo    %KAPE_DIR%\Modules\*.mkape
    echo.
    pause
    exit /b 1
)

REM --- Validar unidad de salida ---
if not exist "%OUT_LET%:\" (
    echo.
    echo  [ERROR] Unidad %OUT_LET%: no existe. Conecte la USB de evidencia.
    pause
    exit /b 1
)

REM --- Crear directorio de salida ---
mkdir "%OUT_DIR%" 2>nul

REM --- Ejecutar KAPE ---
echo.
echo  [GO] Iniciando KAPE contra %TSOURCE% ...
echo  [GO] Salida: %OUT_DIR%
echo  [GO] Esto puede tomar 30-90 minutos. No desconecte la USB.
echo.
pause

REM --- Ejecutar KAPE con paths relativas donde corresponde ---
REM     (kape.exe y targets-full.txt viajan con el .bat;
REM      --targetdir, --modulesdir, --tdest, --zip siguen absolutas
REM      porque apuntan a lugares fijos del stage;
REM      KapeTriage y !SANS_Triage vienen en targets-full.txt — el !
REM      de !SANS_Triage colisiona con delayed expansion de cmd si va
REM      aca como --target, asi que lo dejamos en el archivo)
kape.exe ^
  --tsource "%TSOURCE%" ^
  --targets-list targets-full.txt ^
  --tdest "%OUT_DIR%" ^
  --targetdir "%TARGET_DIR%" ^
  --modulesdir "%MODULES_DIR%" ^
  --zip "%OUT_DIR%%COMPUTERNAME%-KAPE-%STAMP%" ^
  --hv nc,vm ^
  --vss

set "RC=%ERRORLEVEL%"
echo.
echo  ==============================================================
if "%RC%"=="0" (
    echo    KAPE finalizo OK
    echo    Bundle en: %OUT_DIR%
    echo.
    echo  Siguiente paso - calcular SHA-256 del ZIP:
    echo    certutil -hashfile "%OUT_DIR%%COMPUTERNAME%-KAPE-%STAMP%.zip" SHA256
) else (
    echo    KAPE termino con codigo %RC%
    echo    Revisar logs en: %MODULES_DIR%\bin\
)
echo  ==============================================================
pause
endlocal