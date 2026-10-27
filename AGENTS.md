# AGENTS.md — CyboltDFIR Triage

Guía para agentes que trabajan en este repo. Leer **antes** de tocar código.

---

## 1. Qué es (y qué no es) este repo

**CyboltDFIR Triage** es un wizard de adquisición forense para Windows:
KAPE 1.3.0.2 + captura de RAM opcional, empaquetado como **ZIP** que el
operador descomprime en un medio writable (USB o disco) y ejecuta en el
equipo objetivo.

Este repo es **self-contained**: produce un único bundle, `CyboltDFIR-Triage-v<VER>.zip`.

| Repositorio | Contenido | Estado |
|---|---|---|
| `Beacon-Lab-IR/cybolt-dfir-triage` | **este** — wizard + bundle ZIP | activo |
| `Beacon-Lab-IR/cybolt-dfir-uploader` | GUI de subida (S3/FTP/SFTP/SMB) | repo hermano, separado |
| `Beacon-Lab-IR/cybolt-dfir-iso` | ISO no-booteable que combina Triage + Uploader | repo hermano, separado |

> **No reintroducás** `DfirUploaderUi/` ni `iso/` acá. Ambos se movieron a los
> repos hermanos el 2026-10-14 (commit `8b220af`). Volver a meterlos duplica
> código y deja el repo mintiendo sobre qué produce.

---

## 2. Mapa del repo

```
.github/workflows/release.yml   Pipeline: tag v* -> build bundle -> GitHub Release
.gitignore
DfirTriageUi/                   Wizard WinForms .NET Framework 4.5.2 (x64)
├── DfirTriageUi.csproj         net452, WinForms, AssemblyName=CYBOLT_DFIR_TRIAGE
├── app.manifest                requireAdministrator + supportedOS Win7/8/10/11
├── AppInfo.cs                  AssemblyVersion / FileVersion
├── Program.cs                  Entry point (STAThread) + MessageBox en error fatal
├── HashUtil.cs                 SHA-256 streaming (buffer 1 MiB) + FormatBytes
├── KapeRunner.cs               Orquestacion KAPE
├── RamRunner.cs                Orquestacion winpmem
└── DfirTriageMain.cs           Form, 5 paneles, navegacion, parametros
docs/
├── OPERATIVO.md                Procedimiento operativo
└── img/                        Mockups del wizard para el README
LICENSE                         MIT, "Copyright (c) 2026 Cybolt"
README.md                       Quickstart y uso
VERSION.txt                     Changelog del bundle (fuente del build)
build.sh                        Build local -> dist/CyboltDFIR-Triage-v<VER>.zip
targets-full.txt                Lista de KAPE targets (filtrado defensivo)
```

**No trackeados (gitignored):** `staging/` (scratch de build), `dist/`
(bundle final), `kape.zip` (fuente de `Targets/` y `Modules/`), `*.iso`.
No dejar en `dist/` artefactos del monorepo viejo (`CyboltDFIR-ISO-*.iso`,
`CyboltDFIR-Uploader-*.zip`) — viven en los repos hermanos.

---

## 3. Versionado — una sola versión

Tag git = nombre del ZIP = `AssemblyVersion` = título/footer de la UI.

| Dónde | Ejemplo |
|---|---|
| tag / GitHub Release | `v0.3.2` |
| asset | `CyboltDFIR-Triage-v0.3.2.zip` |
| `DfirTriageUi/AppInfo.cs` | `AssemblyVersion("0.3.2.0")` |
| UI (`AppVersion.Current`) | `0.3.2` |
| header de `VERSION.txt` | `CYBOLT-DFIR-TRIAGE-v0.3.2.zip - Version 0.3.2` |

**Regla:** un cambio publicable → bumpear `0.(minor+1).0` (feature) o
`0.x.(patch+1)` (fix). Actualizá **AppInfo.cs** y el **header de VERSION.txt**
juntos. El changelog histórico puede seguir mencionando `v5.x` (era monorepo).

---

## 4. Build

```bash
./build.sh              # usa la version del header de VERSION.txt
./build.sh 0.3.2        # fuerza la version
# -> dist/CyboltDFIR-Triage-v<VER>.zip
```

Requisitos: **.NET SDK 8** (busca `dotnet` en el PATH, fallback `~/.dotnet/dotnet`),
`kape.zip` en la raíz (SHA-256 verificado por el script), `unzip`, `zip`.

> **Fuente de `kape.zip`:** mirror propio en
> `https://github.com/Beacon-Lab-IR/armeria/raw/refs/heads/main/KAPE/kape.zip`.
> El release oficial de `EricZimmerman/KAPE` **no expone el asset** (404) —
> el repo no es público. No volver a poner la URL de EricZimmerman.

