#!/usr/bin/env bash
# build.sh - Compila las dos GUIs, descarga winpmem, y genera el ISO final.
#
# Uso:
#   ./build.sh                    # usa la version leida de staging/VERSION.txt
#   ./build.sh 5.2.18             # fuerza la version 5.2.18
#
# Salidas:
#   dist/KAPE-MEDIA-v<VERSION>.iso    ISO final listo para subir al datastore

set -euo pipefail

if [ $# -eq 1 ]; then
    VERSION="$1"
else
    VERSION=$(grep -oE 'v5\.[0-9]+\.[0-9]+' staging/VERSION.txt | head -1 | sed 's/^v//')
    if [ -z "$VERSION" ]; then
        echo "ERROR: no se pudo detectar la version de staging/VERSION.txt"
        echo "Pasala como argumento: $0 5.2.18"
        exit 1
    fi
fi

echo ">>> Building KAPE-MEDIA v$VERSION"

# 0) Overlay Cybolt (iso/) si existe
if [ -d iso ]; then
    echo ">>> Copiando iso/ -> staging/"
    mkdir -p staging/Tools/Memory
    cp -a iso/. staging/
fi

# 1) Compilar las dos GUIs
echo ">>> Compilando KAPE_TRIAGE_UI..."
dotnet build KapeUi/KapeUi.csproj -c Release --nologo
mkdir -p staging
cp KapeUi/bin/Release/KAPE_TRIAGE_UI.exe staging/

echo ">>> Compilando RAM_CAPTURE_UI..."
dotnet build RamCaptureUi/RamCaptureUi.csproj -c Release --nologo
cp RamCaptureUi/bin/Release/RAM_CAPTURE_UI.exe staging/

# 2) Verificar/descargar winpmem
echo ">>> Verificando winpmem..."
WINPMEM_PATH="staging/Tools/Memory/winpmem_mini_x64_rc2.exe"
EXPECTED_SHA="a4d516b6fcaf3b5b1d4ee709ce86f8eabf1d8028b3a83101479b7568b933d21b"

mkdir -p staging/Tools/Memory
if [ ! -f "$WINPMEM_PATH" ]; then
    echo "    Descargando winpmem..."
    curl -fsSL -o "$WINPMEM_PATH" \
        "https://github.com/Velocidex/WinPmem/releases/download/v4.0.rc1/winpmem_mini_x64_rc2.exe"
fi

ACTUAL_SHA=$(sha256sum "$WINPMEM_PATH" | awk '{print $1}')
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
    echo "ERROR: winpmem SHA-256 mismatch"
    echo "  expected: $EXPECTED_SHA"
    echo "  actual:   $ACTUAL_SHA"
    exit 1
fi

# 3) Regenerar ISO
echo ">>> Generando ISO..."
mkdir -p dist
ISO_NAME="KAPE-MEDIA-v$VERSION.iso"

if [ "$(uname)" = "Darwin" ]; then
    hdiutil makehybrid -joliet -iso -no-emul-boot -no-boot \
        -o "dist/$ISO_NAME" staging/
elif [ "$(uname)" = "Linux" ]; then
    genisoimage -R -J -joliet-long \
        -V "KAPE-MEDIA-v$VERSION" \
        -o "dist/$ISO_NAME" staging/
else
    echo "ERROR: OS no soportado: $(uname)"
    echo "Usa macOS o Linux. En Windows correr build.ps1 (no incluido)."
    exit 1
fi

# 4) Calcular SHA-256 final
ISO_SHA=$(sha256sum "dist/$ISO_NAME" | awk '{print $1}')
ISO_SIZE=$(stat -f%z "dist/$ISO_NAME" 2>/dev/null || stat -c%s "dist/$ISO_NAME")

echo ""
echo "==================================================================="
echo "  BUILD OK: KAPE-MEDIA v$VERSION"
echo "==================================================================="
echo ""
echo "  Archivo : dist/$ISO_NAME"
echo "  Tamano  : $ISO_SIZE bytes ($(echo "scale=2; $ISO_SIZE/1048576" | bc) MiB)"
echo "  SHA-256 : $ISO_SHA"
echo ""
echo "  Subilo a tu datastore via vCenter Web UI:"
echo "    https://<vcenter-host>/ui -> <nodo> -> datastore1 -> Upload"
echo ""
echo "  O crea un GitHub Release:"
echo "    gh release create v$VERSION dist/$ISO_NAME --repo <org>/kape-media-releases \\"
echo "        --title \"KAPE-MEDIA v$VERSION\" \\"
echo "        --notes \"SHA-256: $ISO_SHA\""
echo ""
