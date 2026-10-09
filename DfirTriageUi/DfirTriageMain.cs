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
    ///   2. Deteccion (CD-ROM, output drive, RAM, hostname)
    ///   3. KapeTriage (run KAPE con stage desde CD-ROM a disco de salida)
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
        private string _cdRomDrive = "D:";
        private string _outputDrive = "E:";
        private string _sourceDrive = "C:";
        private long _totalRamBytes;
        private long _freeSpaceBytes;

        // Kape result
        private KapeRunner.Result _kapeResult;
        // RAM result
        private RamRunner.Result _ramResult;
        // Options
        private bool _runRamAfterKape = true;
        private bool _useCmdWrapper = false;

        public DfirTriageMain()
        {
            Text = "CYBOLT DFIR Triage Wizard v5.3.0 - By Cybolt";
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

            lblFooter.Text = "CYBOLT_DFIR_TRIAGE.exe v5.3.0 - By Cybolt, MIT License";
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
                "Este wizard realiza la adquisicion DFIR en dos pasos:\n\n" +
                "  1. KapeTriage  -  copia evidencia de C: a E:\\DFIR-OUTPUT\\ (ZIP)\n" +
                "  2. RAM Capture -  captura RAM con winpmem (opcional, archivo .raw)\n\n" +
                "Requisitos:\n" +
                "  - ISO montada como CD-ROM (auto-detectado)\n" +
                "  - Disco de salida con label DFIR_OUTPUT (auto-detectado)\n" +
                "  - Privilegios de Administrador (UAC forzado)\n" +
                "  - Sin red durante la captura (la subida va aparte con DFIR_UPLOADER_UI)\n\n" +
                "Si alguno de los pasos falla o se omite, los outputs parciales quedan\n" +
                "en E:\\DFIR-OUTPUT\\ y pueden entregarse igual al analista.\n";
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
            chkRunRam.Text = "Capturar RAM despues de KapeTriage (recomendado)";
            chkRunRam.Checked = true;
            pnlWelcome.Controls.Add(chkRunRam);

            chkCmdWrapper.Location = new Point(12, 275);
            chkCmdWrapper.AutoSize = true;
            chkCmdWrapper.Text = "Lanzar via cmd.exe wrapper (evita spam de Console Title en servers)";
            chkCmdWrapper.Checked = false;
            pnlWelcome.Controls.Add(chkCmdWrapper);
        }

        // === Detect panel ===
        private Label lblDetCdRom = new Label();
        private Label lblDetOutput = new Label();
        private Label lblDetSource = new Label();
        private Label lblDetRam = new Label();
        private Label lblDetFree = new Label();
        private Button btnDetRefresh = new Button();

        private void BuildDetectPanel()
        {
            var lblTitle = new Label
            {
                Text = "Deteccion del entorno",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(12, 8),
                AutoSize = true,
            };
            pnlDetect.Controls.Add(lblTitle);

            int y = 40;
            lblDetCdRom.Location = new Point(12, y);
            lblDetCdRom.AutoSize = true;
            lblDetCdRom.Text = "CD-ROM (ISO): detectando...";
            pnlDetect.Controls.Add(lblDetCdRom);
            y += 26;

            lblDetOutput.Location = new Point(12, y);
            lblDetOutput.AutoSize = true;
            lblDetOutput.Text = "Disco de salida (DFIR_OUTPUT): detectando...";
            pnlDetect.Controls.Add(lblDetOutput);
            y += 26;

            lblDetSource.Location = new Point(12, y);
            lblDetSource.AutoSize = true;
            lblDetSource.Text = "Disco fuente (C:): detectando...";
            pnlDetect.Controls.Add(lblDetSource);
            y += 26;

            lblDetRam.Location = new Point(12, y);
            lblDetRam.AutoSize = true;
            lblDetRam.Text = "RAM fisica: detectando...";
            pnlDetect.Controls.Add(lblDetRam);
            y += 26;

            lblDetFree.Location = new Point(12, y);
            lblDetFree.AutoSize = true;
            lblDetFree.Text = "Espacio libre en disco de salida: detectando...";
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
            lblKapeInfo.Text = "KapeTriage ejecuta KAPE 1.3.0.2 con el target predefinido.\n" +
                              "Stage: copia kape.exe + Targets/ desde el CD-ROM al disco de salida (E:\\).\n" +
                              "Output: ZIP en E:\\DFIR-OUTPUT\\<HOSTNAME>-KAPE-<timestamp>.zip";
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
                              "Output: .raw en E:\\DFIR-OUTPUT\\RAM-<HOSTNAME>-<timestamp>.raw\n" +
                              "Tiempo estimado: 5-30 min dependiendo de la RAM fisica.\n" +
                              "Si la VM tiene poca RAM (< 8 GB), podes saltear este paso.";
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
            Log("[+] CYBOLT_DFIR_TRIAGE v5.3.0 iniciado");
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

            // CD-ROM
            _cdRomDrive = FindCdRomWithKape();
            lblDetCdRom.Text = "CD-ROM (ISO): " + (_cdRomDrive ?? "(no encontrado)") +
                                (_cdRomDrive != null && File.Exists(_cdRomDrive + "\\kape.exe") ? " [kape.exe OK]" : " [kape.exe NO]");

            // Output drive (label DFIR_OUTPUT)
            _outputDrive = FindDriveByLabel("DFIR_OUTPUT");
            lblDetOutput.Text = "Disco de salida (DFIR_OUTPUT): " + (_outputDrive ?? "(no encontrado)");

            // Source drive (default C:)
            _sourceDrive = "C:";
            if (Directory.Exists("C:\\"))
            {
                lblDetSource.Text = "Disco fuente: C:\\ (default, OK)";
            }
            else
            {
                lblDetSource.Text = "Disco fuente: C:\\ NO EXISTE";
                _sourceDrive = null;
            }

            // RAM
            _totalRamBytes = DetectTotalRam();
            lblDetRam.Text = "RAM fisica: " + HashUtil.FormatBytes(_totalRamBytes);

            // Free space
            if (_outputDrive != null)
            {
                try
                {
                    _freeSpaceBytes = new DriveInfo(_outputDrive).AvailableFreeSpace;
                    lblDetFree.Text = "Espacio libre en " + _outputDrive + ": " + HashUtil.FormatBytes(_freeSpaceBytes);
                }
                catch (Exception ex)
                {
                    lblDetFree.Text = "Espacio libre: error - " + ex.Message;
                }
            }
            else
            {
                lblDetFree.Text = "Espacio libre: -";
            }

            // Enable Next if everything detected
            var canProceed = !string.IsNullOrEmpty(_cdRomDrive)
                          && !string.IsNullOrEmpty(_outputDrive)
                          && !string.IsNullOrEmpty(_sourceDrive);
            btnNext.Enabled = canProceed || _currentStep != 1; // always enabled except first
            Log("[+] Deteccion completa: " + (canProceed ? "OK para proceder" : "FALTAN elementos"));
        }

        private string FindCdRomWithKape()
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.CDRom) continue;
                var kapePath = Path.Combine(d.Name, "kape.exe");
                if (File.Exists(kapePath)) return d.Name.TrimEnd('\\');
            }
            // Fallback: any drive with kape.exe
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    var kapePath = Path.Combine(d.Name, "kape.exe");
                    if (File.Exists(kapePath)) return d.Name.TrimEnd('\\');
                }
                catch { }
            }
            return null;
        }

        private string FindDriveByLabel(string label)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.IsReady && string.Equals(d.VolumeLabel, label, StringComparison.OrdinalIgnoreCase))
                    {
                        return d.Name.TrimEnd('\\');
                    }
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
            btnNext.Enabled = (n < 4);
            btnNext.Text = (n == 4) ? "Done" : "Next >";

            if (n == 4)
            {
                BuildSummary();
            }
        }

        // === Kape run ===

        private async void BtnRunKape_Click(object sender, EventArgs e)
        {
            if (_cdRomDrive == null || _outputDrive == null)
            {
                MessageBox.Show("CD-ROM o disco de salida no detectados. Volve a 'Deteccion' y reintentá.",
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
                _kapeResult = await runner.RunAsync(_cdRomDrive, _outputDrive, _sourceDrive, _cts.Token);
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
                lblKapeResult.Text = "KapeTriage cancelado por el usuario. Outputs parciales quedan en E:\\DFIR-OUTPUT\\.";
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
            if (_cdRomDrive == null || _outputDrive == null)
            {
                MessageBox.Show("CD-ROM o disco de salida no detectados.",
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
                _ramResult = await runner.RunAsync(_cdRomDrive, _outputDrive, _cts.Token);
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
                lblRamResult.Text = "RAM cancelado. .raw parcial queda en E:\\DFIR-OUTPUT\\ (borrar si no sirve).";
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
            sb.AppendLine("CD-ROM:   " + _cdRomDrive);
            sb.AppendLine("Output:   " + _outputDrive + " (label DFIR_OUTPUT)");
            sb.AppendLine("Source:   " + _sourceDrive);
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
            sb.AppendLine("1. Apagar la VM (Shutdown limpio).");
            sb.AppendLine("2. En VMware/vSphere: detach DFIR-OUTPUT.vmdk de la VM objetivo.");
            sb.AppendLine("3. Attach el VMDK a una VM con internet.");
            sb.AppendLine("4. Correr DFIR_UPLOADER_UI.exe en esa VM para subir la evidencia");
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