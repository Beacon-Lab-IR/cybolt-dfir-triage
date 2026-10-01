#!/bin/bash
# ==============================================================================
#  KAPE_TRIAGE.sh - Port bash de KAPE_TRIAGE.cmd para testing
# ==============================================================================
#  Replica la logica del .cmd pero con bash + mocks.
#  Para validar el flujo sin necesitar Windows real.
#
#  Uso:
#    SCENARIO_DIR=<path> ./KAPE_TRIAGE.sh
#
#  Requiere que $SCENARIO_DIR tenga:
#    - scenario.conf con IS_ADMIN, DFIR_DEVICEID, etc.
#    - mock_cd/ como raiz del CD-ROM virtual
#    - output/ como VMDK de salida
#
#  Variables de entorno:
#    SCENARIO_DIR  - directorio del escenario (obligatorio)
#    MOCK_CD       - ruta al CD mockeado (default: $SCENARIO_DIR/mock_cd)
#    MOCK_OUTPUT   - ruta al output mockeado (default: $SCENARIO_DIR/output)
#    AUTO_VM       - si vale "1" o "2", salta el prompt y elige esa VM
#    AUTO_CONFIRM  - si vale "SI", salta el prompt de confirmacion
#    PATH_OVERRIDE - directorio con los mocks (default: ./bin)
# ==============================================================================

set -u

# Setup paths
SCENARIO_DIR="${SCENARIO_DIR:?SCENARIO_DIR es obligatorio}"
MOCK_CD="${MOCK_CD:-$SCENARIO_DIR/mock_cd}"
MOCK_OUTPUT="${MOCK_OUTPUT:-$SCENARIO_DIR/output}"
PATH_OVERRIDE="${PATH_OVERRIDE:-$(dirname "$0")/bin}"
AUTO_VM="${AUTO_VM:-}"
AUTO_CONFIRM="${AUTO_CONFIRM:-}"

# Poner los mocks al inicio del PATH
export PATH="$PATH_OVERRIDE:$PATH"
export SCENARIO_DIR
export MOCK_CD

mkdir -p "$MOCK_OUTPUT"

# Funciones auxiliares
section() {
    echo ""
    echo "  ----------------------------------------------------------------"
    echo "  [$1]"
    echo "  ----------------------------------------------------------------"
}

ok()    { echo "   [OK]    $1"; }
warn()  { echo "   [WARN]  $1"; }
fail()  { echo "   [FAIL]  $1"; }
info()  { echo "          $1"; }
suggest(){ echo "          Sugerencia: $1"; }

cdrom_root() {
    # Simula %~dp0 — directorio donde esta el script
    # Para KAPE_TRIAGE.sh, eso es el directorio del CD mockeado
    echo "$MOCK_CD"
}

strip_colon() {
    local v="$1"
    if [[ "$v" == *":" ]]; then
        echo "${v%:}"
    else
        echo "$v"
    fi
}

get_conf() {
    grep "^$1=" "$SCENARIO_DIR/scenario.conf" | cut -d= -f2- | tr -d '\r'
}

# ==============================================================================
# Banner inicial
# ==============================================================================
clear 2>/dev/null || true
echo "  ==============================================================="
echo "    KAPE Triage  -  Adquisicion DFIR automatica (offline)"
echo "    Caso: ransomware Windows sobre VMware ESXi"
echo "  ==============================================================="
echo ""
echo "  [HARNESS] SCENARIO_DIR=$SCENARIO_DIR"
echo "  [HARNESS] MOCK_CD=$MOCK_CD"
echo "  [HARNESS] MOCK_OUTPUT=$MOCK_OUTPUT"
echo "  [HARNESS] PATH override: $PATH_OVERRIDE"
echo ""

# ==============================================================================
# 0. Auto-elevacion
# ==============================================================================
section "0. Privilegios de administrador"
if net session >/dev/null 2>&1; then
    ok "Sesion con privilegios de administrador."
else
    warn "Sin privilegios. (Harness: saltando elevacion, simulando admin...)"
    info "(En Windows real: UAC prompt y re-launch con RunAs)"
    # En el harness, no podemos re-elevar. Solo informamos.
    export IS_ADMIN_OVERRIDE=1
fi
echo ""