`build.sh` es **re-ejecutable**: hace `rm -rf` del destino antes de cada `mv`
del árbol de KAPE, y borra los artefactos upstream que trae el zip oficial.

### Qué hace

1. Compila `DfirTriageUi` → `staging/CYBOLT_DFIR_TRIAGE.exe`
2. Descomprime `kape.zip` a `staging/` (deja `kape.exe`, `Targets/`,
   `Modules/`, `Documentation/` en la raíz — el wizard lee `bundleRoot\Targets`)
 y borra `*_ConsoleLog.txt` / `*kape.cli` (basura del zip de Kroll)
3. Verifica `winpmem_mini_x64_rc2.exe` por SHA-256
4. Copia `VERSION.txt`, `LICENSE`, `README.md`, `targets-full.txt`
5. `zip -qr` → `dist/CyboltDFIR-Triage-v<VER>.zip`

---

## 5. Release

```bash
git tag v0.3.2 && git push origin v0.3.2
```

Dispara `.github/workflows/release.yml` (ubuntu-latest, .NET 8), que reconstruye
el bundle **en Linux** y publica el GitHub Release con el SHA-256 del asset.

> El asset publicado por CI **no** es byte-idéntico al build local (distinto
> SDK → distinto MVID). El SHA-256 que aparece en las notas del release es el
> del asset de CI, y es el que hay que verificar.

Crear el release a mano con `gh release create` **no** dispara el workflow
(el tag lo crea la API, no un push) — el asset sería el build local.

---

## 6. Arquitectura del wizard

`DfirTriageMain.cs` — Form de 5 paneles, un `Panel` visible a la vez.

| Paso | Panel | Contenido |
|---|---|---|
| 1 | Welcome | checkboxes: **capturar RAM** (opt-in, default OFF), **cmd wrapper** (default OFF) |
| 2 | Detect | **disco de entrada** (ComboBox), **carpeta de salida** (TextBox + Browse), bundle (`kape.exe` junto al .exe), RAM, espacio libre |
| 3 | Kape | botón ▶ Ejecutar → `KapeRunner.RunAsync` |
| 4 | RAM | botón ▶ Ejecutar → `RamRunner.RunAsync` (se salta si el checkbox está OFF) |
| 5 | Summary | paths + SHA-256 de cada output + próximos pasos |

### Flujo de datos de los parámetros

```
Detección (paso 2)
  ├─ _bundleRoot   : carpeta del .exe (kape.exe al lado) → fallback raiz de disco
  ├─ _sourceDrive  : "C:"        → ComboBox, default C:
  └─ _outputFolder : ruta completa → autodetectada, siempre editable
        ↓
KapeRunner.RunAsync(bundleRoot, outputFolder, sourceDrive, ct)
  ├─ outBase   = outputFolder\<HOST>-<timestamp>
  ├─ stageDir  = outputFolder\_kape_stage
  ├─ --tdest   = outBase          (KAPE escribe el ZIP DENTRO)
  └─ --zip     = outBase\<HOST>-KAPE-<timestamp>   (basename, KAPE agrega .zip)

RamRunner.RunAsync(bundleRoot, outputFolder, ct)
  └─ rawPath = outputFolder\RAM-<HOST>-<timestamp>.raw
```

Default de la carpeta de salida (`DetectDefaultOutputFolder`):
1. `<primer disco removible>\DFIR-OUTPUT`
2. `C:\DFIR-OUTPUT`

No busca volumes por label: cualquier carpeta writable sirve (Browse / TextBox).

"Re-detectar" **no** pisa la carpeta que el operador eligió a mano.

---

## 7. Invariantes — no romper esto

1. **Nunca commitear** `dist/`, `staging/`, `kape.zip`, `*.iso`. Van al
   GitHub Release.
2. **`DfirUploaderUi/` y `iso/` no vuelven.** Viven en repos hermanos.
3. **`--vss` va siempre.** Sin él se pierden los event logs bloqueados
   (Application.evtx, Defender, Security.evtx locked) y el estado histórico.
4. **Sin `--sync`.** Bug de KAPE 1.3.0.2: en proceso (no CMD) interpreta
   `$Null` como filename literal y falla.
5. **Sin `--moduledir` / `--mdest`.** Apuntaban a un medio read-only.
6. **Todas las rutas en los args van entrecomilladas** y **sin barra final**
   (ver pitfalls §8.3).
7. **`.NET Framework 4.5.2`, no .NET Core.** Target del operador: Win7 SP1 /
   Server 2008 R2. Cero NuGet (BCL solamente) para que el binario sea chico y
   no tenga dependencias.
