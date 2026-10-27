using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DfirTriageUi
{
    /// <summary>
    /// Wizard consolidado: KapeTriage + RAM Capture en una sola GUI.
    /// Pasos:
    ///   1. Welcome
    ///   2. Deteccion (bundle, disco entrada, carpeta salida, RAM)
    ///   3. KapeTriage (run KAPE con stage desde el bundle a carpeta de salida)
    ///   4. RAM Capture (run winpmem)
    ///   5. Resumen final con SHA-256 + paths
    /// </summary>
    public class DfirTriageMain : Form
    {
        // Common
        private readonly TextBox txtLog = new TextBox();
        private readonly Label lblStep = new Label();
        private readonly Button btnBack = new Button();
        private readonly Button btnNext = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnClose = new Button();
        private readonly Label lblFooter = new Label();

        // Step panels
        private readonly Panel pnlWelcome = new Panel();
        private readonly Panel pnlDetect = new Panel();
        private readonly Panel pnlKape = new Panel();
        private readonly Panel pnlRam = new Panel();
        private readonly Panel pnlSummary = new Panel();

        // Step state
        private int _currentStep = 0; // 0..4
        private CancellationTokenSource _cts;

        // Detection results
        // Carpeta del bundle (kape.exe + Targets/ + Tools/). Preferimos la
        // carpeta del .exe; el lookup por raiz de disco/ISO queda de fallback.
        private string _bundleRoot = null;
        private string _outputFolder = null;   // ruta completa de la carpeta de salida (ej "E:\DFIR-OUTPUT")
        private bool _outputFolderManual = false;
        private string _sourceDrive = "C:";
        private long _totalRamBytes;
        private long _freeSpaceBytes;
        private bool _suppressTextChanged = false;

        // Kape result
        private KapeRunner.Result _kapeResult;
        // RAM result
        private RamRunner.Result _ramResult;
        // Options
        private bool _runRamAfterKape = true;
        private bool _useCmdWrapper = false;

        public DfirTriageMain()
        {
            Text = "CYBOLT DFIR Triage Wizard v5.5.1 - By Cybolt";
            Width = 960;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            Load += (s, e) => OnLoad();
            Shown += (s, e) => CheckAdminOrWarn();
            FormClosing += DfirTriageMain_FormClosing;
        }

        private void BuildLayout()
        {
            int y = 12;

            // Title
            var lblTitle = new Label
            {
                Text = "CYBOLT DFIR Triage Wizard - KapeTriage + RAM Capture",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblTitle);
            y += 30;

            // Step indicator
            lblStep.Location = new Point(12, y);
            lblStep.AutoSize = true;
            lblStep.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
            lblStep.Text = "Paso 1/5: Bienvenida";
            Controls.Add(lblStep);
            y += 22;

            // === PANELS ===

            // Panel: Welcome
            BuildWelcomePanel();
            pnlWelcome.Location = new Point(12, y);
            pnlWelcome.Size = new Size(920, 380);
            Controls.Add(pnlWelcome);

            // Panel: Detect
            BuildDetectPanel();
            pnlDetect.Location = new Point(12, y);
            pnlDetect.Size = new Size(920, 380);
            pnlDetect.Visible = false;
            Controls.Add(pnlDetect);

            // Panel: Kape
            BuildKapePanel();
            pnlKape.Location = new Point(12, y);
            pnlKape.Size = new Size(920, 380);
            pnlKape.Visible = false;
            Controls.Add(pnlKape);

            // Panel: RAM
            BuildRamPanel();
            pnlRam.Location = new Point(12, y);
            pnlRam.Size = new Size(920, 380);
            pnlRam.Visible = false;
            Controls.Add(pnlRam);

            // Panel: Summary
            BuildSummaryPanel();
            pnlSummary.Location = new Point(12, y);
            pnlSummary.Size = new Size(920, 380);
            pnlSummary.Visible = false;
            Controls.Add(pnlSummary);

            y += 390;

            // Buttons
            btnBack.Text = "< Back";
            btnBack.Location = new Point(12, y);
            btnBack.Width = 100;
            btnBack.Click += BtnBack_Click;
            Controls.Add(btnBack);
            btnNext.Text = "Next >";
            btnNext.Location = new Point(118, y);
            btnNext.Width = 100;
            btnNext.Click += BtnNext_Click;
            Controls.Add(btnNext);
            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(224, y);
            btnCancel.Width = 100;
            btnCancel.Click += BtnCancel_Click;
            btnCancel.Enabled = false;
            Controls.Add(btnCancel);
            btnClose.Text = "Close";
            btnClose.Location = new Point(330, y);
            btnClose.Width = 100;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
            y += 32;

            // Log textbox
            txtLog.Location = new Point(12, y);
            txtLog.Width = 920;
            txtLog.Height = 140;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.BackColor = Color.Black;
            txtLog.ForeColor = Color.LightGreen;
            txtLog.Font = new Font("Consolas", 8F);
            Controls.Add(txtLog);
            y += txtLog.Height + 4;

            lblFooter.Text = "CYBOLT_DFIR_TRIAGE.exe v5.5.1 - By Cybolt, MIT License";
            lblFooter.Location = new Point(12, y);
            lblFooter.AutoSize = true;
            lblFooter.ForeColor = Color.Gray;
            Controls.Add(lblFooter);
        }

        // === Welcome panel ===
        private CheckBox chkRunRam = new CheckBox();
        private CheckBox chkCmdWrapper = new CheckBox();
        private Label lblWelcomeText = new Label();

        private void BuildWelcomePanel()
        {
            lblWelcomeText.Location = new Point(12, 12);
            lblWelcomeText.Size = new Size(880, 200);
            lblWelcomeText.Text =
                "Este wizard realiza la adquisicion DFIR:\n\n" +
                "  1. KapeTriage  -  copia evidencia del disco de entrada a la\n" +
                "                    carpeta de salida elegida (ZIP)\n" +
                "  2. RAM Capture -  captura RAM con winpmem (archivo .raw, opt-in)\n\n" +
                "La captura de RAM es OPCIONAL (checkbox abajo, default OFF). Solo\n" +
                "marcala si necesitas memoria volatil para el analisis (procesos\n" +
                "en ejecucion, conexiones activas, claves DPAPI, etc). Si la\n" +
                "omite, el ZIP de KapeTriage igual contiene evidencia persistente\n" +
                "suficiente para el triage inicial.\n\n" +
                "En el paso 2 (Deteccion) elegis el disco de ENTRADA (default C:)\n" +
                "y la CARPETA de SALIDA (autodetectada, pero Browse la cambia).\n\n" +
                "Requisitos:\n" +
                "  - Bundle descomprimido (CYBOLT_DFIR_TRIAGE.exe junto a kape.exe)\n" +
                "  - Carpeta de salida en un medio writable (disco o USB)\n" +
                "  - Privilegios de Administrador (UAC forzado)\n" +
                "  - Sin red durante la captura (la subida va aparte con DFIR_UPLOADER_UI)\n\n" +
                "Si alguno de los pasos falla o se omite, los outputs parciales quedan\n" +
                "en la carpeta de salida y pueden entregarse igual al analista.\n";
            pnlWelcome.Controls.Add(lblWelcomeText);

            var lblOpts = new Label
            {
                Text = "Opciones:",
                Location = new Point(12, 230),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
            };
            pnlWelcome.Controls.Add(lblOpts);

            chkRunRam.Location = new Point(12, 250);
            chkRunRam.AutoSize = true;
            chkRunRam.Text = "Capturar RAM despues de KapeTriage (opt-in, default OFF)";
            chkRunRam.Checked = false;
            pnlWelcome.Controls.Add(chkRunRam);

            chkCmdWrapper.Location = new Point(12, 275);
            chkCmdWrapper.AutoSize = true;
            chkCmdWrapper.Text = "Lanzar via cmd.exe wrapper (evita spam de Console Title en servers)";
            chkCmdWrapper.Checked = false;
            pnlWelcome.Controls.Add(chkCmdWrapper);
        }

        // === Detect panel ===
        private Label lblDetCdRom = new Label();
        private Label lblDetRam = new Label();
        private Label lblDetFree = new Label();
        private Label lblDetOutputInfo = new Label();
        private ComboBox cmbSourceDrive = new ComboBox();
        private TextBox txtOutputFolder = new TextBox();
        private Button btnBrowseOutput = new Button();
        private Button btnDetRefresh = new Button();

        /// <summary>
        /// Item del ComboBox de discos de entrada: guarda la raiz real ("C:")
        /// y muestra una descripcion legible (label del volumen + tipo).
        /// </summary>
        private class DriveChoice
        {
            public string Root;
            public string Description;
            public DriveChoice(string root, string description) { Root = root; Description = description; }
            public override string ToString() { return Description; }
        }

        private void BuildDetectPanel()
        {
            var lblTitle = new Label
            {
                Text = "Deteccion del entorno - parametros de entrada y salida",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 8),
                AutoSize = true,
            };
            pnlDetect.Controls.Add(lblTitle);

            int y = 40;

            // --- Entrada: disco/volumen aquisitions ---
            var lblSrc = new Label
            {
                Text = "Disco de entrada (input):",
                Location = new Point(12, y + 3),
                AutoSize = true,
            };
            pnlDetect.Controls.Add(lblSrc);

            cmbSourceDrive.Location = new Point(170, y);
            cmbSourceDrive.Width = 420;
            cmbSourceDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSourceDrive.SelectedIndexChanged += (s, e) =>
            {
                var sel = cmbSourceDrive.SelectedItem as DriveChoice;
                if (sel == null) return;
                _sourceDrive = sel.Root;
                UpdatePanelInfos();
                UpdateNextEnabled();
            };
            pnlDetect.Controls.Add(cmbSourceDrive);
            y += 30;

            // --- Salida: carpeta donde se escribe la evidencia ---
            var lblOut = new Label
            {
                Text = "Carpeta de salida (output):",
                Location = new Point(12, y + 3),
                AutoSize = true,
            };
            pnlDetect.Controls.Add(lblOut);

            txtOutputFolder.Location = new Point(170, y);
            txtOutputFolder.Width = 570;
            txtOutputFolder.TextChanged += TxtOutputFolder_TextChanged;
            pnlDetect.Controls.Add(txtOutputFolder);

            btnBrowseOutput.Text = "… Browse…";
            btnBrowseOutput.Location = new Point(748, y);
            btnBrowseOutput.Width = 100;
            btnBrowseOutput.Click += BtnBrowseOutput_Click;
            pnlDetect.Controls.Add(btnBrowseOutput);
            y += 30;

            lblDetOutputInfo.Location = new Point(170, y);
            lblDetOutputInfo.AutoSize = true;
            lblDetOutputInfo.ForeColor = Color.Gray;
            lblDetOutputInfo.Text = "";
            pnlDetect.Controls.Add(lblDetOutputInfo);
            y += 26;

            lblDetCdRom.Location = new Point(12, y);
            lblDetCdRom.AutoSize = true;
            lblDetCdRom.Text = "Bundle (kape.exe): detectando...";
            pnlDetect.Controls.Add(lblDetCdRom);
            y += 26;

            lblDetRam.Location = new Point(12, y);
            lblDetRam.AutoSize = true;
            lblDetRam.Text = "RAM fisica: detectando...";
            pnlDetect.Controls.Add(lblDetRam);
            y += 26;

            lblDetFree.Location = new Point(12, y);
            lblDetFree.AutoSize = true;
            lblDetFree.Text = "Espacio libre en carpeta de salida: detectando...";
            pnlDetect.Controls.Add(lblDetFree);
            y += 30;

            btnDetRefresh.Text = "🔄 Re-detectar";
            btnDetRefresh.Location = new Point(12, y);
            btnDetRefresh.Width = 120;
            btnDetRefresh.Click += (s, e) => DetectEnv();
            pnlDetect.Controls.Add(btnDetRefresh);
        }

        // === Kape panel ===
        private Label lblKapeInfo = new Label();
        private Button btnRunKape = new Button();
        private ProgressBar pbKape = new ProgressBar();
        private Label lblKapeStatus = new Label();
        private Label lblKapeResult = new Label();

        private void BuildKapePanel()
        {
            var lblTitle = new Label
            {
                Text = "Paso 3: KapeTriage - Adquisicion del filesystem",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 8),
                AutoSize = true,
            };
            pnlKape.Controls.Add(lblTitle);

            lblKapeInfo.Location = new Point(12, 36);
            lblKapeInfo.Size = new Size(880, 60);
            lblKapeInfo.Text = "KAPE 1.3.0.2 con el set de targets del bundle (targets-full.txt).\n" +
                              "Stage: copia kape.exe + Targets/ a la carpeta de salida (disco writable).\n" +
                              "Entrada: (definiendo en Deteccion)   ->   Salida: (definiendo en Deteccion)";
            pnlKape.Controls.Add(lblKapeInfo);

            btnRunKape.Text = "▶ Ejecutar KapeTriage";
            btnRunKape.Location = new Point(12, 110);
            btnRunKape.Width = 200;
            btnRunKape.Click += BtnRunKape_Click;
            pnlKape.Controls.Add(btnRunKape);

            pbKape.Location = new Point(12, 142);
            pbKape.Width = 880;
            pbKape.Height = 22;
            pnlKape.Controls.Add(pbKape);

            lblKapeStatus.Location = new Point(12, 168);
            lblKapeStatus.AutoSize = true;
            lblKapeStatus.Text = "Estado: -";
            pnlKape.Controls.Add(lblKapeStatus);

            lblKapeResult.Location = new Point(12, 195);
            lblKapeResult.AutoSize = true;
            lblKapeResult.Text = "";
            lblKapeResult.Font = new Font("Consolas", 8F);
            pnlKape.Controls.Add(lblKapeResult);
        }

        // === RAM panel ===
        private Label lblRamInfo = new Label();
        private Button btnRunRam = new Button();
        private ProgressBar pbRam = new ProgressBar();
        private Label lblRamStatus = new Label();
        private Label lblRamResult = new Label();

        private void BuildRamPanel()
        {
            var lblTitle = new Label
            {
                Text = "Paso 4: RAM Capture - Adquisicion de memoria volatil",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 8),
                AutoSize = true,
            };
            pnlRam.Controls.Add(lblTitle);

            lblRamInfo.Location = new Point(12, 36);
            lblRamInfo.Size = new Size(880, 70);
            lblRamInfo.Text = "winpmem_mini_x64_rc2.exe (Velocidex WinPmem v4.0.rc1).\n" +
                              "Salida: (definiendo en Deteccion)\\RAM-<HOSTNAME>-<timestamp>.raw\n" +
                              "Tiempo estimado: 5-30 min dependiendo de la RAM fisica.\n" +
                              "Este paso es opt-in (checkbox en la pantalla de Bienvenida, default OFF).\n" +
                              "Podes volver a Bienvenida con < Back para activar/desactivar la captura.";
            pnlRam.Controls.Add(lblRamInfo);

            btnRunRam.Text = "▶ Ejecutar RAM Capture";
            btnRunRam.Location = new Point(12, 120);
            btnRunRam.Width = 200;
            btnRunRam.Click += BtnRunRam_Click;
            pnlRam.Controls.Add(btnRunRam);

            pbRam.Location = new Point(12, 152);
            pbRam.Width = 880;
            pbRam.Height = 22;
            pnlRam.Controls.Add(pbRam);

            lblRamStatus.Location = new Point(12, 178);
            lblRamStatus.AutoSize = true;
            lblRamStatus.Text = "Estado: -";
            pnlRam.Controls.Add(lblRamStatus);

            lblRamResult.Location = new Point(12, 205);
            lblRamResult.AutoSize = true;
            lblRamResult.Text = "";
            lblRamResult.Font = new Font("Consolas", 8F);
            pnlRam.Controls.Add(lblRamResult);
        }

        // === Summary panel ===
        private TextBox txtSummary = new TextBox();

        private void BuildSummaryPanel()
        {
            var lblTitle = new Label
            {
                Text = "Paso 5: Resumen de la adquisicion",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 8),
                AutoSize = true,
            };
            pnlSummary.Controls.Add(lblTitle);

            txtSummary.Location = new Point(12, 36);
            txtSummary.Size = new Size(880, 320);
            txtSummary.Multiline = true;
            txtSummary.ReadOnly = true;
            txtSummary.ScrollBars = ScrollBars.Both;
            txtSummary.Font = new Font("Consolas", 9F);
            txtSummary.BackColor = Color.White;
            pnlSummary.Controls.Add(txtSummary);
        }

        // === Lifecycle ===

        private void OnLoad()
        {
            Log("[+] CYBOLT_DFIR_TRIAGE v5.5.1 iniciado");
            Log("[+] Hostname: " + Environment.MachineName + " | OS: " + Environment.OSVersion.VersionString);
            DetectEnv();
        }

        private void CheckAdminOrWarn()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(id);
                    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        MessageBox.Show(
                            "Esta aplicacion requiere permisos de Administrador.\n\n" +
                            "Pide a tu administrador local o de dominio que ejecute el .exe con permisos elevados.",
                            "CYBOLT_DFIR_TRIAGE - Permisos",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[!] CheckAdmin: " + ex.Message);
            }
        }

        private void DfirTriageMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                var r = MessageBox.Show("Hay una operacion activa. Cancelar y cerrar?",
                    "CYBOLT_DFIR_TRIAGE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                _cts.Cancel();
            }
        }

        private void DetectEnv()
        {
            Log("[+] Detectando entorno...");

            // Bundle: carpeta del .exe (ZIP descomprimido) o fallback a raiz de disco
            _bundleRoot = FindBundleRoot();
            if (!string.IsNullOrEmpty(_bundleRoot) && File.Exists(Path.Combine(_bundleRoot, "kape.exe")))
                lblDetCdRom.Text = "Bundle (kape.exe): " + _bundleRoot + "  [OK]";
            else
                lblDetCdRom.Text = "Bundle (kape.exe): (no encontrado)  [NO]";

            // Discos de entrada disponibles (preserva la seleccion actual del operador)
            PopulateSourceDrives();

            // Carpeta de salida: solo autodefectamos si el operador no eligio una a mano
            if (string.IsNullOrEmpty(_outputFolder) || !_outputFolderManual)
            {
                SetOutputFolder(DetectDefaultOutputFolder(), false);
            }
            else
            {
                _suppressTextChanged = true;
                try { txtOutputFolder.Text = _outputFolder; }
                finally { _suppressTextChanged = false; }
                lblDetOutputInfo.Text = "(seleccionada manualmente)";
            }

            // RAM
            _totalRamBytes = DetectTotalRam();
            lblDetRam.Text = "RAM fisica: " + HashUtil.FormatBytes(_totalRamBytes);

            UpdateFreeSpaceInfo();
            UpdatePanelInfos();
            UpdateNextEnabled();

            var canProceed = !string.IsNullOrEmpty(_bundleRoot)
                          && !string.IsNullOrEmpty(_outputFolder)
                          && !string.IsNullOrEmpty(_sourceDrive);
            Log("[+] Deteccion completa: " + (canProceed ? "OK para proceder" : "FALTAN elementos"));
        }

        /// <summary>
        /// Llena el ComboBox con los discos fijos y removibles del equipo.
        /// Mantiene C: como default y respeta la seleccion previa del operador.
        /// </summary>
        private void PopulateSourceDrives()
        {
            var previous = _sourceDrive;
            cmbSourceDrive.Items.Clear();

            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                string volLabel;
                bool ready;
                try { volLabel = d.VolumeLabel; ready = d.IsReady; }
                catch { volLabel = ""; ready = false; }

                var root = d.Name.TrimEnd('\\');
                // Solo letra + label del volumen (si hay). No mostramos DriveType
                // ("Fixed"/"Removable"): confunde al operador y no aporta.
                var desc = root + "\\";
                if (!string.IsNullOrEmpty(volLabel)) desc += "  [" + volLabel + "]";
                if (!ready) desc += "  (no listo)";
                cmbSourceDrive.Items.Add(new DriveChoice(root, desc));
            }

            // Default: C: si existe, si no el primer disco fijo
            var target = previous;
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target + "\\"))
            {
                target = Directory.Exists("C:\\") ? "C:" : null;
                foreach (DriveChoice dc in cmbSourceDrive.Items)
                {
                    if (target == null) { target = dc.Root; break; }
                }
            }
            if (target == null)
            {
                _sourceDrive = null;
                cmbSourceDrive.SelectedIndex = -1;
                return;
            }

            _sourceDrive = target;
            for (int i = 0; i < cmbSourceDrive.Items.Count; i++)
            {
                var dc = cmbSourceDrive.Items[i] as DriveChoice;
                if (dc != null && string.Equals(dc.Root, target, StringComparison.OrdinalIgnoreCase))
                {
                    cmbSourceDrive.SelectedIndex = i;
                    return;
                }
            }
            cmbSourceDrive.SelectedIndex = -1;
        }

        /// <summary>
        /// Carpeta de salida por defecto. Prioridad:
        ///   1) Primer disco removible (USB) -> &lt;usb&gt;\DFIR-OUTPUT
        ///   2) Fallback C:\DFIR-OUTPUT
        /// No busca volumes por label: cualquier carpeta writable sirve.
        /// Siempre sobreescribible con el boton Browse o escribiendo a mano.
        /// </summary>
        private string DetectDefaultOutputFolder()
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Removable) continue;
                try
                {
                    if (d.IsReady) return Path.Combine(d.Name.TrimEnd('\\'), "DFIR-OUTPUT");
                }
                catch { }
            }

            return @"C:\DFIR-OUTPUT";
        }

        private void BtnBrowseOutput_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Seleccionar la carpeta donde se guardara la evidencia";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(_outputFolder) && Directory.Exists(_outputFolder))
                    dlg.SelectedPath = _outputFolder;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SetOutputFolder(dlg.SelectedPath, true);
                    Log("[+] Carpeta de salida: " + _outputFolder);
                }
            }
        }

        private void TxtOutputFolder_TextChanged(object sender, EventArgs e)
        {
            if (_suppressTextChanged) return;
            var t = txtOutputFolder.Text.Trim();
            if (!string.IsNullOrEmpty(t) &&
                !string.Equals(t, _outputFolder, StringComparison.OrdinalIgnoreCase))
            {
                SetOutputFolder(t, true);
            }
        }

        private void SetOutputFolder(string path, bool manual)
        {
            _outputFolder = path;
            _outputFolderManual = manual;

            _suppressTextChanged = true;
            try { txtOutputFolder.Text = path ?? ""; }
            finally { _suppressTextChanged = false; }

            lblDetOutputInfo.Text = manual
                ? "(seleccionada manualmente)"
                : "(detectada automaticamente - Browse para cambiar)";

            UpdateFreeSpaceInfo();
            UpdatePanelInfos();
            UpdateNextEnabled();
        }

        private void UpdateFreeSpaceInfo()
        {
            if (string.IsNullOrEmpty(_outputFolder))
            {
                lblDetFree.Text = "Espacio libre: -";
                return;
            }
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(_outputFolder));
                _freeSpaceBytes = new DriveInfo(root).AvailableFreeSpace;
                lblDetFree.Text = "Espacio libre en " + root + ": " + HashUtil.FormatBytes(_freeSpaceBytes);
            }
            catch (Exception ex)
            {
                lblDetFree.Text = "Espacio libre: error - " + ex.Message;
            }
        }

        /// <summary>
        /// Refresca los textos de los paneles de KAPE y RAM para que reflejen
        /// el disco de entrada y la carpeta de salida elegidos por el operador.
        /// </summary>
        private void UpdatePanelInfos()
        {
            var src = string.IsNullOrEmpty(_sourceDrive) ? "(sin elegir)" : _sourceDrive + "\\";
            var dst = string.IsNullOrEmpty(_outputFolder) ? "(sin elegir)" : _outputFolder;

            lblKapeInfo.Text = "KAPE 1.3.0.2 con el set de targets del bundle (targets-full.txt).\n" +
                               "Stage: copia kape.exe + Targets/ a la carpeta de salida (disco writable).\n" +
                               "Entrada: " + src + "   ->   Salida: " + dst + "\\<HOSTNAME>-KAPE-<timestamp>.zip";

            lblRamInfo.Text = "winpmem_mini_x64_rc2.exe (Velocidex WinPmem v4.0.rc1).\n" +
                              "Salida: " + dst + "\\RAM-<HOSTNAME>-<timestamp>.raw\n" +
                              "Tiempo estimado: 5-30 min dependiendo de la RAM fisica.\n" +
                              "Este paso es opt-in (checkbox en la pantalla de Bienvenida, default OFF).\n" +
                              "Podes volver a Bienvenida con < Back para activar/desactivar la captura.";
        }

        /// <summary>
        /// Habilita Next solo si los parametros de entrada/salida son validos
        /// (en el paso 2 de Deteccion).
        /// </summary>
        private void UpdateNextEnabled()
        {
            if (_currentStep == 1)
            {
                var inputsOk = !string.IsNullOrEmpty(_bundleRoot)
                            && !string.IsNullOrEmpty(_outputFolder)
                            && !string.IsNullOrEmpty(_sourceDrive);
                btnNext.Enabled = inputsOk;
            }
            else
            {
                btnNext.Enabled = _currentStep < 4;
            }
        }

        /// <summary>
        /// Localiza el bundle (carpeta con kape.exe + Targets/).
        /// 1) Carpeta del .exe (caso normal: ZIP descomprimido en USB/Downloads)
        /// 2) Raiz de cualquier disco con kape.exe (ISO/USB montado en raiz)
        /// </summary>
        private string FindBundleRoot()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir) && File.Exists(Path.Combine(exeDir, "kape.exe")))
                    return exeDir.TrimEnd('\\');
            }
            catch { }

            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    var kapePath = Path.Combine(d.Name, "kape.exe");
                    if (File.Exists(kapePath)) return d.Name.TrimEnd('\\');
                }
                catch { }
            }
            return null;
        }

        private static long DetectTotalRam()
        {
            long total = 0;
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                foreach (var obj in searcher.Get())
                {
                    var cap = obj["Capacity"];
                    if (cap != null) total += Convert.ToInt64(cap);
                }
            }
            catch { }
            if (total == 0) total = 4L * 1024 * 1024 * 1024;
            return total;
        }

        // === Navigation ===

        private void BtnBack_Click(object sender, EventArgs e)
        {
            if (_currentStep > 0)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    MessageBox.Show("Operacion en curso. Cancelala primero.", "CYBOLT_DFIR_TRIAGE");
                    return;
                }
                ShowStep(_currentStep - 1);
            }
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            switch (_currentStep)
            {
                case 0: // Welcome -> Detect
                    _runRamAfterKape = chkRunRam.Checked;
                    _useCmdWrapper = chkCmdWrapper.Checked;
                    ShowStep(1);
                    break;
                case 1: // Detect -> Kape
                    ShowStep(2);
                    break;
                case 2: // Kape -> Ram (if enabled) or Summary
                    if (_kapeResult == null)
                    {
                        MessageBox.Show("Ejecuta KapeTriage primero (boton ▶ Ejecutar).",
                            "CYBOLT_DFIR_TRIAGE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    ShowStep(_runRamAfterKape ? 3 : 4);
                    break;
                case 3: // Ram -> Summary
                    ShowStep(4);
                    break;
                case 4: // Summary -> Close (handled by btnClose)
                    break;
            }
        }

        private void ShowStep(int n)
        {
            _currentStep = n;
            pnlWelcome.Visible = (n == 0);
            pnlDetect.Visible = (n == 1);
            pnlKape.Visible = (n == 2);
            pnlRam.Visible = (n == 3);
            pnlSummary.Visible = (n == 4);
            string[] titles = {
                "Paso 1/5: Bienvenida",
                "Paso 2/5: Deteccion del entorno",
                "Paso 3/5: KapeTriage",
                "Paso 4/5: RAM Capture",
                "Paso 5/5: Resumen"
            };
            lblStep.Text = titles[n];
            btnBack.Enabled = (n > 0);
            btnNext.Text = (n == 4) ? "Done" : "Next >";
            UpdateNextEnabled();

            if (n == 4)
            {
                BuildSummary();
            }
        }

        // === Kape run ===

        private async void BtnRunKape_Click(object sender, EventArgs e)
        {
            if (_bundleRoot == null || string.IsNullOrEmpty(_outputFolder) || string.IsNullOrEmpty(_sourceDrive))
            {
                MessageBox.Show("No se encontro el bundle (kape.exe junto al .exe), o falta elegir\n" +
                                "disco de entrada o carpeta de salida. Volve a 'Deteccion'.",
                    "CYBOLT_DFIR_TRIAGE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            btnRunKape.Enabled = false;
            btnBack.Enabled = false;
            btnCancel.Enabled = true;
            btnNext.Enabled = false;
            _cts = new CancellationTokenSource();
            pbKape.Style = ProgressBarStyle.Marquee;
            lblKapeStatus.Text = "Estado: corriendo KapeTriage...";

            var runner = new KapeRunner(Log, _useCmdWrapper);
            try
            {
                _kapeResult = await runner.RunAsync(_bundleRoot, _outputFolder, _sourceDrive, _cts.Token);
                lblKapeResult.Text = "ZIP: " + _kapeResult.ZipPath + "\n" +
                                    "Tamano: " + HashUtil.FormatBytes(_kapeResult.ZipSize) + "\n" +
                                    "SHA-256: " + _kapeResult.Sha256 + "\n" +
                                    "Tiempo: " + _kapeResult.Duration.ToString(@"mm\:ss");
                lblKapeStatus.Text = "Estado: OK (" + _kapeResult.Duration.ToString(@"mm\:ss") + ")";
                btnNext.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                lblKapeStatus.Text = "Estado: CANCELADO";
                lblKapeResult.Text = "KapeTriage cancelado por el usuario. Outputs parciales quedan en " +
                                     _outputFolder + "\\.";
            }
            catch (Exception ex)
            {
                lblKapeStatus.Text = "Estado: ERROR";
                lblKapeResult.Text = "ERROR: " + ex.Message;
                Log("[!] KapeTriage fallo: " + ex.Message);
            }
            finally
            {
                pbKape.Style = ProgressBarStyle.Blocks;
                pbKape.Value = 100;
                btnRunKape.Enabled = true;
                btnBack.Enabled = (_currentStep > 0);
                btnCancel.Enabled = false;
                if (_cts != null) { _cts.Dispose(); _cts = null; }
            }
        }

        // === RAM run ===

        private async void BtnRunRam_Click(object sender, EventArgs e)
        {
            if (_bundleRoot == null || string.IsNullOrEmpty(_outputFolder))
            {
                MessageBox.Show("No se encontro el bundle (kape.exe junto al .exe) o falta\n" +
                                "elegir la carpeta de salida. Volve a 'Deteccion'.",
                    "CYBOLT_DFIR_TRIAGE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            btnRunRam.Enabled = false;
            btnBack.Enabled = false;
            btnCancel.Enabled = true;
            btnNext.Enabled = false;
            _cts = new CancellationTokenSource();
            pbRam.Style = ProgressBarStyle.Marquee;
            lblRamStatus.Text = "Estado: capturando RAM...";

            var runner = new RamRunner(Log, _useCmdWrapper);
            try
            {
                _ramResult = await runner.RunAsync(_bundleRoot, _outputFolder, _cts.Token);
                lblRamResult.Text = "RAW: " + _ramResult.RawPath + "\n" +
                                    "Tamano: " + HashUtil.FormatBytes(_ramResult.RawSize) + "\n" +
                                    "SHA-256: " + _ramResult.Sha256 + "\n" +
                                    "Tiempo: " + _ramResult.Duration.ToString(@"mm\:ss") +
                                    (_ramResult.SuccessDespiteExitCode ? " (exit != 0 pero >= 95% RAM)" : "");
                lblRamStatus.Text = "Estado: OK";
                btnNext.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                lblRamStatus.Text = "Estado: CANCELADO";
                lblRamResult.Text = "RAM cancelado. .raw parcial queda en " + _outputFolder +
                                    "\\ (borrar si no sirve).";
            }
            catch (Exception ex)
            {
                lblRamStatus.Text = "Estado: ERROR";
                lblRamResult.Text = "ERROR: " + ex.Message;
                Log("[!] RAM fallo: " + ex.Message);
            }
            finally
            {
                pbRam.Style = ProgressBarStyle.Blocks;
                pbRam.Value = 100;
                btnRunRam.Enabled = true;
                btnBack.Enabled = (_currentStep > 0);
                btnCancel.Enabled = false;
                if (_cts != null) { _cts.Dispose(); _cts = null; }
            }
        }

        // === Summary ===

        private void BuildSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CYBOLT DFIR Triage - Resumen de la adquisicion");
            sb.AppendLine("============================================");
            sb.AppendLine();
            sb.AppendLine("Hostname: " + Environment.MachineName);
            sb.AppendLine("Fecha:    " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("OS:       " + Environment.OSVersion.VersionString);
            sb.AppendLine();
            sb.AppendLine("Bundle:   " + _bundleRoot);
            sb.AppendLine("Entrada:  " + _sourceDrive + "\\");
            sb.AppendLine("Salida:   " + _outputFolder);
            sb.AppendLine("RAM:      " + HashUtil.FormatBytes(_totalRamBytes));
            sb.AppendLine();

            sb.AppendLine("--- KapeTriage ---");
            if (_kapeResult != null)
            {
                sb.AppendLine("ZIP:      " + _kapeResult.ZipPath);
                sb.AppendLine("Tamano:   " + HashUtil.FormatBytes(_kapeResult.ZipSize));
                sb.AppendLine("SHA-256:  " + _kapeResult.Sha256);
                sb.AppendLine("Tiempo:   " + _kapeResult.Duration.ToString(@"mm\:ss"));
            }
            else
            {
                sb.AppendLine("(no ejecutado o fallido)");
            }
            sb.AppendLine();

            sb.AppendLine("--- RAM Capture ---");
            if (_ramResult != null)
            {
                sb.AppendLine("RAW:      " + _ramResult.RawPath);
                sb.AppendLine("Tamano:   " + HashUtil.FormatBytes(_ramResult.RawSize));
                sb.AppendLine("SHA-256:  " + _ramResult.Sha256);
                sb.AppendLine("Tiempo:   " + _ramResult.Duration.ToString(@"mm\:ss"));
                if (_ramResult.SuccessDespiteExitCode) sb.AppendLine("Nota:     exit != 0 pero .raw >= 95% RAM (valido)");
            }
            else if (_runRamAfterKape)
            {
                sb.AppendLine("(no ejecutado o fallido)");
            }
            else
            {
                sb.AppendLine("(omitido por el usuario)");
            }
            sb.AppendLine();

            sb.AppendLine("--- Proximos pasos ---");
            sb.AppendLine("1. Apagar el equipo (Shutdown limpio) sin desconectar el medio de salida.");
            sb.AppendLine("2. Retirar el medio con la evidencia: " + _outputFolder);
            sb.AppendLine("3. Conectarlo a un equipo con red para la subida.");
            sb.AppendLine("4. Correr DFIR_UPLOADER_UI.exe ahi para subir la evidencia");
            sb.AppendLine("   a S3/FTP/SFTP/SMB segun el config que te paso el operador.");
            sb.AppendLine();
            sb.AppendLine("Log completo: ver textbox de abajo o click Close y abrir el");
            sb.AppendLine("archivo de log si lo guardaste.");

            txtSummary.Text = sb.ToString();
        }

        // === Cancel ===

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                Log("[!] Cancel solicitado por usuario");
                _cts.Cancel();
            }
        }

        // === Log ===

        public void Log(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(msg)));
                return;
            }
            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
            txtLog.AppendText(line + Environment.NewLine);
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
    }
}