# ==============================================================================
# 1. Auto-detectar ruta del CD
# ==============================================================================
section "1. CD-ROM detectado"
ISO_DIR="$(cdrom_root)"
KAPE_REAL="$ISO_DIR/kape.exe"
KAPE="$PATH_OVERRIDE/kape"   # harness usa el mock, NO el binario Windows
TARGET_DIR="$ISO_DIR/Targets"
MODULE_DIR="$ISO_DIR/Modules"
echo "       >> CD-ROM: $ISO_DIR"
echo "       >> kape (real, NO ejecutado): $KAPE_REAL"
echo "       >> kape (harness mock):      $KAPE"

if [ ! -f "$KAPE_REAL" ]; then
    fail "No se encuentra kape.exe en $ISO_DIR"
    suggest "Verificar que la ISO KAPE-MEDIA esta montada como CD/DVD virtual."
    exit 1
fi
ok "kape.exe accesible (real, no se ejecuta en el harness)."
echo ""

# ==============================================================================
# 2. Auto-detectar unidad de salida
# ==============================================================================
section "2. Buscando VMDK de salida (label DFIR_OUTPUT) ..."
# wmic mock devuelve "DeviceID=E:" o vacio
OUT_LET_RAW=$(wmic logicaldisk where "VolumeName='DFIR_OUTPUT'" get DeviceID /value 2>/dev/null | grep "^DeviceID=" | cut -d= -f2)
if [ -z "$OUT_LET_RAW" ]; then
    fail "No se encuentra unidad con etiqueta DFIR_OUTPUT."
    echo ""
    echo "          Unidades visibles ahora:"
    wmic logicaldisk get DeviceID,VolumeName,FileSystem,Size,FreeSpace 2>/dev/null | head -20
    exit 1
fi

OUT_LET="$(strip_colon "$OUT_LET_RAW")"
echo "       >> Unidad de salida: $OUT_LET: (label DFIR_OUTPUT)"
ok "Unidad detectada: $OUT_LET:"

mkdir -p "$MOCK_OUTPUT/NOTES" 2>/dev/null
mkdir -p "$MOCK_OUTPUT/Modules" 2>/dev/null
echo ""

# ==============================================================================
# 3. Preguntar VM
# ==============================================================================
section "3. Identifique la VM que va a procesar:"
echo "         1 >> VM01"
echo "         2 >> VM02"
echo ""

if [ -n "$AUTO_VM" ]; then
    VM_CHOICE="$AUTO_VM"
    echo "  [HARNESS] AUTO_VM=$AUTO_VM, saltando prompt."
else
    read -p "Numero de VM [1/2]: " VM_CHOICE
fi

case "$VM_CHOICE" in
    1) VM_NUM="01" ;;
    2) VM_NUM="02" ;;
    *) fail "Opcion invalida. Escriba 1 o 2."; exit 1 ;;
esac
echo "       >> VM$VM_NUM seleccionada."
echo ""

# ==============================================================================
# 4. Fecha y hostname
# ==============================================================================
section "4. Fecha y hostname"
DT=$(wmic os get localdatetime /value 2>/dev/null | grep "^LocalDateTime=" | cut -d= -f2)
FECHA="${DT:0:8}"
HOSTNAME="VM${VM_NUM}-HOST"  # mock hostname
echo "       >> Fecha=$FECHA  Hostname=$HOSTNAME"

OUT_BASE="$MOCK_OUTPUT/VM${VM_NUM}-KAPE-$FECHA"
OUT_ZIP="$MOCK_OUTPUT/VM${VM_NUM}-KAPE-$FECHA.zip"
NOTES="$MOCK_OUTPUT/NOTES/VM${VM_NUM}-NOTES.txt"
echo "       >> ZIP de salida: $OUT_ZIP"
echo "       >> NOTES:         $NOTES"
echo ""

# ==============================================================================
# 5. Validar target
# ==============================================================================
section "5. Validando target KapeTriage"
if [ ! -f "$TARGET_DIR/Compound/KapeTriage.tkape" ]; then
    fail "No se encuentra $TARGET_DIR/Compound/KapeTriage.tkape"
    exit 1
fi
ok "KapeTriage.tkape presente."

"$KAPE" --target KapeTriage --tlist 2>/dev/null | grep -i "KapeTriage" >/dev/null
if [ $? -eq 0 ]; then
    ok "KAPE resuelve el target correctamente."
else
    warn "KAPE no devolvio info del target. Continuando de todos modos..."
fi
echo ""

