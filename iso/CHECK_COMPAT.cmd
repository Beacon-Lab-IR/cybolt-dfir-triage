@echo off
REM ==============================================================================
REM  CHECK_COMPAT.cmd - Verifica requisitos antes de ejecutar KAPE_TRIAGE
REM  Caso: adquisicion DFIR offline contra VMs Windows (ESXi, sin red).
REM ==============================================================================
REM  Comprueba:
REM    1. Privilegios de administrador
REM    2. Sistema operativo y version
REM    3. .NET Framework 4.5.2 o superior (requerido por kape.exe)
REM    4. PowerShell (2.0+; recomendado 3.0+)
REM    5. wmic (deprecated en Win11 24H2 pero aun funcional)
REM    6. certutil (SHA-256)
REM    7. CD-ROM virtual con kape.exe accesible
REM    8. VMDK de salida con label "DFIR_OUTPUT" accesible
REM    9. Espacio libre en VMDK de salida (al menos 5 GB recomendado)
REM
REM  Salida: pantalla + archivo CHECK_COMPAT-REPORT.txt en el CD.
REM  Codigos de salida:  0 OK   1 advertencia   2 error bloqueante
REM ==============================================================================

setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul 2>&1

color 0B
title CHECK_COMPAT - Verificacion de requisitos KAPE

set "REPORT=%TEMP%\CHECK_COMPAT-REPORT.txt"
set "ERR_COUNT=0"
set "WARN_COUNT=0"

echo. > "%REPORT%"

call :print_header
echo  Iniciando verificacion...
echo.
echo  Iniciando verificacion... >> "%REPORT%"
echo. >> "%REPORT%"

REM ------------------------------------------------------------------------------
REM  1. Privilegios de administrador
REM ------------------------------------------------------------------------------
call :section "1. Privilegios de administrador"
net session >nul 2>&1
if errorlevel 1 (
    call :fail "No se ejecuta como Administrador. Re-lanzar como admin."
    call :suggest "Click derecho -^> 'Ejecutar como administrador' sobre KAPE_TRIAGE.cmd"
    set /a ERR_COUNT+=1
) else (
    call :ok "Sesion con privilegios de administrador."
)
echo.

REM ------------------------------------------------------------------------------
REM  2. Sistema operativo
REM ------------------------------------------------------------------------------
call :section "2. Sistema operativo"
set "WIN_VER="
set "WIN_BUILD="
for /f "tokens=2 delims==" %%a in ('wmic os get caption /value 2^>nul') do set "WIN_VER=%%a"
for /f "tokens=2 delims==" %%a in ('wmic os get version /value 2^>nul') do set "WIN_BUILD=%%a"
if not defined WIN_VER set "WIN_VER=(no detectado)"
echo  Detectado: %WIN_VER%
echo  Build    : %WIN_BUILD%
echo  Detectado: %WIN_VER% >> "%REPORT%"
echo  Build    : %WIN_BUILD% >> "%REPORT%"

REM Clasificacion
set "WIN_OK=0"
set "WIN_NOTE="
if /i not "%WIN_VER%"=="(no detectado)" (
    REM Server 2016+, Windows 10/11
    echo "%WIN_VER%" | findstr /I "Windows 11" >nul && set "WIN_OK=1"
    echo "%WIN_VER%" | findstr /I "Windows 10" >nul && set "WIN_OK=1"
    echo "%WIN_VER%" | findstr /I "Server 201" >nul && set "WIN_OK=1"
    echo "%WIN_VER%" | findstr /I "Server 202" >nul && set "WIN_OK=1"
    REM Win8.x y Win 8.1
    echo "%WIN_VER%" | findstr /I "Windows 8" >nul && set "WIN_OK=1"
    set "WIN_NOTE=Win 8.x OK con .NET 4.5.2 instalado"
    REM Win7
    echo "%WIN_VER%" | findstr /I "Windows 7" >nul && (
        set "WIN_OK=1"
        set "WIN_NOTE=Win 7 SP1 OK si .NET 4.5.2 esta instalado (comun via Windows Update)"
    )
)

if "%WIN_OK%"=="1" (
    call :ok "Windows soportado por KAPE 1.3.0.2."
    if defined WIN_NOTE call :info "%WIN_NOTE%"
) else (
    call :warn "Windows no reconocido explicitamente. Intentar igualmente."
    call :info "KAPE 1.3.0.2 requiere .NET 4.5.2 (soportado en Win7 SP1+, Vista SP2+, Server 2008 SP2+)."
    set /a WARN_COUNT+=1
)
echo.

REM ------------------------------------------------------------------------------
REM  3. .NET Framework 4.5.2+
REM ------------------------------------------------------------------------------
call :section "3. .NET Framework"
set "NET_HIGHEST=0"
set "NET_VER="
if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\clr.dll" (
    REM .NET 4.x esta instalado. Verificar la version.
    for /f "tokens=*" %%v in ('powershell -NoProfile -Command "Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue | Get-ItemProperty -Name Version -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Version" 2^>nul') do set "NET_VER=%%v"
)
if not defined NET_VER set "NET_VER=(no detectable)"