8. **El bundle NO puede llevar logs de terceros.** El `kape.zip` oficial
   viene con 7 `ConsoleLog.txt` y un `kape.cli` de la máquina del autor de
   KAPE. `build.sh` los borra. No reintroducirlos.

---

## 8. Pitfalls (bugs reales ya encontrados — no repetirlos)

### 8.1 KAPE escribe el ZIP DENTRO de `--tdest`

```
{--tdest}\{timestamp}_E__{basename}.zip
```

El wizard buscó una vez en `Path.GetDirectoryName(outBase)` (la carpeta
**padre**) → nunca encontraba el ZIP → `KapeException("KAPE no genero ZIP.
Exit code: 0")` con el archivo ahí al lado. **Buscá siempre dentro de
`--tdest`**, con fallback a la carpeta padre.

Referencia: el `KapeUi` legacy (drill validado 2026-09-23) sí buscaba bien.
**Cuando refactorices, compará con el legacy** — el wizard se extrajo de ahí
en v5.3.0 y perdió behaviours.

### 8.2 `--vss` se perdió en el refactor

`KapeUi/Program.cs` lo tenía (`--hv nc,vm --gui --vss`). Al extraer
`KapeRunner` (v5.3.0) quedó `--hv nc,vm --gui`. El README lo documentaba
desde antes pero el código no lo ejecutaba. Restaurado en v5.4.0.

### 8.3 Quoting de rutas

Con carpeta de salida elegible, la ruta puede tener espacios
(`E:\Mi Evidencia`). Reglas:

```csharp
private static string Quote(string v) => v == null ? "\"\"" : "\"" + v.Replace("\"", "\\\"") + "\"";
```

- Siempre entrecomillar rutas en los args.
- **Sin barra final dentro de las comillas**: `"C:\"` se parsea como `C:`
  con la comilla escapada (regla de `CommandLineToArgvW`).
- `--zip` lleva un **basename** (KAPE le agrega `.zip` solo). Pasar
  `--zip "X.zip"` produce `X.zip.zip`.

### 8.4 `Task.CompletedTask` no existe en .NET 4.5.2

Usar `Task.Delay(0)`.

### 8.5 WinForms en .NET 4.5.2

- `TextBox.PlaceholderText` no existe.
- `JavaScriptSerializer` requiere `<Reference Include="System.Web.Extensions" />`.
- `HttpClient` requiere `<Reference Include="System.Net.Http" />`.
- WMI requiere `<Reference Include="System.Management" />`.
- `JavaScriptSerializer.MaxJsonLength` default 4 MB.

### 8.6 KAPE no acepta `--targets-list`

Probado: falla con "target is required". La lista de `targets-full.txt` la
parsea el propio wizard (`KapeRunner.LoadAndFilterTargets`) y se pasa como
`--target "A,B,C"`. KAPE acepta múltiples `--target` separados pero solo
procesa el primero → hay que usar comma-separated.

### 8.7 No se puede correr `kape.exe` desde un medio read-only

KAPE `CheckDefaultDirectories()` escribe `Modules\bin` y `.kape` junto al
`.exe` y falla con `UnauthorizedAccessException (5) Access is denied: [D:]`.
Por eso el wizard **stagea** `kape.exe` + `Targets/` a la carpeta de salida
antes de correr.

---

## 9. Estado de validación

**Verificado:**
- Build limpio (0 warnings, 0 errors) con .NET 8 SDK.
- Lógica de búsqueda del ZIP y de quoting de rutas, con harness.
- Contenido del bundle (657 `.tkape`, 2905 files de `Modules/`, 0 artefactos
  upstream).
- Strings del binario (v5.4.0 features presentes en el .exe publicado).

**NO verificado — requiere Windows:**
- **No hay drill E2E del wizard contra un target real.** El wizard nunca corrió
  contra KAPE 1.3.0.2 en vivo. Los bugs §8.1 y §8.2 se encontraron leyendo
  código, no ejecutando.
- Multipart upload, SigV4 contra credenciales reales (eso era del uploader,
  ahora en el repo hermano).
- `FolderBrowserDialog` y el flujo del ComboBox en Win7 SP1.

→ **Antes de usar en producción, hacer un drill en una VM Windows 10.**
Es el gap que cerró el release v0.2.0 y sigue abierto.

---

## 10. Referencias

- Esquema del config del uploader: `cybolt-dfir-uploader` → `docs/CONFIG-SCHEMA.md`
- Bundle ISO (Triage + Uploader): `cybolt-dfir-iso`
- Procedimiento operativo: `docs/OPERATIVO.md` (este repo)
- KAPE: https://github.com/EricZimmerman/KAPE
- WinPmem: https://github.com/Velocidex/WinPmem

Licencia: MIT, "Copyright (c) 2026 Cybolt".
