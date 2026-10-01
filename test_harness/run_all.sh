#!/bin/bash
# ==============================================================================
#  run_all.sh - Ejecuta todos los escenarios de prueba del harness
# ==============================================================================
#  Corre KAPE_TRIAGE.sh contra cada escenario, captura salida y exit code,
#  resume resultados al final.
#
#  Uso:
#    ./run_all.sh           # corre todos
#    ./run_all.sh ok_vm01   # corre solo uno
# ==============================================================================

set -u

HARNESS_DIR="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$HARNESS_DIR/KAPE_TRIAGE.sh"
RESULTS_DIR="$HARNESS_DIR/results"
mkdir -p "$RESULTS_DIR"

# Escenarios disponibles
SCENARIOS=(
    "ok_vm01:VM01 happy path"
    "ok_vm02:VM02 happy path (Server 2022)"
    "fail_no_output:no hay VMDK DFIR_OUTPUT conectado"
    "fail_no_admin:no es administrador (deberia elevar)"
)

run_scenario() {
    local sc="$1"
    local desc="$2"
    local scen_dir="$HARNESS_DIR/scenarios/$sc"
    local out_file="$RESULTS_DIR/${sc}.log"
    local err_file="$RESULTS_DIR/${sc}.err"

    echo ""
    echo "==============================================================="
    echo " ESCENARIO: $sc"
    echo " Descripcion: $desc"
    echo "==============================================================="

    if [ ! -d "$scen_dir" ]; then
        echo "  [SKIP] directorio $scen_dir no existe"
        return 2
    fi

    rm -rf "$scen_dir/output"
    mkdir -p "$scen_dir/output"

    SCENARIO_DIR="$scen_dir" \
    MOCK_OUTPUT="$scen_dir/output" \
    AUTO_VM=1 \
    AUTO_CONFIRM=SI \
    bash "$SCRIPT" > "$out_file" 2> "$err_file"

    local rc=$?
    local last_lines=$(tail -3 "$out_file" 2>/dev/null | tr '\n' ' ')
    if [ $rc -eq 0 ]; then
        echo "  [PASS] exit=$rc"
    else
        echo "  [FAIL] exit=$rc"
    fi
    echo "          (last lines: $last_lines)"
    return $rc
}

# Filtrar si pasaron argumentos
if [ $# -gt 0 ]; then
    FILTER="$1"
    FILTERED=()
    for s in "${SCENARIOS[@]}"; do
        name="${s%%:*}"
        if [[ "$name" == *"$FILTER"* ]]; then
            FILTERED+=("$s")
        fi
    done
    if [ ${#FILTERED[@]} -eq 0 ]; then
        echo "No hay escenarios que coincidan con: $FILTER"
        echo "Disponibles:"
        for s in "${SCENARIOS[@]}"; do echo "  - $s"; done
        exit 1
    fi
    SCENARIOS=("${FILTERED[@]}")
fi

echo ""
echo "Ejecutando ${#SCENARIOS[@]} escenario(s)..."
echo ""

PASS=0
FAIL=0
SKIP=0
RESULTS=()

for s in "${SCENARIOS[@]}"; do
    name="${s%%:*}"
    desc="${s#*:}"
    run_scenario "$name" "$desc"
    rc=$?
    case $rc in
        0) PASS=$((PASS+1)); RESULTS+=("$name: PASS");;
        *) FAIL=$((FAIL+1)); RESULTS+=("$name: FAIL (exit=$rc)");;
    esac
done

echo ""
echo "==============================================================="
echo " RESUMEN"
echo "==============================================================="
echo ""
for r in "${RESULTS[@]}"; do
    echo "  $r"
done
echo ""
echo "  PASS: $PASS"
echo "  FAIL: $FAIL"
echo ""
echo "  Logs en: $RESULTS_DIR/"
echo "  Por cada escenario: <escenario>.log (stdout) + <escenario>.err (stderr)"
echo ""

if [ $FAIL -gt 0 ]; then
    exit 1
fi
exit 0