echo  Version detectada: %NET_VER%
echo  Version detectada: %NET_VER% >> "%REPORT%"

if "%NET_VER%"=="(no detectable)" (
    call :fail "No se pudo detectar version de .NET Framework."
    call :suggest "Comprobar manualmente: reg query 'HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' /v Version"
    set /a ERR_COUNT+=1
) else (
    REM Comparar version con 4.5.2
    for /f "tokens=1,2,3 delims=." %%a in ("%NET_VER%") do (
        set "NET_MAJOR=%%a"
        set "NET_MINOR=%%b"
        set "NET_BUILD=%%c"
    )
    if %NET_MAJOR% GTR 4 (
        call :ok ".NET %NET_VER% (mayor que 4.5.2)."
    ) else (
        if %NET_MAJOR% LSS 4 (
            call :fail ".NET %NET_VER% es ANTERIOR a 4.5.2. KAPE no funcionara."
            call :suggest "Instalar .NET 4.5.2 desde Windows Update o desde https://dotnet.microsoft.com/download/dotnet-framework/net452"
            set /a ERR_COUNT+=1
        ) else (
            REM Mayor == 4
            if %NET_MINOR% GEQ 6 (
                call :ok ".NET %NET_VER% (>= 4.6, suficiente)."
            ) else (
                if %NET_MINOR% GEQ 5 (
                    call :ok ".NET %NET_VER% (>= 4.5, probablemente suficiente)."
                ) else (
                    call :fail ".NET %NET_VER% es anterior a 4.5.2."
                    set /a ERR_COUNT+=1
                )
            )
        )
    )
)
echo.

REM ------------------------------------------------------------------------------
REM  4. PowerShell
REM ------------------------------------------------------------------------------
call :section "4. PowerShell"
set "PS_VER="
for /f "tokens=*" %%v in ('powershell -NoProfile -Command "$PSVersionTable.PSVersion.ToString()" 2^>nul') do set "PS_VER=%%v"
if not defined PS_VER set "PS_VER=(no detectado)"

echo  Version detectada: %PS_VER%
echo  Version detectada: %PS_VER% >> "%REPORT%"

if "%PS_VER%"=="(no detectado)" (
    call :fail "PowerShell no disponible. KAPE_TRIAGE.cmd no podra auto-elevarse."
    set /a ERR_COUNT+=1
) else (
    for /f "tokens=1,2 delims=." %%a in ("%PS_VER%") do (
        set "PS_MAJOR=%%a"
        set "PS_MINOR=%%b"
    )
    if %PS_MAJOR% GEQ 3 (
        call :ok "PowerShell %PS_VER% (>= 3.0)."
    ) else (
        if %PS_MAJOR% EQU 2 (
            call :warn "PowerShell 2.0 detectado. Funcional pero antiguo."
            call :info "Win7 SP1 trae PS 2.0 por defecto. Compatible con el script."
            set /a WARN_COUNT+=1
        ) else (
            call :fail "PowerShell %PS_VER% es demasiado antiguo."
            set /a ERR_COUNT+=1
        )
    )
)
echo.

REM ------------------------------------------------------------------------------
REM  5. wmic (deprecated en Win11 24H2 pero funcional)
REM ------------------------------------------------------------------------------
call :section "5. wmic"
where wmic >nul 2>&1
if errorlevel 1 (
    call :fail "wmic no disponible."
    set /a ERR_COUNT+=1
) else (
    call :ok "wmic disponible."
    call :info "wmic esta deprecated en Win11 24H2; sigue funcional en builds actuales."
)
echo.

REM ------------------------------------------------------------------------------
REM  6. certutil
REM ------------------------------------------------------------------------------
call :section "6. certutil"
where certutil >nul 2>&1
if errorlevel 1 (
    call :fail "certutil no disponible."
    set /a ERR_COUNT+=1
) else (
    call :ok "certutil disponible (soporte SHA-256)."
)
echo.

REM ------------------------------------------------------------------------------
REM  7. CD-ROM con kape.exe accesible
REM ------------------------------------------------------------------------------
call :section "7. CD-ROM con kape.exe"
set "ISO_DIR=%~dp0"
if "%ISO_DIR:~-1%"=="\" set "ISO_DIR=%ISO_DIR:~0,-1%"
if exist "%ISO_DIR%\kape.exe" (
    call :ok "kape.exe accesible en %ISO_DIR%"
    for %%F in ("%ISO_DIR%\kape.exe") do call :info "Tamano: %%~zF bytes"
) else (
    call :fail "kape.exe NO esta en %ISO_DIR%"
    call :suggest "Verificar que la ISO KAPE-MEDIA esta montada como CD/DVD virtual."
    set /a ERR_COUNT+=1
)
echo.

