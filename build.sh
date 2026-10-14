#!/usr/bin/env bash
# build.sh - Genera los 3 artifacts de release de CyboltDFIR
#
# Uso:
#   ./build.sh                    # usa version leida de staging/VERSION.txt
#   ./build.sh 5.3.1              # fuerza version
#
# Outputs (en dist/):
#   CyboltDFIR-Triage-v<VERSION>.zip      Bundle completo, listo para USB en VM live
#                                         Incluye CYBOLT_DFIR_TRIAGE.exe + kape.exe + Targets/
#                                         + Modules + Tools/Memory + kape.zip (553 MB)
#   CyboltDFIR-Uploader-v<VERSION>.zip    Solo DFIR_UPLOADER_UI.exe + configs, sin kape.zip
#                                         Para deploy separado del uploader
#   CyboltDFIR-ISO-v<VERSION>.iso         ISO booteable (no-emul-boot). Contiene ambos bundles
#                                         descomprimidos adentro; se monta como CD-ROM
#                                         desde vCenter datastore.

set -euo pipefail

if [ $# -eq 1 ]; then
    VERSION="$1"
else
    VERSION=$(grep -oE 'v5\.[0-9]+\.[0-9]+' staging/VERSION.txt | head -1 | sed 's/^v//')
    if [ -z "$VERSION" ]; then
        echo "ERROR: no se pudo detectar la version de staging/VERSION.txt"
        echo "Pasala como argumento: $0 5.3.1"
        exit 1
    fi
fi

# Helper: human-readable size (definido antes para usar en steps)
human_size() {
    local bytes=$1
    if [ -z "$bytes" ] || [ "$bytes" = "0" ]; then
        echo "0B"
    elif [ "$bytes" -lt 1024 ]; then
        echo "${bytes}B"
    elif [ "$bytes" -lt 1048576 ]; then
        echo "$(echo "scale=2; $bytes/1024" | bc)KB"
    else
        echo "$(echo "scale=2; $bytes/1048576" | bc)MiB"
    fi
}

echo "==================================================================="
echo "  Building CYBOLT-DFIR v$VERSION (3 artifacts)"
echo "==================================================================="
echo ""

# 0) Overlay Cybolt (iso/) → staging/ si existe
if [ -d iso ]; then
    echo ">>> [0/4] Overlay iso/ → staging/"
    mkdir -p staging/Tools/Memory
    cp -a iso/. staging/
fi

# 0.5) Fetch targets-full.txt desde el gist publico (config separada del codigo).
# Gist: https://gist.github.com/Piuliss/b80cdd12263214ed82f5f7c45dd6f9e6
#   - Solo contiene el archivo targets-full.txt con la lista de targets de KAPE.
#   - Permite actualizar la lista sin recompilar la GUI ni sincronizar kape.zip.
#   - KapeRunner.LoadAndFilterTargets() aplica filtro defensivo: si un target
#     del .txt no existe como .tkape en el bundle, lo skipea en vez de fallar.
GIST_ID="b80cdd12263214ed82f5f7c45dd6f9e6"
TARGETS_URL="https://gist.githubusercontent.com/Piuliss/${GIST_ID}/raw/targets-full.txt"
mkdir -p staging
if [ ! -f staging/targets-full.txt ]; then
    echo ">>> [0/4] Fetching targets-full.txt del gist ${GIST_ID}"
    if ! curl -fsSL -o staging/targets-full.txt "$TARGETS_URL"; then
        echo "    WARN: no se pudo descargar targets-full.txt del gist."
        echo "          El GUI usara fallback KapeTriage. Continuando build..."
    else
        echo "    OK: $(wc -l < staging/targets-full.txt) targets cargados"
    fi
else
    echo ">>> [0/4] targets-full.txt ya existe en staging/ (skip fetch)"
fi

# 1) Compilar las dos GUIs
echo ">>> [1/4] Compilando CYBOLT_DFIR_TRIAGE (wizard: KapeTriage + RAM)..."
/Users/rbnetto/.dotnet/dotnet build DfirTriageUi/DfirTriageUi.csproj -c Release --nologo 2>&1 | tail -5
cp DfirTriageUi/bin/Release/CYBOLT_DFIR_TRIAGE.exe staging/

echo ">>> [2/4] Compilando DFIR_UPLOADER_UI (subida a S3/FTP/SFTP/SMB)..."
/Users/rbnetto/.dotnet/dotnet build DfirUploaderUi/DfirUploaderUi.csproj -c Release --nologo 2>&1 | tail -5
cp DfirUploaderUi/bin/Release/DFIR_UPLOADER_UI.exe staging/

# 2) Verificar/descargar winpmem
echo ">>> [3/4] Verificando winpmem..."
WINPMEM_PATH="staging/Tools/Memory/winpmem_mini_x64_rc2.exe"
EXPECTED_SHA="a4d516b6fcaf3b5b1d4ee709ce86f8eabf1d8028b3a83101479b7568b933d21b"
mkdir -p staging/Tools/Memory
if [ ! -f "$WINPMEM_PATH" ]; then
    echo "    Descargando winpmem..."
    curl -fsSL -o "$WINPMEM_PATH" \
        "https://github.com/Velocidex/WinPmem/releases/download/v4.0.rc1/winpmem_mini_x64_rc2.exe"
fi
ACTUAL_SHA=$( (sha256sum "$WINPMEM_PATH" 2>/dev/null || shasum -a 256 "$WINPMEM_PATH") | awk '{print $1}')
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
    echo "ERROR: winpmem SHA-256 mismatch (expected: $EXPECTED_SHA, actual: $ACTUAL_SHA)"
    exit 1
fi

# 3) Empaquetar los 3 artifacts
echo ">>> [4/4] Empaquetando 3 artifacts..."
mkdir -p dist

# Artifact 1: Triage full zip (todo el bundle con kape.zip)
TRIAGE_ZIP="$(pwd)/dist/CyboltDFIR-Triage-v$VERSION.zip"
rm -f "$TRIAGE_ZIP"
( cd staging && zip -qr "$TRIAGE_ZIP" . -x "*.DS_Store" "*/.DS_Store" )
TRIAGE_SIZE=$(stat -f%z "$TRIAGE_ZIP" 2>/dev/null || stat -c%s "$TRIAGE_ZIP")
TRIAGE_SHA=$( (sha256sum "$TRIAGE_ZIP" 2>/dev/null || shasum -a 256 "$TRIAGE_ZIP") | awk '{print $1}')
echo "    [1/3] Triage:   $(human_size $TRIAGE_SIZE)"

# Artifact 2: Uploader alone (sin kape.zip, sin Targets/)
UPLOADER_STAGING="dist/.tmp-uploader-staging-$VERSION"
rm -rf "$UPLOADER_STAGING"
mkdir -p "$UPLOADER_STAGING"
cp staging/DFIR_UPLOADER_UI.exe "$UPLOADER_STAGING/"
cp staging/VERSION.txt      "$UPLOADER_STAGING/"
cp staging/LICENSE          "$UPLOADER_STAGING/"
[ -f staging/README.txt ] && cp staging/README.txt "$UPLOADER_STAGING/"
UPLOADER_ZIP="$(pwd)/dist/CyboltDFIR-Uploader-v$VERSION.zip"
rm -f "$UPLOADER_ZIP"
( cd "$UPLOADER_STAGING" && zip -qr "$UPLOADER_ZIP" . )
rm -rf "$UPLOADER_STAGING"
UPLOADER_SIZE=$(stat -f%z "$UPLOADER_ZIP" 2>/dev/null || stat -c%s "$UPLOADER_ZIP")
UPLOADER_SHA=$( (sha256sum "$UPLOADER_ZIP" 2>/dev/null || shasum -a 256 "$UPLOADER_ZIP") | awk '{print $1}')
echo "    [2/3] Uploader: $(human_size $UPLOADER_SIZE)"

# Artifact 3: ISO booteable (con ambos descomprimidos adentro)
ISO_PATH="$(pwd)/dist/CyboltDFIR-ISO-v$VERSION.iso"
rm -f "$ISO_PATH"
ISO_SIZE=0
ISO_SHA="(n/a)"
if [ "$(uname)" = "Darwin" ]; then
    hdiutil makehybrid -joliet -iso -no-emul-boot -no-boot \
        -o "$ISO_PATH" staging/ >/dev/null
elif [ "$(uname)" = "Linux" ]; then
    genisoimage -R -J -joliet-long -V "CYBOLT-DFIR-ISO-v$VERSION" \
        -o "$ISO_PATH" staging/
else
    echo "WARN: OS no soportado para ISO ($(uname)). Saltando Artifact 3."
    ISO_PATH="(no generado)"
fi
if [ -f "$ISO_PATH" ]; then
    ISO_SIZE=$(stat -f%z "$ISO_PATH" 2>/dev/null || stat -c%s "$ISO_PATH")
    ISO_SHA=$( (sha256sum "$ISO_PATH" 2>/dev/null || shasum -a 256 "$ISO_PATH") | awk '{print $1}')
    echo "    [3/3] ISO:      $(human_size $ISO_SIZE)"
fi

cat <<EOF

===================================================================
  BUILD OK: CYBOLT-DFIR v$VERSION
===================================================================

  [1] Triage full bundle (USB para VM live / operador arranca el wizard)
      File : dist/CyboltDFIR-Triage-v$VERSION.zip
      Size : $TRIAGE_SIZE bytes ($(echo "scale=2; $TRIAGE_SIZE/1048576" | bc) MiB)
      SHA  : $TRIAGE_SHA

  [2] Uploader alone (deploy separado del uploader, sin kape.zip)
      File : dist/CyboltDFIR-Uploader-v$VERSION.zip
      Size : $UPLOADER_SIZE bytes ($(echo "scale=2; $UPLOADER_SIZE/1048576" | bc) MiB)
      SHA  : $UPLOADER_SHA

  [3] ISO (bootable desde datastore; contiene ambos descomprimidos)
      File : dist/CyboltDFIR-ISO-v$VERSION.iso
      Size : $ISO_SIZE bytes ($(echo "scale=2; $ISO_SIZE/1048576" | bc) MiB)
      SHA  : $ISO_SHA

  Subelo a tu datastore via vCenter Web UI:
    https://<vcenter-host>/ui -> <nodo> -> datastore1 -> Upload

  O crea un GitHub Release:
    gh release create v$VERSION \\
      $TRIAGE_ZIP $UPLOADER_ZIP $ISO_PATH \\
      --repo <org>/cybolt-dfir-triage-releases \\
      --title "CYBOLT-DFIR v$VERSION" \\
      --notes "SHA-256 Triage: $TRIAGE_SHA, Uploader: $UPLOADER_SHA, ISO: $ISO_SHA"
EOF