# ==============================================================================
# Resumen y confirmacion
# ==============================================================================
echo "  ==============================================================="
echo "    RESUMEN - revise antes de continuar"
echo "  ==============================================================="
echo "    Origen    : C:\\"
echo "    ISO       : $ISO_DIR"
echo "    Salida    : $OUT_LET:\\  (label DFIR_OUTPUT)"
echo "    VM        : VM$VM_NUM  ($HOSTNAME)"
echo "    Target    : KapeTriage"
echo "    ZIP       : VM${VM_NUM}-KAPE-${FECHA}.zip"
echo "    SHA-256   : se calculara al terminar"
echo "    NOTAS     : NOTES/VM${VM_NUM}-NOTES.txt"
echo "  ==============================================================="
echo ""

if [ -n "$AUTO_CONFIRM" ]; then
    CONFIRM="$AUTO_CONFIRM"
    echo "  [HARNESS] AUTO_CONFIRM=$AUTO_CONFIRM, saltando prompt."
else
    read -p "Escribir SI para continuar (SI/no): " CONFIRM
fi

if [ "$CONFIRM" != "SI" ]; then
    fail "ABORTADO. No se ejecuto KAPE."
    exit 2
fi

# ==============================================================================
# 6. Ejecutar KAPE
# ==============================================================================
section "6. Iniciando KAPE ..."
echo ""

# Generar NOTES
{
    echo "============================================================"
    echo " KAPE Triage - Notas de adquisicion"
    echo "============================================================"
    echo " VM              : VM$VM_NUM"
    echo " Hostname        : $HOSTNAME"
    echo " Operador        : harness"
    echo " Fecha           : $FECHA"
    echo " ISO montada en : $ISO_DIR"
    echo " Salida en       : $OUT_LET:\\  (label DFIR_OUTPUT)"
    echo " Target          : KapeTriage"
    echo " Source          : C:\\"
    echo " Inicio          : $(date '+%Y-%m-%d %H:%M:%S')"
    echo " Comando (mock):"
    echo "   kape --tsource C: --target KapeTriage"
    echo "       --tdest $OUT_BASE"
    echo "       --targetdir $TARGET_DIR"
    echo "       --zip $OUT_ZIP"
    echo "       --mdest $MOCK_OUTPUT/Modules"
    echo "       --moduledir $MODULE_DIR"
    echo "       --hv nc,vm --sync \$Null --gui"
    echo "============================================================"
} > "$NOTES"

"$KAPE" \
    --tsource C: \
    --target KapeTriage \
    --tdest "$OUT_BASE" \
    --targetdir "$TARGET_DIR" \
    --zip "$OUT_ZIP" \
    --mdest "$MOCK_OUTPUT/Modules" \
    --moduledir "$MODULE_DIR" \
    --hv nc,vm \
    --sync '$Null' \
    --gui

RC=$?
echo ""
echo "  ==============================================================="
echo "  KAPE termino con codigo de salida $RC."
echo "  ==============================================================="
echo "Fin: $(date '+%Y-%m-%d %H:%M:%S')" >> "$NOTES"
echo "KapeExitCode: $RC" >> "$NOTES"

# ==============================================================================
# Validar output y calcular hash
# ==============================================================================
if [ -f "$OUT_ZIP" ]; then
    section "Calculando SHA-256 ..."
    HASH=$(certutil -hashfile "$OUT_ZIP" SHA256 2>/dev/null | grep -E "^[a-fA-F0-9]{64}$" | head -1)
    if [ -n "$HASH" ]; then
        echo "$HASH  VM${VM_NUM}-KAPE-${FECHA}.zip" > "$OUT_ZIP.sha256"
        echo "  SHA-256: $HASH"
        echo "  Guardado en: $OUT_ZIP.sha256"
        echo "SHA-256: $HASH" >> "$NOTES"
        SIZE=$(stat -f%z "$OUT_ZIP" 2>/dev/null || stat -c%s "$OUT_ZIP" 2>/dev/null)
        echo "ZIPSize: $SIZE bytes" >> "$NOTES"
    else
        warn "No se pudo calcular SHA-256 automaticamente."
    fi

    echo ""
    echo "  Tamano del ZIP:"
    echo "    $(ls -lh "$OUT_ZIP" | awk '{print $5, $9}')"
    echo ""
    echo "  Archivos generados en $OUT_LET:\\:"
    echo "    $OUT_ZIP"
    echo "    $OUT_ZIP.sha256"
    echo "    $NOTES"
else
    fail "No se genero el ZIP. Revise $NOTES y los logs de KAPE."
    echo "ZipGenerado: NO" >> "$NOTES"
    exit 3
fi

echo ""
echo "  ==============================================================="
echo "   Adquisicion de VM$VM_NUM completada."
echo "  ==============================================================="
