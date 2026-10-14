#!/usr/bin/env bash
# build.sh - Compila el wizard de Triage y genera CyboltDFIR-Triage-v<VERSION>.zip
#
# Uso:
#   ./build.sh                    # usa version leida de VERSION.txt
#   ./build.sh 5.3.1              # fuerza version
#
# Output:
#   dist/CyboltDFIR-Triage-v<VERSION>.zip
#
# Este proyecto es self-contained: compila su propio binario, baja sus propios
# modulos (kape.zip), y arma el bundle. Para el ISO que contiene los 3
# proyectos, ver Beacon-Lab-IR/cybolt-dfir-iso.

set -euo pipefail

if [ $# -eq 1 ]; then
    VERSION="$1"
else
    VERSION=$(grep -oE 'v5\.[0-9]+\.[0-9]+' VERSION.txt | head -1 | sed 's/^v//')
    if [ -z "$VERSION" ]; then
        echo "ERROR: no se pudo detectar la version de VERSION.txt"
        echo "Pasala como argumento: $0 5.3.1"
        exit 1
    fi
fi

echo "==================================================================="
echo "  Building CyboltDFIR Triage v$VERSION"
echo "==================================================================="
echo ""

# Helper: human-readable size
human_size() {
    local bytes=$1
    if [ -z "$bytes" ] || [ "$bytes" = "0" ]; then echo "0B"
    elif [ "$bytes" -lt 1024 ]; then echo "${bytes}B"
    elif [ "$bytes" -lt 1048576 ]; then echo "$(echo "scale=2; $bytes/1024" | bc)KB"
    else echo "$(echo "scale=2; $bytes/1048576" | bc)MiB"
    fi
}

# 0.5) Fetch targets-full.txt desde el gist publico (config separada del codigo).
# Gist: https://gist.github.com/Piuliss/b80cdd12263214ed82f5f7c45dd6f9e6
# Permite actualizar la lista de targets sin recompilar la GUI.
GIST_ID="b80cdd12263214ed82f5f7c45dd6f9e6"
TARGETS_URL="https://gist.githubusercontent.com/Piuliss/${GIST_ID}/raw/targets-full.txt"
if [ ! -f targets-full.txt ]; then
    echo ">>> Fetching targets-full.txt del gist ${GIST_ID}"
    if ! curl -fsSL -o targets-full.txt "$TARGETS_URL"; then
        echo "WARN: no se pudo descargar. El GUI usara fallback KapeTriage."
    else
        echo "    OK: $(wc -l < targets-full.txt) targets cargados"
    fi
else
    echo ">>> targets-full.txt ya existe (skip fetch)"
fi

# 1) Compilar la GUI
echo ">>> [1/3] Compilando CYBOLT_DFIR_TRIAGE..."
/Users/rbnetto/.dotnet/dotnet build DfirTriageUi/DfirTriageUi.csproj -c Release --nologo 2>&1 | tail -5
mkdir -p staging
cp DfirTriageUi/bin/Release/CYBOLT_DFIR_TRIAGE.exe staging/

# 2) Verificar/descargar kape.zip + descomprimir en staging/
echo ">>> [2/3] Preparando kape.zip + Targets/..."
KAPE_ZIP="kape.zip"
KAPE_SHA_EXPECTED="92b687b765d384f6b238c344bed239f2eaebc8a1fb16185b969d80cac71bf892"
if [ ! -f "$KAPE_ZIP" ]; then
    echo "    Descargando kape.zip..."
    curl -fsSL -o "$KAPE_ZIP" "https://github.com/EricZimmerman/KAPE/releases/download/v1.3.0.2/kape.zip"
fi
ACTUAL_SHA=$( (sha256sum "$KAPE_ZIP" 2>/dev/null || shasum -a 256 "$KAPE_ZIP") | awk '{print $1}')
if [ "$ACTUAL_SHA" != "$KAPE_SHA_EXPECTED" ]; then
    echo "ERROR: kape.zip SHA-256 mismatch"; exit 1
fi
echo "    OK: kape.zip ($(human_size $(stat -f%z "$KAPE_ZIP")))"

# Copiar el kape.zip al staging para que vaya en el bundle
cp "$KAPE_ZIP" staging/

# 3) Verificar/descargar winpmem
echo ">>> [3/3] Verificando winpmem..."
WINPMEM_PATH="staging/Tools/Memory/winpmem_mini_x64_rc2.exe"
EXPECTED_SHA="a4d516b6fcaf3b5b1d4ee709ce86f8eabf1d8028b3a83101479b7568b933d21b"
mkdir -p staging/Tools/Memory
if [ ! -f "$WINPMEM_PATH" ]; then
    curl -fsSL -o "$WINPMEM_PATH" \
        "https://github.com/Velocidex/WinPmem/releases/download/v4.0.rc1/winpmem_mini_x64_rc2.exe"
fi
ACTUAL_SHA=$( (sha256sum "$WINPMEM_PATH" 2>/dev/null || shasum -a 256 "$WINPMEM_PATH") | awk '{print $1}')
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
    echo "ERROR: winpmem SHA-256 mismatch"; exit 1
fi

# 4) Copiar el resto del bundle (docs, autorun, etc)
cp VERSION.txt staging/
cp LICENSE staging/
[ -f autorun.inf ] && cp autorun.inf staging/
[ -f README.md ] && cp README.md staging/
[ -f README.txt ] && cp README.txt staging/
[ -f MANUAL_OPERATIVO.txt ] && cp MANUAL_OPERATIVO.txt staging/
[ -f SHA256SUMS.txt ] && cp SHA256SUMS.txt staging/
[ -f KAPE_TRIAGE_UI.exe.config ] && cp KAPE_TRIAGE_UI.exe.config staging/
[ -f targets-full.txt ] && cp targets-full.txt staging/

# 5) Empaquetar ZIP
echo ">>> Empaquetando ZIP..."
mkdir -p dist
TRIAGE_ZIP="$(pwd)/dist/CyboltDFIR-Triage-v$VERSION.zip"
rm -f "$TRIAGE_ZIP"
( cd staging && zip -qr "$TRIAGE_ZIP" . -x "*.DS_Store" "*/.DS_Store" )
TRIAGE_SIZE=$(stat -f%z "$TRIAGE_ZIP" 2>/dev/null || stat -c%s "$TRIAGE_ZIP")
TRIAGE_SHA=$( (sha256sum "$TRIAGE_ZIP" 2>/dev/null || shasum -a 256 "$TRIAGE_ZIP") | awk '{print $1}')

cat <<EOF

===================================================================
  BUILD OK: CyboltDFIR Triage v$VERSION
===================================================================

  File : dist/CyboltDFIR-Triage-v$VERSION.zip
  Size : $TRIAGE_SIZE bytes ($(echo "scale=2; $TRIAGE_SIZE/1048576" | bc) MiB)
  SHA  : $TRIAGE_SHA

  Crea el GitHub Release:
    gh release create v$VERSION \\
      dist/CyboltDFIR-Triage-v$VERSION.zip \\
      --repo Beacon-Lab-IR/cybolt-dfir-triage \\
      --title "CyboltDFIR Triage v$VERSION" \\
      --notes-file RELEASE-NOTES.md
EOF
