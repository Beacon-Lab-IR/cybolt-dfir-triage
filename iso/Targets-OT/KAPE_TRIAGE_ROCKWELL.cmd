@echo off
REM ==============================================================================
REM  KAPE_TRIAGE_ROCKWELL.cmd
REM
REM  Triage KAPE 2-PASS para hosts Windows con Rockwell FactoryTalk
REM  (View SE/ME + servicios FTSP). DFIR.
REM
REM  Setup: el bundle (kape.exe + 4 .tkape + Targets\ + Modules\) en
REM          una carpeta llamada KAPE\ en la USB:
REM            E:\KAPE\kape.exe
REM            E:\KAPE\Targets\...
REM            E:\KAPE\Modules\...
REM            E:\KAPE\RockwellFactoryTalk.tkape, etc.
REM
REM  Uso: doble click sobre este .cmd (asume que vive en E:\KAPE).
REM        O desde cmd / Ejecutar:
REM          E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd              (usa su propia carpeta)
REM          E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E            (letra de USB: -> E:\KAPE)
REM          E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E:           (lo mismo, con :)
REM          E:\KAPE\KAPE_TRIAGE_ROCKWELL.cmd E:\Otra      (path custom)
REM
REM  Output: <HOST>-KAPE-YYYYMMDD.zip + <HOST>-FTV-YYYYMMDD.zip
REM          en la misma carpeta del bundle. + sus .sha256 al lado.
REM ==============================================================================

setlocal EnableExtensions EnableDelayedExpansion

REM ==============================================================================
REM  Carpeta base: por defecto la del .cmd. Si pasan letra de USB, se usa.
REM ==============================================================================
set "BASE=%~1"
if "%BASE%"=="" set "BASE=%~dp0"
if "%BASE:~1,1%"=="" set "BASE=%BASE%:\KAPE"
if "%BASE:~2%"=="" if "%BASE:~1%"==":" set "BASE=%BASE%\KAPE"

cd /d "%BASE%"

REM ==============================================================================
REM  Sanity check
REM ==============================================================================
echo.
echo  KAPE Triage 2-PASS - Rockwell FactoryTalk
echo  Base: %cd%
echo.

if not exist "kape.exe" (
    echo  ERROR: kape.exe no encontrado en %cd%
    echo  Coloque kape.exe en esta misma carpeta y vuelva a correr.
    echo.
    pause
    exit /b 1
)

set "MISSING="
for %%F in (RockwellFactoryTalk.tkape FactoryTalkDiagnostics.tkape FactoryTalkViewSE_ME.tkape FactoryTalkServices.tkape) do (
    if not exist "%%F" set "MISSING=!MISSING! %%F"
)
if defined MISSING (
    echo  ERROR: Faltan archivos en %cd%:
    echo    !MISSING!
    echo.
    pause
    exit /b 1
)

REM ==============================================================================
REM  Paths
REM ==============================================================================
for /f "tokens=2 delims==" %%a in ('wmic os get localdatetime /value') do set "DT=%%a"
set "DATE=%DT:~0,8%"
set "HOST=%COMPUTERNAME%"
set "TMP=C:\_kape_tmp"

echo  Host : %HOST%
echo  Date : %DATE%
echo.

REM ==============================================================================
REM  PASS 1 - KapeTriage (oficial, con modules EZ Tools)
REM  Solo si Targets\Compound\KapeTriage.tkape esta presente.
REM ==============================================================================
if exist "Targets\Compound\KapeTriage.tkape" (
    echo  [1/2] KapeTriage ^(Windows + EZ Tools^)...
    kape.exe --tsource C: ^
        --target KapeTriage ^
        --targetdir "%BASE%\Targets" ^
        --mdest "%BASE%\Modules" ^
        --moduledir "%BASE%\Modules" ^
        --module KapeTriage ^
        --tdest "%TMP%\KAPE" ^
        --zip "%BASE%\%HOST%-KAPE-%DATE%.zip" ^
        --hv nc,vm --vss

    if exist "%BASE%\%HOST%-KAPE-%DATE%.zip" (
        certutil -hashfile "%BASE%\%HOST%-KAPE-%DATE%.zip" SHA256 > "%BASE%\%HOST%-KAPE-%DATE%.zip.sha256.tmp" 2>&1
        for /f "delims=" %%h in ('findstr /R "^[a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9]" "%BASE%\%HOST%-KAPE-%DATE%.zip.sha256.tmp" 2^>nul') do (
            > "%BASE%\%HOST%-KAPE-%DATE%.zip.sha256" echo %%h
        )
        del "%BASE%\%HOST%-KAPE-%DATE%.zip.sha256.tmp" 2>nul
        echo         [OK] %HOST%-KAPE-%DATE%.zip
    ) else (
        echo         [FALLO] Pass 1 no genero ZIP. Continuando a Pass 2.
    )
) else (
    echo  [1/2] SKIP - Targets\Compound\KapeTriage.tkape no encontrado.
    echo         ^(Falta Targets\ del kape.zip.^)
)

REM ==============================================================================
REM  PASS 2 - RockwellFactoryTalk (OT custom)
REM ==============================================================================
echo.
echo  [2/2] RockwellFactoryTalk ^(OT^)...
kape.exe --tsource C: ^
    --target RockwellFactoryTalk ^
    --targetdir "%BASE%" ^
    --tdest "%TMP%\FTV" ^
    --zip "%BASE%\%HOST%-FTV-%DATE%.zip" ^
    --hv nc,vm --vss

if exist "%BASE%\%HOST%-FTV-%DATE%.zip" (
    certutil -hashfile "%BASE%\%HOST%-FTV-%DATE%.zip" SHA256 > "%BASE%\%HOST%-FTV-%DATE%.zip.sha256.tmp" 2>&1
    for /f "delims=" %%h in ('findstr /R "^[a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9][a-fA-F0-9]" "%BASE%\%HOST%-FTV-%DATE%.zip.sha256.tmp" 2^>nul') do (
        > "%BASE%\%HOST%-FTV-%DATE%.zip.sha256" echo %%h
    )
    del "%BASE%\%HOST%-FTV-%DATE%.zip.sha256.tmp" 2>nul
    echo         [OK] %HOST%-FTV-%DATE%.zip
) else (
    echo         [FALLO] Pass 2 no genero ZIP.
)

echo.
echo  ===============================================================
echo   Listo. Outputs en %BASE%:
echo  ===============================================================
if exist "%BASE%\*.zip" dir /b "%BASE%\*.zip" 2>nul
if exist "%BASE%\*.sha256" echo  (^y los .sha256 al lado de cada .zip^)
echo.
pause
endlocal & exit /b 0
