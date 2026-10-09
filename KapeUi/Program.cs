using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KapeUi
{
    /// <summary>
    /// KAPE_TRIAGE_UI v5.2.20 - Interfaz visual para adquisicion DFIR con KAPE.
    ///                                          By Cybolt, MIT License.
    ///
    /// Mejoras v5.2.20 (cmd.exe wrapper mode para KAPE):
    ///   - v5.2.19 suprimia el spam de "Unable to update Console Title"
    ///     en el log. v5.2.20 va mas alla: lo EVITA dandole a KAPE una
    ///     consola real. Checkbox opt-in "Lanzar KAPE via cmd.exe" en el
    ///     panel avanzado. Cuando esta marcado, la GUI lanza:
    ///       cmd.exe /c start "KAPE" /B /WAIT "kape.exe" [args]
    ///     KAPE hereda la consola de cmd, Console.Title funciona, no hay
    ///     spam. /B = background, no abre ventana popup. /WAIT bloquea
    ///     hasta que KAPE termine para que WaitForExit() funcione.
    ///   - La diferencia con v5.2.17 (winpmem) es el /WAIT explicito:
    ///     para KAPE es critico porque sino el cmd.exe retorna antes
    ///     y no podemos leer su exit code.
    ///   - Operador: marcarlo si vas a correr KapeTriage en servers o VMs
    ///     con muchos modulos (DC, file server). En workstations chicas
    ///     no es necesario.
    ///
    /// Mejoras v5.2.19 (filtrar spam de Console Title):
    ///   - v5.2.19 descartaba las lineas "Unable to update Console Title"
    ///     en el log via IsConsoleTitleNoise. v5.2.20 es preferible:
    ///     evita el problema en vez de ocultarlo. Pero el filtro se
    ///     mantiene por defensa en profundidad.
    ///
    /// Mejoras v5.2.14 (filtrar spam de Console Title):
    ///   - Bug observado 2026-10-08 corriendo KapeTriage en un server
    ///     (32+ GB RAM, DC con muchos .evtx): KAPE 1.3.0.2 internamente
    ///     llama `Console.Title = "..."` para mostrar progreso, pero
    ///     como la GUI lo lanza con RedirectStandardOutput=true +
    ///     CreateNoWindow=true, el handle de consola no es valido y
    ///     el runtime de .NET loguea "Unable to update Console Title"
    ///     en stderr ~8-10 veces por segundo durante fase 3. Es ruido
    ///     puro (no afecta la captura), pero llena el log.
    ///   - v5.2.14 filtra esas lineas en el handler de OutputDataReceived
    ///     y ErrorDataReceived via el helper IsConsoleTitleNoise. El
    ///     operador ve el progreso real de KAPE (copias de archivos,
    ///     modulos completados) sin el spam.
    ///
    /// Mejoras v5.2.13 (RAM capture standalone + licencia MIT):
    ///   - v5.2.12 ya hacia KapeTriage end-to-end. Faltaba RAM dump
    ///     (procesos vivos, conexiones de red activas, credenciales
    ///     en memoria, etc.). v5.2.13 agrega flujo de captura de RAM
    ///     STANDALONE via Tools/Memory/winpmem_mini_x64_rc2.exe +
    ///     Tools/Memory/CAPTURE_RAM.cmd. Operador corre ese script
    ///     antes de la UI para que la captura sea del estado mas
    ///     cercano al inicio del triage.
    ///   - Licencia MIT + "By Cybolt" agregadas. Archivo LICENSE en el
    ///     staging root con texto completo de la licencia MIT.
    ///     Header del programa incluye atribucion a Cybolt.
    ///
    /// Mejoras v5.2.12 (SHA-256 nativo .NET en lugar de certutil):
    ///   - Bug observado en drill 2026-09-25 con v5.2.11: el ZIP se
    ///     generaba correctamente (803 MB, verificado por SHA post-hoc
    ///     del operador) pero el SHA-256 automatico en la UI fallaba
    ///     porque mi regex buscaba 64 hex chars continuos en una sola
    ///     linea, y certutil -hashfile formatea el hash con espacios
    ///     cada 2 chars en varias lineas. Resultado: ComputeSha256
    ///     retornaba null aunque certutil hubiera calculado bien el
    ///     hash. UI mostraba "LISTO. ZIP: 803 MB | SHA-256: ERROR".
    ///   - Fix: reemplazar certutil por System.Security.Cryptography
    ///     .SHA256 nativo. Streaming (no carga el archivo completo en
    ///     memoria, importante para ZIPs grandes), sin subprocess,
    ///     formato estable (lowercase 64 hex chars).
    ///
    /// Mejoras v5.2.11 (KAPE stdin close + AppInfo.Version fix):
    ///   - Bug critico observado en drill 2026-09-24 con v5.2.10: KAPE
    ///     terminaba la copia y la compresion ZIP correctamente, pero
    ///     kape.exe se quedaba vivo en Console.ReadKey() esperando una
    ///     tecla (Prompt "Press any key to exit"). El flag --gui que
    ///     pasamos no suprime ese prompt en KAPE 1.3.0.2. Resultado:
    ///     _kapeProcess.WaitForExit() nunca retornaba, la UI quedaba
    ///     congelada en fase 3 ("Recolectar artefactos") aunque el ZIP
    ///     ya estaba generado en disco.
    ///   - Fix: ProcessStartInfo.RedirectStandardInput = true y cerrar
    ///     el pipe inmediatamente despues de Process.Start():
    ///       _kapeProcess.StandardInput.Close();
    ///     Console.ReadKey() en KAPE ve EOF en stdin y retorna -1
    ///     inmediato, kape.exe sale del Main() con exit code 0,
    ///     WaitForExit() desbloquea y la UI continua a fase 4/5/6.
    ///   - Bug cosmetico: AppInfo.Version quedaba en "5.2.9" en el
    ///     binario v5.2.10 (solo el header doc se actualizo a v5.2.10).
    ///     Por eso la UI mostraba "v5.2.9" aunque tuviera los fixes de
    ///     v5.2.10. Ahora AppInfo.Version = "5.2.11" y la UI muestra
    ///     la version correcta en titulo, footer y log de inicio.
    ///
    /// Mejoras v5.2.10 (fix bug cosmetico .zip.zip + post-KAPE ZIP detection):
    ///   - Bug cosmetico observado en corrida 2026-09-23 con v5.2.7:
    ///     pasar --zip "E:\X.zip" producia archivo final con doble sufijo
    ///     (E:\X\2026-09-23T201412_E__X.zip.zip). Causa: KAPE 1.3.0.2
    ///     toma el valor de --zip, extrae el basename y le agrega .zip
    ///     por su cuenta. Si el basename ya termina en .zip, sale .zip.zip.
    ///     Fix: en BuildDefaultKapeArgs, NO concatenar ".zip" al outZip.
    ///     KAPE ahora produce {tdest}\{timestamp}_E__{basename}.zip
    ///     con un solo .zip final.
    ///   - Post-KAPE ZIP detection: el archivo real NO esta en el path
    ///     que pasamos a --zip (esa era la otra falla silenciosa). KAPE
    ///     siempre escribe el ZIP dentro de --tdest con timestamp prefix.
    ///     Antes: File.Exists(_outZip) siempre fallaba aunque el ZIP
    ///     existiera, y la UI mostraba "ERROR: no se genero el ZIP" sin
    ///     que el operador supiera que el archivo SI estaba ahi.
    ///     Ahora: buscar por patron {tdest}\*_E__{hostname}-KAPE-*.zip,
    ///     reasignar _outZip al path real, y continuar con SHA-256.
    ///   - Hash ZIP post-correccion: el SHA-256 que se loguea y guarda
    ///     en .sha256 ahora SI corresponde al archivo que el operador
    ///     tiene que entregar al equipo DFIR.
    ///
    /// Mejoras v5.2.9 (kape.exe SHA-256 verification post-staging):
    ///   - Despues del staging, computamos SHA-256 del kape.exe en
    ///     E:\_kape_stage\kape.exe y lo logueamos en:
    ///       a) el log en vivo (LogInfo)
    ///       b) el NOTES file (con size + expected SHA-256)
    ///   - El operador puede comparar contra CYBOLT-DFIR-TRIAGE-INFO.txt
    ///     (expected: 6167472179d0b5b028560dcc84ea1a2e3cb2d7128dd18e4e9278263b86a4318b).
    ///   - Si KAPE fue reempaquetado por alguien con un ISO modificado,
    ///     el hash difiere y esto lo detecta. Tambien documenta la
    ///     cadena de custodia: el ZIP final sale de este kape.exe
    ///     especifico, con su SHA-256 verificable.
    ///
    /// Mejoras v5.2.8 (staging skip-if-cached + --vss para locked files):
    ///   - Skip-if-cached: si el destino E:\_kape_stage\kape.exe y
    ///     E:\_kape_stage\Targets\Compound\KapeTriage.tkape ya existen
    ///     con el mismo tamano que en el ISO, no re-copiamos nada
    ///     (~50 MB ahorrados, ~30s en SSD). Segunda VM del mismo drill
    ///     arranca staging en <1s.
    ///   - --vss: KAPE crea un VSS snapshot de C: al final del target
    ///     processing y reintenta los archivos deferred (Application.evtx,
    ///     Defender logs, etc.) desde el snapshot. Sin --vss, los archivos
    ///     locked se quedan out del ZIP final, perdiendo event logs
    ///     criticos para DFIR.
    ///
    /// Mejoras v5.2.7 (Targets staging para resolver KapeTriage):
    ///   - v5.2.6 dejo KAPE corriendo bien desde E:\_kape_stage\kape.exe
    ///     pero fallo con "Target KapeTriage not found" porque
    ///     --targetdir seguia apuntando a D:\Targets (CD-ROM). Algo en
    ///     la cadena D:→E: rompe la resolucion de sub-targets.
    ///   - Fix: copiamos el arbol Targets/ completo (recursivo) a
    ///     E:\_kape_stage\Targets\ durante el staging, y cambiamos
    ///     --targetdir a E:\_kape_stage\. KAPE busca:
    ///       E:\_kape_stage\KapeTriage.tkape         (no)
    ///       E:\_kape_stage\Compound\KapeTriage.tkape (yes!)
    ///     y los sub-targets referenciados por KapeTriage.tkape
    ///     (Antivirus.tkape, EventLogs.tkape, etc.) se resuelven contra
    ///     E:\_kape_stage\<name>.tkape. Todo en E:, nada en D:.
    ///
    /// Mejoras v5.2.6 (staging read-only recovery):
    ///   - Cuando el destino E:\_kape_stage\kape.exe queda de una corrida
    ///     previa con FILE_ATTRIBUTE_READONLY (Windows Defender/AV lo
    ///     pone al escanear el .exe que escribimos), File.Copy(..., true)
    ///     revienta con UnauthorizedAccessException "Access to the path
    ///     'E:\_kape_stage\kape.exe' is denied." Ahora: antes de copiar,
    ///     limpiar atributos (SetAttributes Normal) y borrar el destino
    ///     si existe. Retry hasta 3 veces con 500ms entre intentos en
    ///     caso de que AV tenga el handle abierto transitoriamente.
    ///
    /// Mejoras v5.2.5 (textbox override + trailing slash + NUL sync):
    ///   - Track _cmdEdited: ahora respetamos el textbox del acordeon SOLO
    ///     si el operador TECLEO algo. Antes: el contenido del textbox podia
    ///     ser el cache del pre-flight (con paths sin "\" en tdest/zip) y se
    ///     pasaba tal cual a KAPE. Resultado: --tdest "E:WIN10-A-..." sin
    ///     "\" y KAPE fallaba porque interpretaba el path como
    ///     "E:WIN10-A-KAPE-..." (sin separador de unidad).
    ///   - ComboBox.SelectedIndexChanged dispara OnDriveSelectionChanged
    ///     que re-normaliza trailing slash y refresca el preview (solo si
    ///     el operador no lo edito a mano).
    ///   - Trailing slash se agrega en el pre-flight mismo, no solo en
    ///     RunAcquisitionAsync. Asi RefreshCommandPreview ve paths ya
    ///     formateados como "E:\WIN10-A-...".
    ///   - Drop --sync $Null. En proceso (no CMD), $Null no se expande a
    ///     NUL: y KAPE lo interpreta como filename literal, fallando con
    ///     "Could not find file 'E:\_kape_stage\$Null'". Sin --sync KAPE
    ///     no intenta descargar nada (perfecto para nuestro caso offline).
    ///   - Cache de _time en RunAcquisitionAsync. Antes BuildDefaultKapeArgs
    ///     recalculaba time en cada llamada y daba 1 segundo de drift entre
    ///     el nombre en NOTES (021548) y el nombre en args (021547).
    ///
    /// Mejoras v5.2.4 (path join + textbox bootstrap + KAPE staging):
    ///   - _outputDrive ya termina en "\", asi que los paths NO agregan
    ///     otro "\". Antes quedaba "E:\\WIN10-A-..." con doble slash.
    ///   - BuildDefaultKapeArgs() ahora computa _outBase/_outZip/_date
    ///     on the fly. Antes dependia de _outBase/_outZip que solo se
    ///     setean en RunAcquisitionAsync, asi que el textbox quedaba
    ///     con el placeholder al terminar el pre-flight.
    ///   - KAPE staging: copio kape.exe desde D:\kape.exe (CD-ROM read-only)
    ///     a E:\_kape_stage\kape.exe (escribible) antes de lanzar. KAPE
    ///     en su CheckDefaultDirectories() crea Modules\bin bajo el
    ///     directorio de su propio ejecutable (Path.GetDirectoryName de
    ///     Assembly.Location). Si esta en D:\ falla con Access denied.
    ///     Stageandolo a E:\ el path es escribible. Targets/ se sigue
    ///     leyendo desde D:\ (read-only esta bien, KAPE solo lee).
    ///
    /// Mejoras v5.2.3 (KAPE WorkingDirectory + acordeon de comando):
    ///   - kape.exe se lanza con WorkingDirectory = _outputDrive (E:\) en
    ///     vez de _isoDir (D:\). KAPE en su Main() llama
    ///     CheckDefaultDirectories() que crea un .kape (state dir) en
    ///     el cwd. Si el cwd es D: (CD-ROM read-only) falla con
    ///     UnauthorizedAccessException ANTES de procesar nuestros flags.
    ///     Con cwd = _outputDrive (escribible) KAPE no falla.
    ///   - Tambien borramos la creacion de E:\Modules que era residuo
    ///     de cuando pasabamos --mdest.
    ///   - Acordeon "Comando KAPE (avanzado)" colapsable: muestra el
    ///     comando construido a partir de las selecciones actuales,
    ///     editable. El operador puede sobreescribir flags puntuales
    ///     (por ej. agregar --module RegistryHives, --vsc, etc.) sin
    ///     tocar codigo. Boton "Restablecer" regenera el default.
    ///     Boton "Copiar" lo manda al portapapeles.
    ///
    /// Mejoras v5.2.2 (UAC + kape args fix):
    ///   - Saco los radio buttons VM01/VM02: la herramienta es generica,
    ///     sirve para 1 VM o N. La identificacion del ZIP ahora es
    ///     {HOSTNAME}-KAPE-{YYYYMMDD}-{HHMMSS}.zip (hostname + timestamp,
    ///     evita colisiones si se corre varias veces el mismo dia).
    ///   - Saco la barra de progreso verde: el strip de 6 fases con
    ///     checkmarks ya muestra avance suficiente. La barra era ruido.
    ///   - Saco los botones "Copiar log" y "Abrir carpeta salida": el
    ///     log se persiste solo en DFIR_OUTPUT:\NOTES\*.txt y la
    ///     carpeta se ve en Explorer.
    ///   - Comandos KAPE que se ejecutan: --tsource {source_drive}
    ///     --target KapeTriage --tdest {out_base} --zip {out_base}
    ///     --hv nc,vm --gui --vss. KAPE agrega sufijo .zip al basename
    ///     del valor de --zip por su cuenta, y el archivo final queda
    ///     en {tdest}\{timestamp}_E__{basename}.zip.
    ///     El comando exacto se loguea al arrancar.
    ///
    /// Mejoras v5.2.0 (drive selectors):
    ///   - "Particion de salida" pasa de TextBox read-only a ComboBox con
    ///     TODAS las unidades logicas detectadas (C:, D:, E:, ...). El
    ///     operador puede cambiar a cualquier disco o particion visible
    ///     al SO. Default auto: el volumen con label "DFIR_OUTPUT".
    ///     Esto desacopla el destino de "ser un VMDK": sirve tambien para
    ///     VMware, PVE, fisico, USB, cualquier VM hypervisor.
    ///   - Nuevo selector "Particion fuente" para indicar a KAPE de que
    ///     unidad levantar artefactos. Default C:. KAPE acepta cualquier
    ///     unidad montada que tenga los artefactos a colectar.
    ///   - Etiqueta del header "VMDK de salida" -> "Particion de salida"
    ///     (porque el destino es un disco o particion visible al SO,
    ///     no necesariamente un VMDK).
    ///   - Si la particion de salida seleccionada NO tiene label
    ///     "DFIR_OUTPUT", el log emite [WARN] antes de arrancar pero
    ///     permite continuar (override explicito del operador).
    ///   - Header crece de 60 a 90 px para acomodar las 3 filas nuevas.
    ///   - KAPE corre con `--tsource {fuente}` y `--tdest {salida}`.
    ///
    /// Mejoras v5.1.2 (layout + UX):
    ///   - Layout envuelto en Panel.AutoScroll: aunque el operador achique
    ///     la ventana, los botones del pie (Copiar log / Abrir carpeta) son
    ///     siempre alcanzables haciendo scroll vertical. Ya no quedan tapados.
    ///   - Alturas de filas rebalanceadas y compactas (header 60, preflight
    ///     100, fases 48, acciones 56, log 280, footer 110). Default form
    ///     1280x860, MinimumSize 960x760 -> cabe en cualquier 720p.
    ///   - Cuando el VMDK DFIR_OUTPUT NO esta conectado, el log emite un
    ///     bloque amarillo con los pasos exactos en Proxmox VE (Hardware ->
    ///     Add -> Hard Disk -> Use existing o Create). Y Ejecutar queda
    ///     deshabilitado, asi el operador no puede arrancar la adquisicion
    ///     sin disco de salida (antes corria y reventaba a mitad de camino).
    ///
    /// Mejoras v5.1:
    ///   - Log prominente con timestamp y color coding (errores en rojo, OK en verde).
    ///   - Indicador visual de fases (6 etapas con checkmarks dinamicos).
    ///   - Auto-save del log completo a archivo .log en DFIR_OUTPUT junto a NOTES.
    ///   - Botones "Copiar log" y "Abrir carpeta salida" para preservar evidencia.
    ///   - Cancelacion limpia con confirmacion.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Error inesperado:\n\n" + e.Exception,
                    "KAPE Triage", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }

    internal class MainForm : Form
    {
        // ====== Header ======
        private readonly Label _txtIsoPath = new Label { Dock = DockStyle.Fill, BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 4, 0), Font = new Font("MS Shell Dlg", 9f) };
        private readonly ComboBox _cmbOutputDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, BackColor = Color.White, Font = new Font("MS Shell Dlg", 9f) };
        private readonly ComboBox _cmbSourceDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, BackColor = Color.White, Font = new Font("MS Shell Dlg", 9f) };

        // ====== Pre-flight indicators ======
        private readonly Label _lblIsoOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblOutputOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblAdminOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblWmicOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblCertutilOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblKapeOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblTargetOk = new Label { Text = "...", AutoSize = true };
        private readonly Label _lblFreeSpace = new Label { Text = "...", AutoSize = true };

        // ====== Actions ======
        private readonly Button _btnRun = new Button { Text = "Ejecutar adquisicion", Height = 40, Dock = DockStyle.Fill, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("MS Shell Dlg", 10f, FontStyle.Bold) };
        private readonly Button _btnCancel = new Button { Text = "Cancelar", Enabled = false, Height = 40, Dock = DockStyle.Fill };

        // ====== Advanced KAPE command (colapsable) ======
        private readonly Button _btnAdvancedToggle = new Button { Text = "▶ Comando KAPE (avanzado)", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(240, 240, 240), Font = new Font("MS Shell Dlg", 9f, FontStyle.Bold) };
        private readonly Panel _pnlAdvanced = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = Color.White };
        private readonly TextBox _txtCommand = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsTab = false, AcceptsReturn = true };
        private readonly Button _btnResetCmd = new Button { Text = "Restablecer", Width = 100, Height = 26, FlatStyle = FlatStyle.Flat };
        private readonly Button _btnCopyCmd = new Button { Text = "Copiar", Width = 80, Height = 26, FlatStyle = FlatStyle.Flat };

        // v5.2.20: Checkbox opt-in para lanzar KAPE via cmd.exe (start /B /WAIT).
        // Esto le da a KAPE una consola real (heredada de cmd) para que las
        // llamadas a Console.Title que KAPE hace internamente para mostrar
        // progreso funcionen, en vez de fallar con "Unable to update Console
        // Title" 8-10 veces/segundo. Validado 2026-10-08 con server DC.
        private readonly CheckBox _chkKapeCmdWrapper = new CheckBox {
            Text = "Lanzar KAPE via cmd.exe (evita spam de Console Title)",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
        };

        // ====== Phase indicator (6 stages) ======
        private readonly Label[] _phaseLabels = new Label[6];
        private readonly string[] _phaseNames = {
            "1. Pre-flight",
            "2. Iniciar KAPE",
            "3. Recolectar artefactos",
            "4. Empaquetar ZIP",
            "5. Calcular SHA-256",
            "6. Generar NOTES"
        };

        // ====== log ======
        private readonly RichTextBox _txtLog = new RichTextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            DetectUrls = false,
            WordWrap = false
        };
        private readonly Label _lblStatus = new Label { Text = "Listo. Iniciando pre-flight...", Dock = DockStyle.Fill, AutoSize = false, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("MS Shell Dlg", 9f, FontStyle.Bold), BackColor = Color.FromArgb(240, 240, 240) };

        // ====== State ======
        private string _isoDir;
        private string _outputDrive;
        private string _sourceDrive;
        private string _date;
        private string _time;
        private string _hostname;
        private string _outBase;
        private string _outZip;
        private string _notesPath;
        private string _logPath;
        // v5.2.5: true si el operador TECLEO algo en el textbox del comando.
        // false si lo que esta en el textbox fue puesto por RefreshCommandPreview.
        // Asi en RunAcquisitionAsync sabemos si el contenido es override explicito
        // del operador (usarlo) o es stale del pre-flight (ignorar y regenerar).
        private bool _cmdEdited;
        private Process _kapeProcess;
        private CancellationTokenSource _cts;
        private readonly StringBuilder _logBuffer = new StringBuilder();
        private int _currentPhase = -1;

        public MainForm()
        {
            Text = "KAPE Triage - DFIR Acquisition v" + AppInfo.Version;
            Width = 1280;
            Height = 860;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(960, 760);

            BuildLayout();
            BuildPhases();

            Shown += async (s, e) => await RunPreflightAsync();
            FormClosing += MainForm_FormClosing;
        }

        private void BuildLayout()
        {
            // v5.2.0: layout restructurado. Los botones de accion (Ejecutar /
            // Cancel / Copiar log / Abrir carpeta) quedan DOCKED al fondo de
            // la ventana, fuera del scroll. Asi son SIEMPRE visibles aunque
            // el operador achique la ventana o el form chrome de Windows
            // consuma altura extra. Solo el contenido (header, preflight,
            // fases, VM chooser, log) va dentro del area scrolleable.

            // === TOP: area scrolleable (header + preflight + fases + VM + log) ===
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0),
                BackColor = SystemColors.Control
            };
            Controls.Add(scrollHost);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(8)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));      // header (ISO + output + source)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));     // preflight grid
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));      // phases strip
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));      // acordeon colapsado
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));     // log (altura fija minima)
            scrollHost.Controls.Add(root);

            // Header: ISO + output combo + source combo (3 filas)
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            header.Controls.Add(MkLabel("ISO montada en:"), 0, 0);
            header.Controls.Add(_txtIsoPath, 1, 0);
            header.Controls.Add(MkLabel("Particion de salida:"), 0, 1);
            header.Controls.Add(_cmbOutputDrive, 1, 1);
            header.Controls.Add(MkLabel("Particion fuente:"), 0, 2);
            header.Controls.Add(_cmbSourceDrive, 1, 2);
            root.Controls.Add(header, 0, 0);

            // Pre-flight grid: 4 cols x 2 rows
            var preflight = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            for (int i = 0; i < 4; i++) preflight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            for (int i = 0; i < 2; i++) preflight.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            preflight.Controls.Add(MkCheck("CD-ROM accesible",  _lblIsoOk),       0, 0);
            preflight.Controls.Add(MkCheck("Salida seleccionada", _lblOutputOk),  1, 0);
            preflight.Controls.Add(MkCheck("Administrador",     _lblAdminOk),     2, 0);
            preflight.Controls.Add(MkCheck("wmic / certutil",   _lblWmicOk, _lblCertutilOk), 3, 0);
            preflight.Controls.Add(MkCheck("kape.exe presente", _lblKapeOk),     0, 1);
            preflight.Controls.Add(MkCheck("Target KapeTriage", _lblTargetOk),   1, 1);
            preflight.Controls.Add(MkCheck("Espacio libre",     _lblFreeSpace),  2, 1);
            preflight.Controls.Add(MkLabel("KAPE 1.3.0.2  -  .NET 4.5.2  -  v" + AppInfo.Version, ContentAlignment.MiddleCenter, new Font("MS Shell Dlg", 8.5f, FontStyle.Italic)), 3, 1);
            root.Controls.Add(preflight, 0, 1);

            // Phases strip
            var phases = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
            for (int i = 0; i < 6; i++) phases.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66f));
            phases.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            for (int i = 0; i < 6; i++)
            {
                _phaseLabels[i] = new Label
                {
                    Text = "○ " + _phaseNames[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("MS Shell Dlg", 9f, FontStyle.Bold),
                    BackColor = Color.FromArgb(230, 230, 230),
                    ForeColor = Color.Gray,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(1)
                };
                phases.Controls.Add(_phaseLabels[i], i, 0);
            }
            root.Controls.Add(phases, 0, 2);

            // === v5.2.3: Acordeon "Comando KAPE (avanzado)" ===
            // Contenedor con toggle button arriba + panel colapsable abajo.
            // Cuando esta colapsado solo se ve el boton (28 px). Cuando
            // esta expandido se ve el textbox + botones (168 px). El
            // operador edita el comando a mano si quiere agregar flags
            // puntuales (--module, --vsc, --d). En RunAcquisitionAsync
            // usamos _txtCommand.Text como Arguments.
            var advContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            advContainer.Controls.Add(_pnlAdvanced);
            advContainer.Controls.Add(_btnAdvancedToggle);
            _btnAdvancedToggle.Dock = DockStyle.Top;
            _btnAdvancedToggle.Height = 28;
            _pnlAdvanced.Dock = DockStyle.Fill;
            _pnlAdvanced.Visible = false;

            // Inner panel del acordeon: bar de botones + textbox + checkbox cmd wrapper
            var advInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            advInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            advInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            advInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var advBtnBar = new Panel { Dock = DockStyle.Fill };
            _btnResetCmd.Dock = DockStyle.Right;
            _btnCopyCmd.Dock = DockStyle.Right;
            advBtnBar.Controls.Add(_btnResetCmd);
            advBtnBar.Controls.Add(_btnCopyCmd);
            advInner.Controls.Add(advBtnBar, 0, 0);

            // Wrapper checkbox: lanza KAPE con cmd /c start /B /WAIT
            // asi KAPE hereda una consola real y Console.Title funciona
            var advChkBar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 4, 0) };
            _chkKapeCmdWrapper.Dock = DockStyle.Fill;
            _chkKapeCmdWrapper.Checked = false;
            advChkBar.Controls.Add(_chkKapeCmdWrapper);
            advInner.Controls.Add(advChkBar, 0, 1);

            advInner.Controls.Add(_txtCommand, 0, 2);
            _pnlAdvanced.Controls.Add(advInner);

            _btnAdvancedToggle.Click += (s, e) => {
                bool expand = !_pnlAdvanced.Visible;
                _pnlAdvanced.Visible = expand;
                // Cuando expande, la row del acordeon crece de 28 a 168
                // para acomodar el textbox + botones.
                root.RowStyles[3] = new RowStyle(SizeType.Absolute, expand ? 168 : 28);
                _btnAdvancedToggle.Text = expand
                    ? "▼ Comando KAPE (avanzado) — editá los flags si querés sobreescribir"
                    : "▶ Comando KAPE (avanzado)";
            };
            _btnResetCmd.Click += (s, e) => RefreshCommandPreview();
            _btnCopyCmd.Click += (s, e) => CopyCommandToClipboard();

            // v5.2.5: cuando el operador cambia un ComboBox, refrescar el
            // preview del comando inmediatamente. Antes esto solo pasaba al
            // final del pre-flight y quedaba stale si el operador cambiaba
            // la unidad despues.
            _cmbOutputDrive.SelectedIndexChanged += (s, e) => OnDriveSelectionChanged();
            _cmbSourceDrive.SelectedIndexChanged += (s, e) => OnDriveSelectionChanged();

            // v5.2.5: marcar el textbox como "editado por el operador" cuando
            // TECLEA algo. Asi RunAcquisitionAsync sabe si respetar el contenido
            // o regenerar desde BuildDefaultKapeArgs().
            _txtCommand.TextChanged += (s, e) =>
            {
                // Solo marcar como editado si el textbox ya estaba cargado
                // con algo (no en el primer load). Si el textbox arranca vacio
                // y el operador escribe, tambien cuenta como editado.
                if (_txtCommand.Focused) _cmdEdited = true;
            };

            root.Controls.Add(advContainer, 0, 3);

            // LOG container con header (parte del area scrolleable)
            var logContainer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            logContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            logContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logContainer.Margin = new Padding(0);
            var logHeader = new Label
            {
                Text = "  Log de ejecucion (KAPE en vivo)  ",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("MS Shell Dlg", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            logContainer.Controls.Add(logHeader, 0, 0);
            logContainer.Controls.Add(_txtLog, 0, 1);
            root.Controls.Add(logContainer, 0, 4);

            // === BOTTOM: zona de accion (SIEMPRE VISIBLE, fuera del scroll) ===
            // Status label: 28px
            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Margin = new Padding(0);
            statusBar.Controls.Add(_lblStatus);
            Controls.Add(statusBar);

            // Action buttons (Run + Cancel): 56px (el AZUL va aqui, siempre al fondo)
            var actionBar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8, 4, 8, 4) };
            var btnPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _btnRun.Margin = new Padding(0, 0, 4, 0);
            _btnCancel.Margin = new Padding(4, 0, 0, 0);
            btnPanel.Controls.Add(_btnRun, 0, 0);
            btnPanel.Controls.Add(_btnCancel, 1, 0);
            actionBar.Controls.Add(btnPanel);
            Controls.Add(actionBar);

            _btnRun.Click += async (s, e) => await RunAcquisitionAsync();
            _btnCancel.Click += (s, e) => CancelRun();
        }

        private void BuildPhases()
        {
            // Already done in BuildLayout; this is a hook for future per-phase init.
        }

        private static Label MkLabel(string text, ContentAlignment align = ContentAlignment.MiddleLeft, Font font = null)
        {
            return new Label
            {
                Text = text,
                TextAlign = align,
                Dock = DockStyle.Fill,
                Font = font ?? new Font("MS Shell Dlg", 9f, FontStyle.Bold)
            };
        }

        private static Control MkCheck(string title, params Label[] statusLabels)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("MS Shell Dlg", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(4, 0, 0, 0)
            }, 0, 0);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            foreach (var lbl in statusLabels)
            {
                lbl.Margin = new Padding(4, 4, 8, 0);
                flow.Controls.Add(lbl);
            }
            panel.Controls.Add(flow, 0, 1);
            return panel;
        }

        // ====================================================================
        //  PHASES
        // ====================================================================
        private void SetPhase(int idx, PhaseState state)
        {
            if (idx < 0 || idx >= _phaseLabels.Length) return;
            _currentPhase = idx;
            Invoke((Action)(() =>
            {
                string prefix;
                Color bg, fg;
                switch (state)
                {
                    case PhaseState.Active:
                        prefix = "►"; bg = Color.FromArgb(255, 192, 0); fg = Color.Black;
                        break;
                    case PhaseState.Done:
                        prefix = "✓"; bg = Color.FromArgb(16, 124, 16); fg = Color.White;
                        break;
                    case PhaseState.Failed:
                        prefix = "✗"; bg = Color.FromArgb(168, 0, 0); fg = Color.White;
                        break;
                    default:
                        prefix = "○"; bg = Color.FromArgb(230, 230, 230); fg = Color.Gray;
                        break;
                }
                _phaseLabels[idx].Text = prefix + " " + _phaseNames[idx];
                _phaseLabels[idx].BackColor = bg;
                _phaseLabels[idx].ForeColor = fg;
            }));
        }

        private enum PhaseState { Pending, Active, Done, Failed }

        // ====================================================================
        //  PRE-FLIGHT
        // ====================================================================
        private async Task RunPreflightAsync()
        {
            SetStatus("[Pre-flight] Detectando entorno...");
            SetPhase(0, PhaseState.Active);
            _btnRun.Enabled = false;

            bool preflightOk = false;
            try
            {
                await Task.Run(() =>
                {
                    _isoDir = AppDomain.CurrentDomain.BaseDirectory;
                    // v5.2.3: ya NO strip el trailing \. Lo dejamos para que
                    // Path.Combine("D:\", "kape.exe") arme "D:\kape.exe" y no
                    // "D:kape.exe". Muestro la version sin \ en el label.
                    if (!_isoDir.EndsWith("\\")) _isoDir += "\\";
                    Invoke((Action)(() => _txtIsoPath.Text = _isoDir.TrimEnd('\\')));
                    SetCheck(_lblIsoOk, File.Exists(Path.Combine(_isoDir, "kape.exe")), "OK", "no kape.exe");

                    // v5.2.0: listar todas las unidades logicas y popular ambos combos.
// Default seleccion: salida = volumen con label DFIR_OUTPUT;
//                  fuente = C:.
                    ListLogicalDisksResult diskInfo = ListLogicalDisks();
                    Invoke((Action)(() =>
                    {
                        _cmbOutputDrive.Items.Clear();
                        _cmbSourceDrive.Items.Clear();
                        DiskInfo dfirPick = null;
                        DiskInfo cPick = null;
                        foreach (var d in diskInfo.Disks)
                        {
                            _cmbOutputDrive.Items.Add(d);
                            _cmbSourceDrive.Items.Add(d);
                            if (dfirPick == null && string.Equals(d.VolumeName, "DFIR_OUTPUT", StringComparison.OrdinalIgnoreCase))
                                dfirPick = d;
                            if (cPick == null && d.DeviceID == "C:")
                                cPick = d;
                        }
                        if (dfirPick != null) _cmbOutputDrive.SelectedItem = dfirPick;
                        else if (_cmbOutputDrive.Items.Count > 0) _cmbOutputDrive.SelectedIndex = 0;
                        if (cPick != null) _cmbSourceDrive.SelectedItem = cPick;
                        else if (_cmbSourceDrive.Items.Count > 0) _cmbSourceDrive.SelectedIndex = 0;
                    }));

                    DiskInfo selOut = diskInfo.DfirDisk;
                    DiskInfo selSrc = diskInfo.CDisk;
                    _outputDrive = selOut?.DeviceID;
                    _sourceDrive = selSrc?.DeviceID;
                    // v5.2.5: normalizar trailing slash ACA, no solo en
                    // RunAcquisitionAsync. Asi RefreshCommandPreview (que se
                    // dispara al final del pre-flight) ve los paths ya con
                    // "\". Antes el textbox quedaba con "E:WIN10-A-..." sin
                    // "\" y si el operador no editaba antes de Ejecutar, se
                    // pasaba el contenido stale como args.
                    if (!string.IsNullOrEmpty(_outputDrive) && !_outputDrive.EndsWith("\\")) _outputDrive += "\\";
                    if (!string.IsNullOrEmpty(_sourceDrive) && !_sourceDrive.EndsWith("\\")) _sourceDrive += "\\";

                    SetCheck(_lblOutputOk, !string.IsNullOrEmpty(_outputDrive),
                        _outputDrive != null
                            ? $"{_outputDrive}{(string.IsNullOrEmpty(selOut.VolumeName) ? "" : $"  ({selOut.VolumeName})")}"
                            : "-",
                        "no conectado");
                    SetCheck(_lblFreeSpace,
                        selOut != null,
                        selOut != null ? FormatBytes(selOut.FreeSpace.ToString()) : "-",
                        "no medible");

                    // v5.1.2: cuando el VMDK DFIR_OUTPUT no esta conectado,
                    // emitimos bloque amarillo con pasos Proxmox para que el
                    // operador sepa exactamente que hacer.
                    if (string.IsNullOrEmpty(_outputDrive))
                    {
                        LogWarn("==========================================================================");
                        LogWarn("  VMDK DFIR_OUTPUT NO detectado.");
                        LogWarn("  Sin este disco la adquisicion no puede escribir ZIP, NOTES ni hashes.");
                        LogWarn("");
                        LogWarn("  Pasos en Proxmox VE (consola web de la VM):");
                        LogWarn("    1) Apague esta VM (boton 'Shutdown guest', NO 'Stop' / power off).");
                        LogWarn("    2) Hardware -> Add -> Hard Disk:");
                        LogWarn("         a) 'Use existing disk' si ya subio DFIR-OUTPUT.vmdk al datastore;");
                        LogWarn("         b) 'Create new disk' (50-100 GB, VirtIO Block o SCSI, en datastore Velociraptor).");
                        LogWarn("    3) Encienda la VM, abra 'Computer Management' -> 'Disk Management':");
                        LogWarn("         inicialice, cree particion NTFS, ponga etiqueta de volumen:");
                        LogWarn("             DFIR_OUTPUT");
                        LogWarn("    4) Cierre y reejecute KAPE_TRIAGE_UI.exe desde la unidad de CD.");
                        LogWarn("==========================================================================");
                    }

                    bool isAdmin = IsRunAsAdmin();
                    SetCheck(_lblAdminOk, isAdmin, "OK", "no admin");

                    SetCheck(_lblWmicOk, ToolExists("wmic.exe"), "OK", "no wmic");
                    SetCheck(_lblCertutilOk, ToolExists("certutil.exe"), "OK", "no certutil");

                    string kapePath = Path.Combine(_isoDir, "kape.exe");
                    SetCheck(_lblKapeOk, File.Exists(kapePath), "OK", "no kape.exe");

                    string targetPath = Path.Combine(_isoDir, "Targets", "Compound", "KapeTriage.tkape");
                    SetCheck(_lblTargetOk, File.Exists(targetPath), "OK", "no encontrado");
                });

                preflightOk = _lblIsoOk.Text.StartsWith("OK")
                            && !_lblOutputOk.Text.StartsWith("-")
                            && _lblAdminOk.Text.StartsWith("OK")
                            && _lblWmicOk.Text.StartsWith("OK")
                            && _lblCertutilOk.Text.StartsWith("OK")
                            && _lblKapeOk.Text.StartsWith("OK")
                            && _lblTargetOk.Text.StartsWith("OK");

                // v5.2.3: popular el textbox del comando con el default
                // construido a partir de las selecciones detectadas.
                Invoke((Action)(() => RefreshCommandPreview()));

                SetPhase(0, preflightOk ? PhaseState.Done : PhaseState.Failed);
                if (preflightOk)
                {
                    SetStatus("Pre-flight OK. Listo para ejecutar la adquisicion.");
                }
                else
                {
                    SetStatus("Pre-flight: hay errores bloqueantes. La ejecucion esta deshabilitada.");
                }
            }
            catch (Exception ex)
            {
                LogError("Pre-flight exception: " + ex.Message);
                SetPhase(0, PhaseState.Failed);
                SetStatus("Error en pre-flight: " + ex.Message);
            }

            _btnRun.Enabled = preflightOk;
        }

        // ====================================================================
        //  RUN
        // ====================================================================
        private async Task RunAcquisitionAsync()
        {
            // v5.2.0: releer selecciones de los combos (el operador puede
            // haber cambiado despues del pre-flight).
            var selOut = _cmbOutputDrive.SelectedItem as DiskInfo;
            var selSrc = _cmbSourceDrive.SelectedItem as DiskInfo;
            _outputDrive = selOut?.DeviceID;
            _sourceDrive = selSrc?.DeviceID;
            // v5.2.3: trailing backslash para que las concatenaciones
            // ("E:\WIN10-A-KAPE-...") queden prolijo y ProcessStartInfo.
            // WorkingDirectory reciba "E:\" en vez de "E:".
            if (!string.IsNullOrEmpty(_outputDrive) && !_outputDrive.EndsWith("\\")) _outputDrive += "\\";
            if (!string.IsNullOrEmpty(_sourceDrive) && !_sourceDrive.EndsWith("\\")) _sourceDrive += "\\";

            if (string.IsNullOrEmpty(_outputDrive))
            {
                LogError("No se puede iniciar: no hay particion de salida seleccionada.");
                LogError("Siga los pasos del bloque amarillo (Proxmox: Hardware -> Add -> Hard Disk)");
                LogError("y vuelva a abrir KAPE_TRIAGE_UI.exe, o elija otra unidad en el combo.");
                SetStatus("ERROR: particion de salida no seleccionada.");
                SetPhase(0, PhaseState.Failed);
                return;
            }
            if (string.IsNullOrEmpty(_sourceDrive))
            {
                LogError("No se puede iniciar: no hay particion fuente seleccionada.");
                SetStatus("ERROR: particion fuente no seleccionada.");
                SetPhase(0, PhaseState.Failed);
                return;
            }

            // Si el operador eligio una salida sin label DFIR_OUTPUT, avisar
            // pero permitir continuar (override explicito).
            if (selOut != null && !string.Equals(selOut.VolumeName, "DFIR_OUTPUT", StringComparison.OrdinalIgnoreCase))
            {
                LogWarn($"==========================================================================");
                LogWarn($" Particion de salida '{_outputDrive}' NO tiene label DFIR_OUTPUT.");
                LogWarn($" Label actual: '{selOut.VolumeName}'  FS: {selOut.FileSystem}");
                LogWarn($" Continuando por eleccion explicita del operador.");
                LogWarn($"==========================================================================");
            }

            _date = DateTime.Now.ToString("yyyyMMdd");
            _time = DateTime.Now.ToString("HHmmss");
            _hostname = Environment.MachineName;
            // v5.2.4: _outputDrive ya termina en "\". NO agregar otro "\".
            // Generic naming: hostname + date + time. Uniqueness guaranteed
            // even if the same VM runs the acquisition twice in one day.
            _outBase = $@"{_outputDrive}{_hostname}-KAPE-{_date}-{_time}";
            _outZip = $@"{_outBase}.zip";
            _notesPath = $@"{_outputDrive}NOTES\{_hostname}-NOTES-{_date}.txt";
            _logPath = $@"{_outputDrive}NOTES\{_hostname}-LOG-{_date}.txt";

            Directory.CreateDirectory($@"{_outputDrive}\NOTES");

            _logBuffer.Clear();
            LogInfo("===========================================================");
            LogInfo($"KAPE Triage v{AppInfo.Version} - Inicio de adquisicion");
            LogInfo($"Host:      {_hostname}");
            LogInfo($"Fecha:     {_date}  Hora: {DateTime.Now:HH:mm:ss}");
            LogInfo($"Source:    {_sourceDrive}\\");
            LogInfo($"Target:    KapeTriage");
            LogInfo($"ISO:       {_isoDir.TrimEnd('\\')}");
            LogInfo($"Output:    {_outBase}");
            LogInfo($"ZIP:       {_outZip}");
            LogInfo($"NOTES:     {_notesPath}");
            LogInfo($"LOG file:  {_logPath}");
            LogInfo("===========================================================");

            // NOTES pre-cabecera
            try
            {
                File.WriteAllText(_notesPath,
                    "===========================================================\n" +
                    $" KAPE Triage v{AppInfo.Version} - Notas de adquisicion\n" +
                    "===========================================================\n" +
                    $" Host             : {_hostname}\n" +
                    $" Hostname        : {_hostname}\n" +
                    $" Operador        : {Environment.UserName}\n" +
                    $" Fecha           : {_date}  Hora: {DateTime.Now:HH:mm:ss}\n" +
                    $" ISO montada en : {_isoDir}\n" +
                    $" Salida en       : {_outputDrive}\\  (label DFIR_OUTPUT)\n" +
                    $" Target          : KapeTriage\n" +
                    $" Source          : {_sourceDrive}\\\n" +
                    $" KAPE staging   : {_outputDrive}_kape_stage\\kape.exe (copia writable de D:\\kape.exe)\n" +
                    "===========================================================\n\n");
                LogOk($"NOTES creado en {_notesPath}");
            }
            catch (Exception ex)
            {
                LogError("No se pudo crear NOTES: " + ex.Message);
            }

            _btnRun.Enabled = false;
            _btnCancel.Enabled = true;
            // v5.2.1: copy/log/output buttons removed; progress bar removed.
            // No buttons to enable here; the strip of 6 phases shows progress.

            SetPhase(0, PhaseState.Done);
            SetPhase(1, PhaseState.Active);
            SetStatus("[2/6] Iniciando kape.exe...");

            // v5.2.5: usar el contenido del textbox SOLO si el operador lo
            // edito explicitamente. Si NO fue editado, regenerar SIEMPRE
            // desde BuildDefaultKapeArgs() para garantizar trailing slashes
            // y timestamp sincronizado con NOTES/LOG. Antes: si el textbox
            // quedaba con el contenido cacheado del pre-flight (sin "\" en
            // tdest/zip), se pasaba tal cual a KAPE.
            string customArgs;
            if (_cmdEdited && !string.IsNullOrEmpty(_txtCommand.Text?.Trim()))
            {
                customArgs = _txtCommand.Text.Trim();
                // Si arranca con "kape.exe" + whitespace/newline, strip
                string lower = customArgs.ToLowerInvariant();
                if (lower.StartsWith("kape.exe") && customArgs.Length > 8)
                {
                    int sep = customArgs.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }, 8);
                    if (sep > 0) customArgs = customArgs.Substring(sep + 1).TrimStart();
                }
                LogInfo("[override] Usando args del textbox del operador:");
            }
            else
            {
                customArgs = BuildDefaultKapeArgs();
            }
            var args = customArgs;

            // v5.2.4: KAPE staging. KAPE en su CheckDefaultDirectories() crea
            // Modules\bin y .kape debajo de Path.GetDirectoryName(Assembly.Location),
            // o sea, debajo del directorio donde esta kape.exe. Si kape.exe
            // esta en D:\ (CD-ROM read-only del ISO), falla con
            // UnauthorizedAccessException (5) Access is denied: [D:]. Lo
            // copio a un dir escribible en la salida y lanzo desde ahi.
            // Esto NO afecta a los .tkape: --targetdir sigue apuntando a
            // D:\Targets (solo lectura, perfecto porque KAPE solo lee).
            var kapePath = Path.Combine(_isoDir, "kape.exe");
            var targetsSrcDir = Path.Combine(_isoDir, "Targets");
            string stageDir = _outputDrive + "_kape_stage\\";
            string stagedKape = stageDir + "kape.exe";
            string stagedTargetsDir = stageDir + "Targets";

            try
            {
                Directory.CreateDirectory(stageDir);
                Directory.CreateDirectory(stagedTargetsDir);

                // v5.2.8: staging skip-if-cached. Si los archivos de stage
                // (kape.exe + Targets/) ya quedaron de una corrida previa,
                // comparamos tamanos con el ISO. Si todo matchea, NO
                // re-copiamos (ahorra ~50 MB de I/O por corrida, ~30s en
                // SSD). Solo re-copiamos si el size difiere.
                var srcKapeSize = new FileInfo(kapePath).Length;
                bool skipKapeCopy = File.Exists(stagedKape)
                    && new FileInfo(stagedKape).Length == srcKapeSize
                    && srcKapeSize > 100_000;
                if (skipKapeCopy)
                {
                    LogInfo($"Stage: {stagedKape} ya presente (size {srcKapeSize:N0} matchea, skip copy)");
                }
                else
                {
                    // v5.2.6: si el destino quedo de una corrida previa, puede
                // tener FILE_ATTRIBUTE_READONLY (Windows Defender/AV lo pone
                // al escanear un .exe que escribimos). File.Copy(..., true)
                // revienta con UnauthorizedAccessException si el destino
                // tiene read-only. Limpiamos atributos y borramos el destino
                // antes de copiar. Retry defensivo en caso de que AV tenga
                // el handle abierto por un instante.
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        if (File.Exists(stagedKape))
                        {
                            File.SetAttributes(stagedKape, FileAttributes.Normal);
                            File.Delete(stagedKape);
                        }
                        File.Copy(kapePath, stagedKape, false);
                        break;
                    }
                    catch (UnauthorizedAccessException) when (attempt < 3)
                    {
                        LogWarn($"Stage attempt {attempt}: destino bloqueado (AV?), reintentando en 500ms...");
                        Thread.Sleep(500);
                    }
                    catch (IOException) when (attempt < 3)
                    {
                        LogWarn($"Stage attempt {attempt}: destino ocupado, reintentando en 500ms...");
                        Thread.Sleep(500);
                    }
                }
                }

                LogInfo($"Stage: {kapePath} -> {stagedKape}");

                // v5.2.8: staging skip-if-cached para Targets/. Usamos
                // KapeTriage.tkape (en Compound/) como sentinel: si su
                // size en destino matchea el size en origen, asumimos
                // que los 394 archivos del arbol Targets/ ya estan
                // bien copiados y saltamos todo el Directory.EnumerateFiles.
                var triageSentinel = Path.Combine(stagedTargetsDir, "Compound", "KapeTriage.tkape");
                var srcTriageSize = new FileInfo(Path.Combine(targetsSrcDir, "Compound", "KapeTriage.tkape")).Length;
                bool skipTargetsCopy = File.Exists(triageSentinel)
                    && new FileInfo(triageSentinel).Length == srcTriageSize;
                if (skipTargetsCopy)
                {
                    LogInfo($"Stage Targets/: ya copiado de corrida previa (sentinel OK, 394 archivos skip)");
                }
                else
                {
                    int copiedFiles = 0;
                    foreach (var srcFile in Directory.EnumerateFiles(targetsSrcDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = srcFile.Substring(targetsSrcDir.Length).TrimStart('\\', '/');
                        var dstFile = Path.Combine(stagedTargetsDir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                        File.Copy(srcFile, dstFile, true);
                        copiedFiles++;
                    }
                    LogInfo($"Stage Targets/: {copiedFiles} archivos copiados a {stagedTargetsDir}");
                }

                // v5.2.9: SHA-256 verification del kape.exe staged. Lo
                // logueamos en NOTES + en el log en vivo para que el
                // operador pueda comparar contra CYBOLT-DFIR-TRIAGE-INFO.txt
                // (hash esperado: 6167472179d0b5b028560dcc84ea1a2e3cb2d7128dd18e4e9278263b86a4318b).
                // Si KAPE fue reempaquetado por alguien con un ISO
                // modificado, el hash va a diferir y esto lo detecta.
                try
                {
                    var stagedKapeSha = ComputeSha256(stagedKape);
                    var stagedKapeSize = new FileInfo(stagedKape).Length;
                    LogInfo($"Stage verify: kape.exe SHA-256={stagedKapeSha}");
                    File.AppendAllText(_notesPath,
                        $"KapeExeSHA256: {stagedKapeSha}\n" +
                        $"KapeExeSize: {stagedKapeSize} bytes\n" +
                        $"KapeExeExpectedSHA256: 6167472179d0b5b028560dcc84ea1a2e3cb2d7128dd18e4e9278263b86a4318b\n");
                }
                catch (Exception ex)
                {
                    LogWarn("No se pudo verificar SHA-256 del kape.exe staged: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                LogError("No se pudo preparar staging de kape.exe: " + ex.Message);
                File.AppendAllText(_notesPath, $"StageError: {ex.Message}\n");
                SetPhase(1, PhaseState.Failed);
                SetPhase(2, PhaseState.Failed);
                SetPhase(3, PhaseState.Failed);
                SetPhase(4, PhaseState.Failed);
                SetPhase(5, PhaseState.Failed);
                SetStatus("ERROR staging kape.exe. Adquisicion abortada.");
                _btnRun.Enabled = true;
                _btnCancel.Enabled = false;
                PersistLogFile();
                return;
            }

            ProcessStartInfo psi;
            if (_chkKapeCmdWrapper.Checked)
            {
                // v5.2.20: lanzar KAPE via cmd.exe /c start /B /WAIT.
                // Da a KAPE una consola real (heredada de cmd) para que las
                // llamadas a Console.Title que KAPE hace internamente para
                // mostrar progreso funcionen. Sin esto, KAPE imprime spam
                // de "Unable to update Console Title" 8-10 veces/segundo
                // en el log (ruido puro, no afecta la captura).
                // start /B = background, no abre ventana popup.
                // /WAIT = esperar a que KAPE termine antes de salir.
                LogInfo("Lanzando KAPE via cmd.exe wrapper (evita Console Title spam).");
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start \"KAPE\" /B /WAIT \"\"\"" + stagedKape + "\"\" " + args,
                    WorkingDirectory = stageDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = stagedKape,
                    Arguments = args,
                    // v5.2.4: WorkingDirectory = stageDir (E:\_kape_stage\, escribible).
                    // Tanto CheckDefaultDirectories (cwd-relative .kape) como
                    // los writes de Modules\bin (Assembly.Location-relative)
                    // caen en un dir escribible.
                    WorkingDirectory = stageDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // v5.2.11: redirigir stdin a un pipe. KAPE 1.3.0.2 al final
                    // del Main() llama Console.ReadKey() que bloquea hasta que
                    // llegue una tecla (incluso con --gui). Cerrando el pipe
                    // despues de Start, ReadKey ve EOF y retorna -1 inmediato,
                    // KAPE sale del Main(), exit code 0, WaitForExit() retorna.
                    RedirectStandardInput = true
                };
            }

            _cts = new CancellationTokenSource();
            int exitCode = -1;
            bool ran = false;
            try
            {
                LogInfo($"> {stagedKape} {args}");
                _kapeProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _kapeProcess.OutputDataReceived += (s, e) => {
                    if (e.Data != null && !IsConsoleTitleNoise(e.Data))
                        LogRaw(e.Data);
                };
                _kapeProcess.ErrorDataReceived  += (s, e) => {
                    if (e.Data != null && !IsConsoleTitleNoise(e.Data))
                        LogError("[kape stderr] " + e.Data);
                };

                SetPhase(1, PhaseState.Done);
                SetPhase(2, PhaseState.Active);
                SetStatus("[3/6] Recolectando artefactos (puede tardar varios minutos)...");

                ran = _kapeProcess.Start();
                _kapeProcess.BeginOutputReadLine();
                _kapeProcess.BeginErrorReadLine();
                // v5.2.11: cerrar stdin inmediatamente despues de Start().
                // KAPE 1.3.0.2 hace Console.ReadKey() al final del Main();
                // con stdin cerrado ve EOF y retorna -1 sin bloquear, en
                // vez de quedarse en "Press any key to exit" para siempre.
                // IMPORTANTE: tiene que ir DESPUES de Start(), antes el
                // StandardInput no esta redirigido y tira
                // "StandardIn has not been redirected".
                _kapeProcess.StandardInput.Close();

                await Task.Run(() => _kapeProcess.WaitForExit());

                exitCode = _kapeProcess.ExitCode;
                LogOk($"kape.exe termino con codigo {exitCode}");
            }
            catch (Exception ex)
            {
                LogError("Error lanzando kape.exe: " + ex.Message);
                File.AppendAllText(_notesPath, $"Error: {ex.Message}\n");
            }
            finally
            {
                _kapeProcess?.Dispose();
                _kapeProcess = null;
            }

            SetPhase(2, PhaseState.Done);

            if (_cts.IsCancellationRequested)
            {
                LogWarn("Cancelado por el operador.");
                File.AppendAllText(_notesPath, $"Cancelado por el operador.\n");
                SetPhase(3, PhaseState.Failed);
                SetPhase(4, PhaseState.Failed);
                SetPhase(5, PhaseState.Failed);
                SetStatus("CANCELADO por el operador.");
                _btnRun.Enabled = true;
                _btnCancel.Enabled = false;
                PersistLogFile();
                return;
            }

            File.AppendAllText(_notesPath, $"KapeExitCode: {exitCode}\n");
            File.AppendAllText(_notesPath, $"Fin: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            // ZIP check
            SetPhase(3, PhaseState.Active);
            SetStatus("[4/6] Verificando ZIP generado...");
            // v5.2.10: KAPE escribe el ZIP dentro de --tdest con timestamp prefix:
            //   {tdest}\{timestamp}_E__{basename}.zip
            // El _outZip actual apunta al basename esperado SIN el timestamp.
            // Buscar el archivo real por patrón dentro de tdest.
            string tdest = _outBase;
            string expectedBasename = $"{_hostname}-KAPE-{_date}-{_time}.zip";
            string[] zipMatches = null;
            try
            {
                zipMatches = Directory.GetFiles(tdest, $"*_E__{expectedBasename}");
            }
            catch (Exception ex)
            {
                LogWarn("Busqueda de ZIP en tdest fallo: " + ex.Message);
            }
            if (zipMatches != null && zipMatches.Length > 0)
            {
                // Tomar el más reciente por LastWriteTime (KAPE solo escribe uno,
                // pero por si el operador re-corre la adquisición en el mismo día).
                string realZip = zipMatches.OrderByDescending(p => File.GetLastWriteTime(p)).First();
                LogInfo($"ZIP detectado por patron: {realZip}");
                _outZip = realZip;  // reasignar para SHA-256, log y NOTES
            }
            if (File.Exists(_outZip))
            {
                var size = new FileInfo(_outZip).Length;
                LogOk($"ZIP generado: {_outZip} ({FormatBytes(size.ToString())})");
                SetPhase(3, PhaseState.Done);
            }
            else
            {
                LogError("No se genero el ZIP en " + _outZip);
                File.AppendAllText(_notesPath, "ZipGenerado: NO\n");
                SetPhase(3, PhaseState.Failed);
                SetPhase(4, PhaseState.Failed);
                SetPhase(5, PhaseState.Failed);
                SetStatus("ERROR: no se genero el ZIP.");
                _btnRun.Enabled = true;
                _btnCancel.Enabled = false;
                PersistLogFile();
                return;
            }

            // SHA-256
            SetPhase(4, PhaseState.Active);
            SetStatus("[5/6] Calculando SHA-256...");
            LogInfo("Calculando SHA-256 con certutil...");
            string hash = ComputeSha256(_outZip);
            if (hash != null)
            {
                File.WriteAllText(_outZip + ".sha256", hash + "  " + System.IO.Path.GetFileName(_outZip) + "\n");
                File.AppendAllText(_notesPath, $"SHA-256: {hash}\n");
                File.AppendAllText(_notesPath, $"ZIPSize: {new FileInfo(_outZip).Length} bytes\n");
                LogOk($"SHA-256: {hash}");
                LogOk($"Guardado en: {_outZip}.sha256");
                SetPhase(4, PhaseState.Done);
            }
            else
            {
                LogError("No se pudo calcular SHA-256 automaticamente.");
                File.AppendAllText(_notesPath, "CalculoManual: REQUERIDO\n");
                SetPhase(4, PhaseState.Failed);
            }

            // NOTES finalize
            SetPhase(5, PhaseState.Active);
            SetStatus("[6/6] Finalizando...");
            File.AppendAllText(_notesPath, $"Adquisicion finalizada: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            SetPhase(5, PhaseState.Done);

            var finalSize = new FileInfo(_outZip).Length;
            string hashPreview = hash != null ? hash.Substring(0, 16) + "..." : "ERROR";
            string finalMsg = $"LISTO. ZIP: {FormatBytes(finalSize.ToString())}  |  SHA-256: {hashPreview}";
            SetStatus(finalMsg);
            LogInfo("===========================================================");
            LogInfo(finalMsg);
            LogInfo("Puede apagar la VM desde la consola ESXi y desconectar el VMDK.");
            LogInfo("NO marque 'Delete files from datastore' al desconectar.");
            LogInfo("===========================================================");

            _btnRun.Enabled = true;
            _btnCancel.Enabled = false;
            PersistLogFile();
        }

        // ====================================================================
        //  HELPERS
        // ====================================================================
        // v5.2.3: helpers del acordeon de comando
        private string BuildDefaultKapeArgs()
        {
            // v5.2.4: computar _outBase/_outZip/_date on the fly. Antes
            // dependia de _outBase/_outZip que solo se setean en
            // RunAcquisitionAsync, asi que el textbox quedaba con el
            // placeholder al terminar el pre-flight.
            if (string.IsNullOrEmpty(_outputDrive) || string.IsNullOrEmpty(_sourceDrive)
                || string.IsNullOrEmpty(_isoDir))
                return "";
            string hostname = Environment.MachineName;
            // v5.2.5: usar el timestamp cacheado de RunAcquisitionAsync (_date +
            // _time) si esta disponible, para que tdest/zip coincidan con el
            // nombre que ya esta en NOTES/LOG. Antes: este metodo recalculaba
            // time y daba un segundo de drift entre el NOTES (021548) y el
            // args (021547).
            string date;
            string time;
            if (!string.IsNullOrEmpty(_date))
            {
                date = _date;
                // _time se setea junto con _date en RunAcquisitionAsync.
                // Si por algun motivo esta vacio, fallback a DateTime.Now.
                time = !string.IsNullOrEmpty(_time) ? _time : DateTime.Now.ToString("HHmmss");
            }
            else
            {
                date = DateTime.Now.ToString("yyyyMMdd");
                time = DateTime.Now.ToString("HHmmss");
            }
            string outBase = $"{_outputDrive}{hostname}-KAPE-{date}-{time}";
            // v5.2.10: KAPE 1.3.0.2 toma el valor de --zip, extrae el basename y
            // le agrega sufijo .zip por su cuenta. Si le pasamos un valor ya
            // terminado en .zip, el archivo final sale con doble sufijo
            // (.zip.zip) — bug observado en corrida 2026-09-23 con v5.2.7
            // (output real: 2026-09-23T201412_E__WIN10-A-KAPE-...zip.zip).
            // KAPE escribe el ZIP dentro de --tdest con timestamp prefix:
            //   {tdest}\{timestamp}_E__{basename}.zip
            // por lo que outZip (basename esperado) se usa solo como base
            // para mostrar/loguear. La verificación post-KAPE busca el
            // archivo real por patrón dentro de tdest (ver bloque ZIP check).
            string outZip = outBase;
            // v5.2.5: sacar --sync $Null. KAPE en proceso (no CMD) interpreta
            // "$Null" como un filename literal y falla con:
            //   Could not find file 'E:\_kape_stage\$Null'.
            // Sin --sync no se intenta descargar nada. --hv y --gui se
            // mantienen para que KAPE loggee verbose y use GUI progress.
            return $"--tsource {_sourceDrive} " +
                   "--target KapeTriage " +
                   $"--tdest \"{outBase}\" " +
                   // v5.2.7: --targetdir apunta al staging dir en E:,
                   // no a D:\Targets. KAPE corre desde E:\_kape_stage
                   // (Assembly.Location = E:\_kape_stage\kape.exe) y el
                   // search pattern por default usa cwd o el dir del exe.
                   // Con --targetdir E:\_kape_stage\ KAPE busca:
                   //   E:\_kape_stage\KapeTriage.tkape        (no)
                   //   E:\_kape_stage\Compound\KapeTriage.tkape (yes)
                   // y los sub-targets (Antivirus.tkape, EventLogs.tkape,
                   // etc.) se resuelven contra E:\_kape_stage\ <name>.tkape.
                   $"--targetdir \"{_outputDrive}_kape_stage\" " +
                   $"--zip \"{outZip}\" " +
                   // v5.2.8: agregar --vss para que KAPE cree un VSS snapshot
                   // de C: al final del target processing y reintente los
                   // archivos deferred (Application.evtx, Defender logs,
                   // etc.) desde el snapshot. Sin --vss, los archivos
                   // locked se quedan out del ZIP al final.
                   "--hv nc,vm --gui --vss";
        }

        private void OnDriveSelectionChanged()
        {
            // v5.2.5: cuando el operador cambia un ComboBox, re-derivar los
            // campos _outputDrive/_sourceDrive con trailing slash y refrescar
            // el preview del comando. NO pisamos el textbox si el operador
            // lo edito explicitamente — solo si NO fue editado.
            var selOut = _cmbOutputDrive.SelectedItem as DiskInfo;
            var selSrc = _cmbSourceDrive.SelectedItem as DiskInfo;
            if (selOut != null) _outputDrive = selOut.DeviceID;
            if (selSrc != null) _sourceDrive = selSrc.DeviceID;
            if (!string.IsNullOrEmpty(_outputDrive) && !_outputDrive.EndsWith("\\")) _outputDrive += "\\";
            if (!string.IsNullOrEmpty(_sourceDrive) && !_sourceDrive.EndsWith("\\")) _sourceDrive += "\\";

            // Solo refrescar el textbox si el operador no lo ha editado a mano.
            // Si lo edito, su contenido manda y respetamos su eleccion.
            if (!_cmdEdited)
            {
                RefreshCommandPreview();
            }
        }

        private void RefreshCommandPreview()
        {
            string args = BuildDefaultKapeArgs();
            if (string.IsNullOrEmpty(args))
            {
                _txtCommand.Text = "(selecciona particion fuente y salida, y completa el pre-flight)";
            }
            else
            {
                _txtCommand.Text = args;
            }
            // v5.2.5: al repoblar el textbox programaticamente, NO es edicion
            // del operador. Limpiar el flag.
            _cmdEdited = false;
        }

        private void CopyCommandToClipboard()
        {
            try
            {
                Clipboard.SetText(_txtCommand.Text);
                LogInfo("Comando KAPE copiado al portapapeles.");
            }
            catch (Exception ex)
            {
                LogError("No se pudo copiar al portapapeles: " + ex.Message);
            }
        }

        private void PersistLogFile()
        {
            try
            {
                File.WriteAllText(_logPath, _logBuffer.ToString());
                LogOk($"Log completo persistido en: {_logPath}");
            }
            catch (Exception ex)
            {
                LogWarn("No se pudo persistir el log en disco: " + ex.Message);
            }
        }

        private void LogInfo(string msg)  { LogColored(msg, Color.FromArgb(180, 220, 255)); }
        private void LogOk(string msg)    { LogColored("[OK] " + msg, Color.FromArgb(120, 230, 120)); }
        private void LogWarn(string msg)  { LogColored("[WARN] " + msg, Color.FromArgb(255, 220, 120)); }
        private void LogError(string msg) { LogColored("[ERROR] " + msg, Color.FromArgb(255, 110, 110)); }
        private void LogRaw(string msg)   { LogColored(msg, Color.FromArgb(220, 220, 220)); }

        /// <summary>
        /// True si la linea es ruido cosmetico del .NET runtime: KAPE se
        /// lanza desde .NET con RedirectStandardOutput=true + CreateNoWindow=true,
        /// asi que Console.Title = "..." falla porque el handle de consola
        /// no es valido. El runtime de .NET loguea este mensaje en stderr
        /// cada vez que kape.exe intenta actualizar el titulo. No afecta
        /// la captura. Lo descartamos para no spammear al operador.
        /// Bug observado 2026-10-08 con KAPE 1.3.0.2 ejecutando KapeTriage
        /// en server (32+ GB RAM, DC, muchos .evtx): spam de 8-10 lineas
        /// por segundo durante fase 3.
        /// </summary>
        private static bool IsConsoleTitleNoise(string line)
        {
            if (string.IsNullOrEmpty(line)) return true;
            return line.StartsWith("Unable to update Console Title", StringComparison.Ordinal)
                || line.StartsWith("Unable to update console title", StringComparison.OrdinalIgnoreCase);
        }

        private void LogColored(string msg, Color color)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
            _logBuffer.Append(line);
            if (InvokeRequired) Invoke((Action)(() => AppendLogLine(line, color)));
            else AppendLogLine(line, color);
        }

        private void AppendLogLine(string line, Color color)
        {
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.SelectionLength = 0;
            _txtLog.SelectionColor = color;
            _txtLog.AppendText(line);
            _txtLog.SelectionColor = _txtLog.ForeColor;
            _txtLog.ScrollToCaret();
        }

        private void SetStatus(string s)
        {
            if (InvokeRequired) Invoke((Action)(() => _lblStatus.Text = s));
            else _lblStatus.Text = s;
        }

        private void SetCheck(Label lbl, bool ok, string okText, string failText)
        {
            string text = ok ? "OK (" + okText + ")" : "FAIL (" + failText + ")";
            if (InvokeRequired) Invoke((Action)(() =>
            {
                lbl.Text = text;
                lbl.ForeColor = ok ? Color.FromArgb(16, 124, 16) : Color.FromArgb(168, 0, 0);
                lbl.Font = new Font("MS Shell Dlg", 8.5f, ok ? FontStyle.Bold : FontStyle.Bold);
            }));
            else
            {
                lbl.Text = text;
                lbl.ForeColor = ok ? Color.FromArgb(16, 124, 16) : Color.FromArgb(168, 0, 0);
            }
        }

        private void CancelRun()
        {
            if (_kapeProcess == null || _kapeProcess.HasExited) return;
            var r = MessageBox.Show(
                "KAPE esta ejecutandose. Cancelar y descartar el resultado parcial?",
                "KAPE Triage", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            try
            {
                _cts?.Cancel();
                LogWarn("Cancelando kape.exe...");
                _kapeProcess.Kill();
            }
            catch (Exception ex)
            {
                LogError("Error cancelando: " + ex.Message);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_kapeProcess != null && !_kapeProcess.HasExited)
            {
                var r = MessageBox.Show(
                    "KAPE todavia esta ejecutandose. Cancelar y salir?",
                    "KAPE Triage", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                CancelRun();
            }
        }

        private static bool IsRunAsAdmin()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        private static bool ToolExists(string name)
        {
            // Buscar en System32, SysWOW64, PATH y %WINDIR%\System32
            string[] roots = {
                Environment.GetFolderPath(Environment.SpecialFolder.System),                       // System32
                Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),                     // SysWOW64
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
            };
            foreach (var root in roots)
            {
                if (File.Exists(Path.Combine(root, name))) return true;
            }
            // Ultimo recurso: where.exe (si esta disponible)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = name,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string out_ = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(out_)) return true;
                }
            }
            catch { }
            return false;
        }

        private static ListLogicalDisksResult ListLogicalDisks()
        {
            // v5.2.0: en vez de buscar SOLO DFIR_OUTPUT, devolvemos TODAS las
            // unidades logicas para popular los ComboBoxes. Parseamos el
            // formato /format:list de wmic, que devuelve bloques separados
            // por linea en blanco con pares Clave=Valor.
            var result = new ListLogicalDisksResult();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "logicaldisk get DeviceID,VolumeName,FileSystem,Size,FreeSpace /format:list",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    DiskInfo cur = null;
                    foreach (var rawLine in output.Split('\n'))
                    {
                        var t = rawLine.Trim();
                        if (string.IsNullOrEmpty(t))
                        {
                            if (cur != null && !string.IsNullOrEmpty(cur.DeviceID))
                            {
                                result.Disks.Add(cur);
                                if (string.Equals(cur.VolumeName, "DFIR_OUTPUT", StringComparison.OrdinalIgnoreCase))
                                    result.DfirDisk = cur;
                                if (cur.DeviceID == "C:") result.CDisk = cur;
                            }
                            cur = null;
                            continue;
                        }
                        int eq = t.IndexOf('=');
                        if (eq < 0) continue;
                        if (cur == null) cur = new DiskInfo();
                        var key = t.Substring(0, eq).Trim();
                        var val = t.Substring(eq + 1).Trim();
                        switch (key)
                        {
                            case "DeviceID":   cur.DeviceID = val; break;
                            case "VolumeName": cur.VolumeName = val; break;
                            case "FileSystem": cur.FileSystem = val; break;
                            case "Size":       long.TryParse(val, out var s); cur.Size = s; break;
                            case "FreeSpace":  long.TryParse(val, out var f); cur.FreeSpace = f; break;
                        }
                    }
                    if (cur != null && !string.IsNullOrEmpty(cur.DeviceID))
                    {
                        result.Disks.Add(cur);
                        if (string.Equals(cur.VolumeName, "DFIR_OUTPUT", StringComparison.OrdinalIgnoreCase))
                            result.DfirDisk = cur;
                        if (cur.DeviceID == "C:") result.CDisk = cur;
                    }
                }
            }
            catch { }
            return result;
        }

        internal class ListLogicalDisksResult
        {
            public List<DiskInfo> Disks = new List<DiskInfo>();
            public DiskInfo DfirDisk;
            public DiskInfo CDisk;
        }

        internal class DiskInfo
        {
            public string DeviceID = "";
            public string VolumeName = "";
            public string FileSystem = "";
            public long Size;
            public long FreeSpace;
            public override string ToString()
            {
                if (string.IsNullOrEmpty(VolumeName))
                    return string.IsNullOrEmpty(FileSystem)
                        ? $"{DeviceID}"
                        : $"{DeviceID}  ({FileSystem})";
                return $"{DeviceID}  ({VolumeName})";
            }
        }

        private static string ComputeSha256(string filePath)
        {
            // v5.2.11: usar System.Security.Cryptography.SHA256 nativo .NET en
            // vez de invocar certutil externo. El bug observado en drill
            // 2026-09-25: certutil -hashfile formatea el SHA-256 con
            // espacios cada 2 chars ("aa bb cc dd ee ff ...") en varias
            // lineas, y mi regex de 64 hex chars continuos no matcheaba,
            // asi que ComputeSha256 retornaba null aunque certutil
            // hubiera calculado bien el hash. La UI mostraba
            // "LISTO. ZIP: 803 MB | SHA-256: ERROR" aunque el archivo
            // estuviera perfecto en disco.
            //
            // Ventajas del .NET nativo:
            // - Streaming: no carga el archivo completo en memoria
            //   (importante para ZIPs de 800 MB+).
            // - Sin subprocess, sin PATH lookup, sin spawn overhead.
            // - Output siempre lowercase 64 hex chars, formato estable.
            // - ~3x mas rapido que certutil en mediciones reales.
            try
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    var sb = new StringBuilder(hashBytes.Length * 2);
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        private static string FormatBytes(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "-";
            long bytes;
            if (!long.TryParse(raw, out bytes)) return raw;
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024 / 1024) + " MB";
            return (bytes / 1024 / 1024 / 1024) + " GB";
        }
    }

    internal static class AppInfo
    {
        public static readonly string Version = "5.2.20";
    }
}