REM ------------------------------------------------------------------------------
REM  8. VMDK de salida con label DFIR_OUTPUT
REM ------------------------------------------------------------------------------
call :section "8. VMDK de salida (label DFIR_OUTPUT)"
set "OUT_LET="
for /f "tokens=2 delims==" %%a in ('
    wmic logicaldisk where "VolumeName='DFIR_OUTPUT'" get DeviceID /value 2^>nul
') do (
    if not "%%a"=="" set "OUT_LET=%%a"
)
if "%OUT_LET%"=="" (
    call :fail "No se encuentra unidad con label 'DFIR_OUTPUT'."
    call :suggest "Conectar DFIR-OUTPUT.vmdk y formatearlo como NTFS con label DFIR_OUTPUT."
    set /a ERR_COUNT+=1
) else (
    if "%OUT_LET:~-1%"==":" set "OUT_LET=%OUT_LET:~0,-1%"
    call :ok "Unidad detectada: %OUT_LET%: (label DFIR_OUTPUT)"
)
echo.

REM ------------------------------------------------------------------------------
REM  9. Espacio libre en VMDK de salida
REM ------------------------------------------------------------------------------
call :section "9. Espacio libre en VMDK de salida"
if defined OUT_LET (
    set "FREE_GB=0"
    for /f "tokens=2 delims==" %%a in ('
        wmic logicaldisk where "DeviceID='%OUT_LET%:'" get FreeSpace /value 2^>nul
    ') do set "FREE_RAW=%%a"
    if defined FREE_RAW (
        set /a FREE_GB=FREE_RAW/1073741824
        echo  Espacio libre: !FREE_GB! GB
        echo  Espacio libre: !FREE_GB! GB >> "%REPORT%"
        if !FREE_GB! GEQ 5 (
            call :ok "Espacio suficiente (>= 5 GB)."
        ) else (
            call :warn "Poco espacio libre (!FREE_GB! GB). Recomendado al menos 5 GB."
            set /a WARN_COUNT+=1
        )
    ) else (
        call :warn "No se pudo medir espacio libre."
        set /a WARN_COUNT+=1
    )
) else (
    call :skip "Saltado porque no hay VMDK conectado."
)
echo.

REM ------------------------------------------------------------------------------
REM  Resumen final
REM ------------------------------------------------------------------------------
call :section "RESUMEN"
echo.
echo  Errores bloqueantes:  %ERR_COUNT%
echo  Advertencias:         %WARN_COUNT%
echo.
echo  Errores bloqueantes:  %ERR_COUNT% >> "%REPORT%"
echo  Advertencias:         %WARN_COUNT% >> "%REPORT%"
echo. >> "%REPORT%"

if "%ERR_COUNT%"=="0" (
    if "%WARN_COUNT%"=="0" (
        echo  ============================================================
        echo    ESTADO: TODO OK. Ya puede ejecutar KAPE_TRIAGE.cmd.
        echo  ============================================================
        echo  ============================================================ >> "%REPORT%"
        echo    ESTADO: TODO OK. >> "%REPORT%"
        echo  ============================================================ >> "%REPORT%"
    ) else (
        echo  ============================================================
        echo    ESTADO: OK CON ADVERTENCIAS. Revise los puntos marcados.
        echo    KAPE_TRIAGE.cmd probablemente funcionara.
        echo  ============================================================
        echo  ============================================================ >> "%REPORT%"
        echo    ESTADO: OK CON ADVERTENCIAS. >> "%REPORT%"
        echo  ============================================================ >> "%REPORT%"
    )
    exit /b 0
) else (
    echo  ============================================================
    echo    ESTADO: HAY %ERR_COUNT% ERROR^%(S^) BLOQUEANTE^%(S^).
    echo    NO ejecute KAPE_TRIAGE.cmd hasta corregirlos.
    echo  ============================================================
    echo  ============================================================ >> "%REPORT%"
    echo    ESTADO: HAY %ERR_COUNT% ERROR^%(S^) BLOQUEANTE^%(S^). >> "%REPORT%"
    echo  ============================================================ >> "%REPORT%"
    exit /b 2
)

REM ==============================================================================
REM  Funciones auxiliares
REM ==============================================================================
:section
echo.
echo  ----------------------------------------------------------------
echo  [%~1]
echo  ----------------------------------------------------------------
echo. >> "%REPORT%"
echo  [%~1] >> "%REPORT%"
goto :eof

:ok
echo   [OK] %~1
echo   [OK] %~1 >> "%REPORT%"
goto :eof

:warn
echo   [WARN] %~1
echo   [WARN] %~1 >> "%REPORT%"
goto :eof

:fail
echo   [FAIL] %~1
echo   [FAIL] %~1 >> "%REPORT%"
goto :eof

:info
echo         %~1
echo         %~1 >> "%REPORT%"
goto :eof

:suggest
echo         Sugerencia: %~1
echo         Sugerencia: %~1 >> "%REPORT%"
goto :eof

:skip
echo   [SKIP] %~1
echo   [SKIP] %~1 >> "%REPORT%"
goto :eof

:print_header
echo  ===============================================================
echo    CHECK_COMPAT - Verificacion de requisitos para KAPE
echo    Caso: adquisicion DFIR offline contra VMs Windows
echo  ===============================================================
echo  Reporte completo en: %REPORT%
echo  (siempre escribible, no en el CD-ROM)
echo.
