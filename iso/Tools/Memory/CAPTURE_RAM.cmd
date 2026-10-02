@echo off
REM ============================================================================
REM  CAPTURE_RAM.cmd - Captura volatil de RAM para triage DFIR
REM                    By Cybolt, MIT License
REM
REM  Wrapper standalone de winpmem_mini_x64_rc2.exe (Velocidex). Captura la
REM  RAM fisica completa del host y computa SHA-256 de la imagen resultante
REM  para cadena de custodia.
REM
REM  Uso:
REM    CAPTURE_RAM.cmd                  -> captura en E:\RAM-<HOSTNAME>-<DATE>.raw
REM    CAPTURE_RAM.cmd D:              -> captura en D:\RAM-<HOSTNAME>-<DATE>.raw
REM    CAPTURE_RAM.cmd E: Z:\evidencia -> captura en Z:\evidencia
REM
REM  Requisitos:
REM    - Ejecutar como Administrator (UAC).
REM    - Espacio libre en disco >= 1.5x RAM fisica (peor caso comprimido).
REM      Tipico: ~30-50% de RAM fisica con formato .raw.
REM    - Funciona en Windows 7 SP1+ / Server 2008 R2+ / Win10 / Win11
REM      (x64). No funciona en x86 (solo hay binario x64).
REM    - No requiere red. No requiere instalacion.
REM
REM  Tiempo esperado: 5-15 min para capturar + 1-5 min para SHA-256,
REM  dependiendo del tamano de RAM y velocidad del disco de salida.
REM ============================================================================

setlocal EnableDelayedExpansion

REM --- Verificar admin ---
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Este script requiere privilegios de Administrator.
    echo         Click derecho "Ejecutar como administrador".
    exit /b 1
)

REM --- Determinar directorio de salida ---
set "OUTDIR=%~1"
if "%OUTDIR%"=="" set "OUTDIR=E:\"

REM --- Asegurar que el directorio existe ---
if not exist "%OUTDIR%" (
    echo [ERROR] Directorio de salida no existe: %OUTDIR%
    exit /b 1
)

REM --- Localizar winpmem (mismo directorio que este script) ---
set "SCRIPTDIR=%~dp0"
set "WINPMEM=%SCRIPTDIR%winpmem_mini_x64_rc2.exe"
if not exist "%WINPMEM%" (
    echo [ERROR] No encuentro winpmem_mini_x64_rc2.exe en:
    echo         %WINPMEM%
    echo         Verifica que el ISO v5.2.13+ se haya montado correctamente.
    exit /b 1
)

REM --- Construir nombre del archivo de salida ---
set "HOSTNAME=%COMPUTERNAME%"
for /f "tokens=2 delims==" %%I in ('wmic os get localdatetime /value') do set "DT=%%I"
set "DATESTAMP=%DT:~0,8%-%DT:~8,6%"
set "OUTFILE=%OUTDIR%RAM-%HOSTNAME%-%DATESTAMP%.raw"

echo ============================================================================
echo  CAPTURE_RAM.cmd - Captura volatil de RAM para triage DFIR
echo  By Cybolt, MIT License
echo ============================================================================
echo.
echo  Host       : %HOSTNAME%
echo  Date       : %DT:~0,4%-%DT:~4,2%-%DT:~6,2% %DT:~8,2%:%DT:~10,2%:%DT:~12,2%
echo  Output     : %OUTFILE%
echo  winpmem    : %WINPMEM%
echo  Total RAM  : 
systeminfo | findstr /C:"Total Physical Memory"
echo.

REM --- Aviso ---
echo [AVISO] La captura de RAM puede tardar varios minutos.
echo         NO cierres esta ventana. NO apagues la VM.
echo         La captura se hace a la velocidad del disco de destino.
echo.
pause

REM --- Capturar RAM ---
echo.
echo [%date% %time%] Iniciando captura de RAM con winpmem...
echo.
"%WINPMEM%" "%OUTFILE%"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] winpmem fallo con codigo %ERRORLEVEL%.
    echo         Causas comunes:
    echo           - Disco lleno (necesita ~1.5x RAM fisica libre).
    echo           - Antivirus/EDR bloqueando carga de driver.
    echo           - Secure Boot activo y driver no firmado (no aplica a
    echo             esta version firmada por Microsoft).
    exit /b 1
)

echo.
echo [%date% %time%] Captura completa: %OUTFILE%
for %%I in ("%OUTFILE%") do (
    set "SIZE=%%~zI"
    set /a "SIZE_MB=%%~zI / 1048576"
    echo  Tamano     : !SIZE! bytes ^(!SIZE_MB! MB^)
)

REM --- SHA-256 ---
echo.
echo [%date% %time%] Calculando SHA-256 (puede tardar 1-5 min)...
certutil -hashfile "%OUTFILE%" SHA256 > "%OUTFILE%.sha256.tmp" 2>&1
findstr /R "^[0-9a-fA-F][0-9a-fA-F]* [0-9a-fA-F]*$" "%OUTFILE%.sha256.tmp" > "%OUTFILE%.sha256"
del "%OUTFILE%.sha256.tmp"
echo [%date% %time%] SHA-256 guardado en: %OUTFILE%.sha256
type "%OUTFILE%.sha256"

echo.
echo ============================================================================
echo  Captura de RAM completa. Cadena de custodia:
echo    Archivo : %OUTFILE%
echo    Tamano  : !SIZE! bytes
echo    SHA-256 : ver %OUTFILE%.sha256
echo ============================================================================

endlocal
