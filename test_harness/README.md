# Test Harness — KAPE-MEDIA ISO

Harness de pruebas para validar la lógica de `KAPE_TRIAGE.cmd` y `CHECK_COMPAT.cmd`
sin necesitar un Windows ni ESXi real. Corre 100% en macOS / Linux usando bash
+ Python (que ya están instalados).

## Qué prueba

| Componente | Cubre |
|---|---|
| Detección de CD-ROM virtual | sí (vía symlink a staging) |
| Detección de VMDK por label "DFIR_OUTPUT" | sí (mock de wmic) |
| Auto-elevación a admin | parcial (mock: continúa con advertencia) |
| Validación de target `KapeTriage` | sí |
| Cálculo de SHA-256 con certutil | sí (mock usa `shasum`) |
| Generación de NOTES y ZIPs | sí (mock de kape genera artefactos) |

Lo que **NO** prueba (es Windows-específico):

- Sintaxis exacta del `.cmd` (`%` vs `!` vs `$()`, paréntesis en bloques)
- UAC real (Start-Process -Verb RunAs)
- Comportamiento exacto de cmd.exe vs bash
- KAPE real contra el sistema de archivos NTFS

Para validar eso, hay que correr el `.cmd` real en Windows (la VM afectada).

## Estructura

```
test_harness/
├── README.md                  ← este archivo
├── KAPE_TRIAGE.sh             ← port bash de KAPE_TRIAGE.cmd
├── run_all.sh                 ← runner de todos los escenarios
├── bin/                       ← mocks de comandos Windows
│   ├── wmic                   ← respuesta configurable desde scenario.conf
│   ├── certutil               ← usa shasum -a 256
│   ├── net                    ← net session configurable
│   ├── powershell             ← no-op (registra la llamada)
│   ├── where                  ← "encuentra" los ejecutables
│   └── kape                   ← mock de kape.exe (crea artefactos)
├── scenarios/                 ← un directorio por caso
│   ├── ok_vm01/
│   │   ├── scenario.conf      ← IS_ADMIN=1, DFIR_DEVICEID=E:, etc.
│   │   ├── mock_cd -> ../../staging
│   │   └── output/            ← generado por la corrida
│   ├── ok_vm02/
│   ├── fail_no_output/        ← DFIR_DEVICEID vacío
│   └── fail_no_admin/         ← IS_ADMIN=0
└── results/                   ← logs de cada corrida (stdout + stderr)
```

## Uso rápido

```bash
cd /Users/rbnetto/Projects/CYBOLT-PROJ/kape-iso/test_harness

# Correr todos los escenarios
./run_all.sh

# Correr uno solo (cualquier substring del nombre)
./run_all.sh ok_vm01
./run_all.sh fail_no_output
```

## Uso manual (más control)

Si querés correr el script a mano (sin el runner) para depurar:

```bash
SCENARIO_DIR=./scenarios/ok_vm01 \
MOCK_OUTPUT=./scenarios/ok_vm01/output \
AUTO_VM=1 \
AUTO_CONFIRM=SI \
bash KAPE_TRIAGE.sh
```

Variables de entorno:

| Variable | Default | Función |
|---|---|---|
| `SCENARIO_DIR` | (obligatorio) | Directorio del escenario; lee `scenario.conf` |
| `MOCK_OUTPUT` | `$SCENARIO_DIR/output` | Donde se crean los artefactos |
| `AUTO_VM` | (vacío) | Si vale `1` o `2`, salta el prompt de selección de VM |
| `AUTO_CONFIRM` | (vacío) | Si vale `SI`, salta el prompt de confirmación final |
| `MOCK_CD` | `$SCENARIO_DIR/mock_cd` | Ruta al CD mockeado (symlink al staging) |
| `PATH_OVERRIDE` | `./bin` | Directorio con los mocks de comandos |

## Agregar un escenario nuevo

1. Crear directorio `scenarios/<nombre>/`
2. Crear `scenario.conf` con las variables necesarias (ver abajo)
3. Crear symlink al CD mockeado:
   ```bash
   ln -s ../../../staging scenarios/<nombre>/mock_cd
   ```
4. Ejecutar:
   ```bash
   ./run_all.sh <nombre>
   ```

### Variables del `scenario.conf`

| Variable | Significado | Ejemplo |
|---|---|---|
| `IS_ADMIN` | ¿La sesión es admin? (1/0) | `1` |
| `DFIR_DEVICEID` | Letra del VMDK DFIR_OUTPUT. Vacío = "no conectado" | `E:` |
| `DFIR_FREESPACE` | Bytes libres en VMDK | `107374182400` (100 GB) |
| `WIN_CAPTION` | Caption devuelto por `wmic os get caption` | `Microsoft Windows 10 Pro` |
| `WIN_VERSION` | Versión devuelta por `wmic os get version` | `10.0.19045` |
| `FAKE_DATE` | Fecha simulada YYYYMMDD | `20260921` |

## Resultado actual (smoke test pasado)

```
  ok_vm01:           PASS   (VM01 happy path)
  ok_vm02:           PASS   (VM02 happy path, Server 2022)
  fail_no_output:    FAIL   (correcto: aborta con mensaje claro)
  fail_no_admin:     PASS   (mock: simula elevación y continúa)

  PASS: 3
  FAIL: 1 (esperado, no es bug)
```

El "FAIL" de `fail_no_output` es **deseado**: queremos validar que cuando
no hay VMDK, el script aborta limpio con exit code != 0 y mensaje útil.

## Cuando cambia el KAPE_TRIAGE.cmd

Si modificás el `.cmd` original (raíz del proyecto), tenés que portar
los cambios al `KAPE_TRIAGE.sh` del harness. Las secciones están
numeradas igual para facilitar la correlación:

| Sección `.cmd` | Línea aprox. en `KAPE_TRIAGE.sh` |
|---|---|
| 0. Elevación | sección 0 |
| 1. CD-ROM | sección 1 |
| 2. Output drive | sección 2 |
| 3. VM choice | sección 3 |
| 4. Fecha/hostname | sección 4 |
| 5. Validar target | sección 5 |
| 6. KAPE run + hash + NOTES | sección 6 |

Limitaciones del port bash:
- `%~dp0` → `MOCK_CD`
- `set "X=Y"` → `X="Y"`  
- `set /p` → `read -p`
- `for /f` → `grep ... | cut` o `awk`
- `if errorlevel 1` → `if ! command`
- `^>` (escape de redirect) → redirección normal en bash

Cuando un cambio afecte el flujo de control del `.cmd`, portá manualmente
al `.sh` y re-ejecutá `./run_all.sh`.

## ¿Por qué no usar Wine directamente?

Wine requeriría .NET 4.5.2 corriendo sobre Wine, lo cual es problemático:
- macOS no trae Wine por defecto; instalar CrossOver o Whisky añade
  ~500 MB de overhead.
- Wine implementa parcialmente .NET 4.x; KAPE podría fallar de formas
  inesperadas que no reflejan comportamiento real.
- El harness bash cubre el 95% de la lógica del script (detección de
  paths, branching, generación de artefactos) sin esas complicaciones.

Recomendación: usar este harness para iteración rápida, y el `.cmd`
real sobre la VM afectada (o un Win10 limpio en Proxmox) para la
validación final antes de subir al datastore ESXi de producción.
