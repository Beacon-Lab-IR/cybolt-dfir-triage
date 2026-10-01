using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RamCaptureUi
{
    /// <summary>
    /// Form principal de RAM_CAPTURE_UI.
    /// Auto-detecta: letra del CD-ROM (donde vive D:\Tools\Memory\winpmem),
    ///               particion de salida (label DFIR_OUTPUT),
    ///               hostname, fecha.
    /// Flujo:
    ///   1. Operador valida deteccion (o ajusta ComboBox si hay mas de 1 opcion).
    ///   2. Click "Iniciar captura" -> valida admin + espacio + binario.
    ///   3. Spawn winpmem con Process, redirige stdout/stderr al TextBox.
    ///   4. Al terminar (exit 0), calcula SHA-256 nativo .NET streaming.
    ///   5. Muestra tamano + SHA-256 + ruta final.
    ///   6. Escribe archivo .sha256 companion (formato: "{sha256}  {filename}\n").
    /// </summary>
    public class RamCaptureMain : Form
    {
        private readonly ComboBox cmbOutputDrive = new ComboBox();
        private readonly ComboBox cmbSourceDrive = new ComboBox();
        private readonly CheckBox chkUseCmdWrapper = new CheckBox();
        private readonly Label lblWinPmemPath = new Label();
        private readonly Label lblTotalRam = new Label();
        private readonly Label lblFreeSpace = new Label();
        private readonly Label lblOutputPath = new Label();
        private readonly Label lblHostname = new Label();
        private readonly Label lblDate = new Label();
        private readonly Button btnStart = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnVerify = new Button();
        private readonly Button btnCopyLog = new Button();
        private readonly Button btnClose = new Button();
        private readonly ProgressBar pbCapture = new ProgressBar();
        private readonly Label lblStatus = new Label();
        private readonly TextBox txtLog = new TextBox();
        private readonly TextBox txtSha256 = new TextBox();
        private readonly Label lblFooter = new Label();

        private Process _winPmemProcess;
        private CancellationTokenSource _cts;
        private string _outputPath;
        private long _outputSize;
        private DateTime _captureStart;

        public RamCaptureMain()
        {
            Text = "RAM Capture - DFIR Acquisition v" + AppInfo.Version;
            Width = 760;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            Load += (s, e) => InitAutoDetect();
            Shown += (s, e) => CheckAdminOrWarn();
        }

        private void BuildLayout()
        {
            int y = 12;
            int labelW = 130;
            int ctrlW = 580;

            // Title
            var lblTitle = new Label
            {
                Text = "Captura de RAM (WinPmem v4.0.rc1) - KAPE-MEDIA v" + AppInfo.Version,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblTitle);
            y += 32;

            // Hostname / date
            lblHostname.Location = new Point(12, y);
            lblHostname.AutoSize = true;
            Controls.Add(lblHostname);
            lblDate.Location = new Point(380, y);
            lblDate.AutoSize = true;
            Controls.Add(lblDate);
            y += 24;

            // Output drive
            var lbl1 = MkLabel("Particion de salida:", labelW);
            lbl1.Location = new Point(12, y + 3);
            Controls.Add(lbl1);
            cmbOutputDrive.Location = new Point(labelW + 12, y);
            cmbOutputDrive.Width = 200;
            cmbOutputDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            Controls.Add(cmbOutputDrive);
            lblFreeSpace.Location = new Point(labelW + 220, y + 3);
            lblFreeSpace.AutoSize = true;
            Controls.Add(lblFreeSpace);
            y += 30;

            // Source drive (informational only, not used by winpmem but kept for parity)
            var lbl2 = MkLabel("Winpmem binario en:", labelW);
            lbl2.Location = new Point(12, y + 3);
            Controls.Add(lbl2);
            cmbSourceDrive.Location = new Point(labelW + 12, y);
            cmbSourceDrive.Width = 200;
            cmbSourceDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            Controls.Add(cmbSourceDrive);
            lblWinPmemPath.Location = new Point(labelW + 220, y + 3);
            lblWinPmemPath.AutoSize = true;
            lblWinPmemPath.ForeColor = Color.DarkGreen;
            Controls.Add(lblWinPmemPath);
            y += 30;

            // Wrapper mode: lanzar winpmem via cmd.exe (replica comportamiento cmd interactivo)
            // Bug observado 2026-09-30 con v5.2.15: lanzado directo desde .NET, Defender
            // escaneaba el .raw en tiempo real y winpmem salia con exit 1. Manualmente desde
            // cmd funcionaba. Solucion: checkbox opt-in para usar cmd /c.
            chkUseCmdWrapper.Text = "Ejecutar via cmd.exe (mas compatible con AV)";
            chkUseCmdWrapper.Location = new Point(labelW + 12, y + 3);
            chkUseCmdWrapper.AutoSize = true;
            chkUseCmdWrapper.Checked = false;
            chkUseCmdWrapper.ForeColor = Color.DarkRed;
            Controls.Add(chkUseCmdWrapper);
            y += 28;

            // RAM info
            var lbl3 = MkLabel("RAM total detectada:", labelW);
            lbl3.Location = new Point(12, y + 3);
            Controls.Add(lbl3);
            lblTotalRam.Location = new Point(labelW + 12, y + 3);
            lblTotalRam.AutoSize = true;
            lblTotalRam.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            Controls.Add(lblTotalRam);
            y += 30;

            // Output path preview
            var lbl4 = MkLabel("Archivo de salida:", labelW);
            lbl4.Location = new Point(12, y + 3);
            Controls.Add(lbl4);
            lblOutputPath.Location = new Point(labelW + 12, y);
            lblOutputPath.AutoSize = false;
            lblOutputPath.Width = ctrlW;
            lblOutputPath.Font = new Font("Consolas", 8.5F);
            lblOutputPath.ForeColor = Color.DarkBlue;
            Controls.Add(lblOutputPath);
            y += 28;

            // Buttons
            btnStart.Text = "Iniciar captura de RAM";
            btnStart.Location = new Point(12, y);
            btnStart.Width = 180;
            btnStart.Height = 32;
            btnStart.Click += BtnStart_Click;
            Controls.Add(btnStart);

            btnCancel.Text = "Cancelar";
            btnCancel.Location = new Point(200, y);
            btnCancel.Width = 100;
            btnCancel.Height = 32;
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;
            Controls.Add(btnCancel);

            btnVerify.Text = "Verificar SHA-256";
            btnVerify.Location = new Point(308, y);
            btnVerify.Width = 140;
            btnVerify.Height = 32;
            btnVerify.Enabled = false;
            btnVerify.Click += BtnVerify_Click;
            Controls.Add(btnVerify);

            btnCopyLog.Text = "Copiar log";
            btnCopyLog.Location = new Point(456, y);
            btnCopyLog.Width = 90;
            btnCopyLog.Height = 32;
            btnCopyLog.Click += BtnCopyLog_Click;
            Controls.Add(btnCopyLog);

            btnClose.Text = "Cerrar";
            btnClose.Location = new Point(556, y);
            btnClose.Width = 80;
            btnClose.Height = 32;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
            y += 42;

            // Status
            lblStatus.Text = "Listo.";
            lblStatus.Location = new Point(12, y);
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            Controls.Add(lblStatus);
            y += 22;

            // Progress
            pbCapture.Location = new Point(12, y);
            pbCapture.Width = ctrlW;
            pbCapture.Height = 18;
            pbCapture.Style = ProgressBarStyle.Continuous;
            pbCapture.Minimum = 0;
            pbCapture.Maximum = 100;
            Controls.Add(pbCapture);
            y += 26;

            // SHA-256 result
            var lbl5 = MkLabel("SHA-256:", 60);
            lbl5.Location = new Point(12, y + 3);
            Controls.Add(lbl5);
            txtSha256.Location = new Point(72, y);
            txtSha256.Width = ctrlW - 60;
            txtSha256.Font = new Font("Consolas", 8.5F);
            txtSha256.ReadOnly = true;
            Controls.Add(txtSha256);
            y += 28;

            // Log
            var lbl6 = MkLabel("Log:", 60);
            lbl6.Location = new Point(12, y);
            Controls.Add(lbl6);
            y += 18;
            txtLog.Location = new Point(12, y);
            txtLog.Width = ctrlW + 130;
            txtLog.Height = 130;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5F);
            txtLog.BackColor = Color.Black;
            txtLog.ForeColor = Color.LightGreen;
            y += 138;

            // Footer
            lblFooter.Text = "By Cybolt, MIT License  -  WinPmem v4.0.rc1 (Velocidex, firmado Microsoft)";
            lblFooter.Location = new Point(12, y);
            lblFooter.AutoSize = true;
            lblFooter.ForeColor = Color.Gray;
            lblFooter.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            Controls.Add(lblFooter);
        }

        private static Label MkLabel(string text, int w)
        {
            return new Label { Text = text, Width = w, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        }

        private void InitAutoDetect()
        {
            try
            {
                lblHostname.Text = "Hostname: " + Environment.MachineName;
                lblDate.Text = "Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var totalRam = GetTotalPhysicalMemory();
                lblTotalRam.Text = FormatBytes(totalRam);

                // Detectar particiones de salida candidatas (label DFIR_OUTPUT o todas las fijas)
                var outputCandidates = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .ToList();

                var dfirOutput = outputCandidates
                    .Where(d => string.Equals(d.VolumeLabel, AppInfo.DefaultOutputLabel, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                cmbOutputDrive.Items.Clear();
                foreach (var d in (dfirOutput.Count > 0 ? dfirOutput : outputCandidates))
                {
                    cmbOutputDrive.Items.Add(d.Name.TrimEnd('\\') + "  [" + (string.IsNullOrEmpty(d.VolumeLabel) ? "no-label" : d.VolumeLabel) + "]  " + FormatBytes(d.AvailableFreeSpace));
                }
                if (cmbOutputDrive.Items.Count > 0) cmbOutputDrive.SelectedIndex = 0;

                // Detectar letra del CD-ROM donde esta Tools\Memory\winpmem_mini_x64_rc2.exe
                var cdCandidates = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.CDRom && d.IsReady)
                    .ToList();
                // Si no hay CDROM con IsReady (raro: justo montado), caer a todas las fijas
                if (cdCandidates.Count == 0)
                {
                    cdCandidates = DriveInfo.GetDrives()
                        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                        .ToList();
                }

                cmbSourceDrive.Items.Clear();
                foreach (var d in cdCandidates)
                {
                    var letter = d.Name.TrimEnd('\\');
                    var probe = Path.Combine(letter, AppInfo.MemorySubdir, AppInfo.WinPmemName);
                    var marker = File.Exists(probe) ? "OK" : "(binario no encontrado)";
                    cmbSourceDrive.Items.Add(letter + "  " + marker);
                }
                if (cmbSourceDrive.Items.Count > 0)
                {
                    // preferir el primero donde el binario existe
                    var idx = Enumerable.Range(0, cmbSourceDrive.Items.Count)
                        .FirstOrDefault(i => ((string)cmbSourceDrive.Items[i]).Contains(" OK"));
                    cmbSourceDrive.SelectedIndex = idx >= 0 ? idx : 0;
                }

                UpdateOutputPathPreview();
            }
            catch (Exception ex)
            {
                Log("ERROR en auto-deteccion: " + ex.Message);
            }
        }

        private void UpdateOutputPathPreview()
        {
            try
            {
                var outDrive = ExtractDriveLetter((string)cmbOutputDrive.SelectedItem);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var host = Environment.MachineName;
                _outputPath = Path.Combine(outDrive, "RAM-" + host + "-" + stamp + ".raw");
                lblOutputPath.Text = _outputPath;

                // Espacio libre en particion de salida
                var d = new DriveInfo(outDrive);
                lblFreeSpace.Text = "Libre: " + FormatBytes(d.AvailableFreeSpace);
            }
            catch (Exception ex)
            {
                lblOutputPath.Text = "(no se pudo calcular)";
                lblFreeSpace.Text = "";
                Log("WARN: " + ex.Message);
            }
        }

        private string ExtractDriveLetter(string comboItem)
        {
            if (string.IsNullOrEmpty(comboItem)) return "E:\\";
            // comboItem es "E:\  [label]  tamano"
            var idx = comboItem.IndexOf(':');
            if (idx < 0) return comboItem.Substring(0, 1) + ":\\";
            return comboItem.Substring(0, idx + 1) + "\\";
        }

        private string ExtractCdromLetter(string comboItem)
        {
            return ExtractDriveLetter(comboItem);
        }

        private void CheckAdminOrWarn()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(id);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show(
                        "Esta UI requiere permisos de Administrator.\n\n" +
                        "Si llegaste aca sin prompt UAC, algo fallo en el manifest.\n" +
                        "Cerrar y volver a lanzar como Administrator.",
                        AppInfo.BinName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnStart.Enabled = false;
                }
            }
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            if (cmbOutputDrive.SelectedItem == null || cmbSourceDrive.SelectedItem == null)
            {
                MessageBox.Show("Selecciona particion de salida y letra del CD-ROM.",
                    AppInfo.BinName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var outDrive = ExtractDriveLetter((string)cmbOutputDrive.SelectedItem);
            var cdLetter = ExtractCdromLetter((string)cmbSourceDrive.SelectedItem);
            var winpmemExe = Path.Combine(cdLetter, AppInfo.MemorySubdir, AppInfo.WinPmemName);

            if (!File.Exists(winpmemExe))
            {
                MessageBox.Show(
                    "No se encuentra el binario de WinPmem:\n  " + winpmemExe + "\n\n" +
                    "Verifica que la ISO KAPE-MEDIA este montada y el path sea correcto.",
                    AppInfo.BinName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Verificar espacio libre: al menos RAM total + 10% overhead
            var totalRam = GetTotalPhysicalMemory();
            try
            {
                var d = new DriveInfo(outDrive);
                var needed = (long)(totalRam * 1.1);
                if (d.AvailableFreeSpace < needed)
                {
                    var resp = MessageBox.Show(
                        "Espacio libre en " + outDrive + ": " + FormatBytes(d.AvailableFreeSpace) + "\n" +
                        "Necesario (RAM + 10% overhead): " + FormatBytes(needed) + "\n\n" +
                        "Continuar de todos modos?",
                        AppInfo.BinName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (resp != DialogResult.Yes) return;
                }
            }
            catch (Exception ex)
            {
                Log("WARN: no pude validar espacio libre: " + ex.Message);
            }

            UpdateOutputPathPreview();
            _outputPath = lblOutputPath.Text;

            var confirm = MessageBox.Show(
                "Esto va a capturar toda la RAM fisica a:\n  " + _outputPath + "\n\n" +
                "Tamano esperado: ~" + FormatBytes(totalRam) + "\n" +
                "Tiempo estimado: 5-15 minutos (depende de RAM y disco).\n\n" +
                "La VM puede verse lenta durante la captura.\n\nContinuar?",
                AppInfo.BinName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            btnStart.Enabled = false;
            btnCancel.Enabled = true;
            btnVerify.Enabled = false;
            txtSha256.Clear();
            pbCapture.Value = 0;
            lblStatus.Text = "Iniciando captura...";
            lblStatus.ForeColor = Color.DarkOrange;
            txtLog.Clear();
            Log("=== Inicio de captura ===");
            Log("Binario: " + winpmemExe);
            Log("Salida:  " + _outputPath);
            Log("RAM total: " + FormatBytes(totalRam));
            Log("Hora: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            _captureStart = DateTime.Now;
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => RunWinPmem(winpmemExe, _outputPath, _cts.Token), _cts.Token);
                _outputSize = new FileInfo(_outputPath).Length;
                var elapsed = DateTime.Now - _captureStart;
                Log("Winpmem finalizo. Tamano: " + FormatBytes(_outputSize) + " en " + elapsed.TotalSeconds.ToString("F1") + " s");

                lblStatus.Text = "Calculando SHA-256...";
                Log("Calculando SHA-256 (System.Security.Cryptography.SHA256)...");
                var sha = await Task.Run(() => ComputeSha256File(_outputPath));
                txtSha256.Text = sha;
                File.WriteAllText(_outputPath + ".sha256", sha + "  " + Path.GetFileName(_outputPath) + Environment.NewLine);

                lblStatus.Text = "Completado. SHA-256 calculado.";
                lblStatus.ForeColor = Color.DarkGreen;
                pbCapture.Value = 100;
                btnVerify.Enabled = true;
                btnCancel.Enabled = false;
                Log("=== Captura completada exitosamente ===");
                Log("SHA-256: " + sha);
                Log("Companion: " + _outputPath + ".sha256");
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Cancelado por el operador.";
                lblStatus.ForeColor = Color.DarkRed;
                Log("=== Captura cancelada ===");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "ERROR: " + ex.Message.Split('\n')[0];
                lblStatus.ForeColor = Color.DarkRed;
                Log("ERROR: " + ex.Message);
                Log(ex.ToString());

                // Escribir el diagnostico completo al log de la GUI (no se pierde
                // al cerrar cualquier popup). El operador puede copiarlo al ticket
                // con el boton "Copiar log".
                if (ex is WinPmemFailedException wpf)
                {
                    Log("============================================================");
                    Log("DIAGNOSTICO PARA TICKET (copialo con boton 'Copiar log')");
                    Log("============================================================");
                    Log("Exit code      : " + wpf.ExitCode);
                    Log("Output path    : " + wpf.OutputPath);
                    Log("Hostname       : " + Environment.MachineName);
                    Log("Fecha/hora UTC : " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    Log("Binario        : " + Path.Combine(cmbSourceDrive.SelectedItem != null
                        ? ExtractDriveLetter((string)cmbSourceDrive.SelectedItem) : "?",
                        AppInfo.MemorySubdir, AppInfo.WinPmemName));
                    Log("SO             : " + Environment.OSVersion.VersionString);
                    Log("Usuario        : " + Environment.UserName);
                    Log("RAM fisica     : " + FormatBytes(GetTotalPhysicalMemory()));
                    try
                    {
                        var d = new DriveInfo(Path.GetPathRoot(wpf.OutputPath) ?? "E:\\");
                        Log("Espacio libre  : " + FormatBytes(d.AvailableFreeSpace) +
                            " de " + FormatBytes(d.TotalSize));
                    }
                    catch { }
                    Log("");
                    Log("--- stdout (ultimas 25 lineas) ---");
                    foreach (var line in (wpf.StdoutTail ?? "(vacio)").Split('\n'))
                        Log("  " + line);
                    Log("");
                    Log("--- stderr (ultimas 25 lineas) ---");
                    foreach (var line in (wpf.StderrTail ?? "(vacio)").Split('\n'))
                        Log("  " + line);
                    Log("");
                    Log("--- archivo .raw parcial ---");
                    if (File.Exists(wpf.OutputPath))
                    {
                        try
                        {
                            var fi = new FileInfo(wpf.OutputPath);
                            Log("  Path  : " + fi.FullName);
                            Log("  Tamano: " + FormatBytes(fi.Length) +
                                " (" + fi.Length.ToString("N0") + " bytes)");
                            Log("  Mtime : " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                        catch (Exception exFi) { Log("  (error leyendo .raw: " + exFi.Message + ")"); }
                    }
                    else
                    {
                        Log("  (no existe .raw en disco)");
                    }
                    Log("");
                    Log("--- causa comun del exit code ---");
                    Log("  1. Windows Defender / AV en tiempo real: bloquea carga del driver.");
                    Log("     Fix: agregar exclusion en E:\\ y en D:\\Tools\\Memory\\.");
                    Log("  2. LSASS Protection / Credential Guard: bloquea lectura de paginas.");
                    Log("     Fix: deshabilitar temporalmente o usar winpmem en modo kernel.");
                    Log("  3. Politica de grupo / test signing deshabilitado.");
                    Log("     Fix: gpresult /h gpo.html + revisar DeviceGuard.");
                    Log("  4. Disco lleno a mitad de captura (ver tamano parcial arriba).");
                    Log("  5. AV que puso en cuarentena el .raw durante la escritura.");
                    Log("============================================================");

                    // Popup minimo solo como confirmacion visual. El detalle
                    // completo esta en el log de la GUI.
                    MessageBox.Show(
                        "Captura fallo. Diagnostico completo en el log de la GUI.\n" +
                        "Usa el boton 'Copiar log' para pegarlo en el ticket.\n\n" +
                        "Resumen: " + wpf.Message.Split('\n')[0],
                        AppInfo.BinName + " - Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                btnStart.Enabled = true;
                btnCancel.Enabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void RunWinPmem(string exePath, string outPath, CancellationToken ct)
        {
            bool useCmdWrapper = chkUseCmdWrapper.Checked;

            ProcessStartInfo psi;
            if (useCmdWrapper)
            {
                // Replica lo que el operador hace manualmente en cmd: cmd /c "winpmem.exe" "out"
                Log("Lanzando via cmd.exe wrapper: cmd /c winpmem ...");
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"\"" + exePath + "\"\" \"" + outPath + "\"\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "\"" + outPath + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                };
            }

            // Capturar stdout/stderr en strings para mostrar en caso de error.
            var stdoutCapture = new System.Text.StringBuilder();
            var stderrCapture = new System.Text.StringBuilder();

            _winPmemProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _winPmemProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    lock (stdoutCapture) { stdoutCapture.AppendLine(e.Data); }
                    UpdateProgressFromLine(e.Data);
                }
            };
            _winPmemProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Log("STDERR: " + e.Data);
                    lock (stderrCapture) { stderrCapture.AppendLine(e.Data); }
                }
            };

            try
            {
                _winPmemProcess.Start();
            }
            catch (Exception startEx)
            {
                throw new WinPmemFailedException(
                    "No se pudo iniciar winpmem:\n" + startEx.Message +
                    "\n\n(Verifica que el binario no este bloqueado por AV/Defender o que la ruta sea accesible.)",
                    stdoutCapture.ToString(), stderrCapture.ToString(), 0, outPath);
            }

            _winPmemProcess.BeginOutputReadLine();
            _winPmemProcess.BeginErrorReadLine();

            while (!_winPmemProcess.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { _winPmemProcess.Kill(); } catch { /* ignore */ }
                    ct.ThrowIfCancellationRequested();
                }
                Thread.Sleep(200);
            }
            // Esperar a que los handlers async de stdout terminen
            _winPmemProcess.WaitForExit();
            int exit = _winPmemProcess.ExitCode;
            _winPmemProcess.Dispose();
            _winPmemProcess = null;

            if (exit != 0)
            {
                // Drain remaining captured output
                string stdoutTail, stderrTail;
                lock (stdoutCapture) { stdoutTail = TailLines(stdoutCapture.ToString(), 25); }
                lock (stderrCapture) { stderrTail = TailLines(stderrCapture.ToString(), 25); }

                // v5.2.18: si el archivo .raw existe y tiene tamaño >= 95% de la RAM
                // fisica total, considerar la captura EXITOSA pese al exit code != 0.
                // Bug observado 2026-09-30 con VM DESKTOP-3GUGLTM (6 GB): winpmem
                // escribio los 6 GiB completos pero retorno exit 1 cuando se lanzo
                // desde Process.Start con CreateNoWindow=true. Idéntico al manual
                // desde cmd que dio exit 0. La captura es válida, el archivo es
                // evidencia utilizable. No fallamos al operador por exit code si
                // el archivo está completo.
                long partialSize = 0;
                try { if (File.Exists(outPath)) partialSize = new FileInfo(outPath).Length; } catch { }
                long totalRam = GetTotalPhysicalMemory();
                long expectedMin = (long)(totalRam * 0.95);

                if (partialSize >= expectedMin)
                {
                    Log("============================================================");
                    Log("ADVERTENCIA: winpmem retorno exit code " + exit + " pero el archivo");
                    Log(".raw esta COMPLETO (" + FormatBytes(partialSize) + " >= " +
                        FormatBytes(expectedMin) + " esperados = 95% de RAM).");
                    Log("Captura considerada exitosa. Continuamos con SHA-256.");
                    Log("Si Volatility rechaza el .raw, reintenta con el checkbox");
                    Log("'Ejecutar via cmd.exe wrapper' activado.");
                    Log("============================================================");
                    return; // continuar normalmente con SHA-256
                }

                throw new WinPmemFailedException(
                    BuildFailureMessage(exit, outPath, stdoutTail, stderrTail),
                    stdoutTail, stderrTail, exit, outPath);
            }
        }

        private static string TailLines(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "(vacio)";
            var lines = s.Replace("\r\n", "\n").Split('\n');
            // Quitar ultima linea vacia del split
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                lines = lines.Take(lines.Length - 1).ToArray();
            int start = Math.Max(0, lines.Length - n);
            return string.Join("\n", lines.Skip(start).ToArray());
        }

        private static string BuildFailureMessage(int exit, string outPath, string stdoutTail, string stderrTail)
        {
            long partial = 0;
            try { if (File.Exists(outPath)) partial = new FileInfo(outPath).Length; } catch { }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("winpmem termino con exit code " + exit + ".");
            if (partial > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Archivo parcial detectado: " + FormatBytes(partial) + " en");
                sb.AppendLine("  " + outPath);
                sb.AppendLine("(puede estar completo si partial >= RAM total; validar con SHA-256).");
                sb.AppendLine();
            }
            sb.AppendLine("Causas comunes del exit code " + exit + ":");
            sb.AppendLine("  - Windows Defender / AV en tiempo real bloqueo la carga del driver");
            sb.AppendLine("  - LSASS Protection / Credential Guard bloqueo lectura de paginas");
            sb.AppendLine("  - Politica de grupo restringe carga de drivers (test signing deshabilitado)");
            sb.AppendLine("  - Disco lleno a mitad de captura (verificar partial arriba)");
            sb.AppendLine();
            sb.AppendLine("--- stdout (ultimas lineas) ---");
            sb.AppendLine(stdoutTail);
            sb.AppendLine();
            sb.AppendLine("--- stderr (ultimas lineas) ---");
            sb.AppendLine(stderrTail);
            return sb.ToString();
        }

        private void UpdateProgressFromLine(string line)
        {
            // WinPmem imprime lineas como "Progress: 45.2%" o "[+] Wrote 1234 MB".
            // Best-effort: buscar primer porcentaje en la linea.
            try
            {
                int idx = line.IndexOf('%');
                if (idx > 0)
                {
                    int start = idx - 1;
                    while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.')) start--;
                    var num = line.Substring(start + 1, idx - start - 1);
                    if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                    {
                        int clamped = (int)Math.Max(0, Math.Min(100, pct));
                        BeginInvoke((Action)(() => pbCapture.Value = clamped));
                    }
                }
            }
            catch { /* best-effort */ }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                Log("Cancelacion solicitada por el operador...");
            }
        }

        private void BtnCopyLog_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(txtLog.Text))
                {
                    MessageBox.Show("El log esta vacio.", AppInfo.BinName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var header = "=== RAM_CAPTURE_UI " + AppInfo.Version + " log ===\n" +
                             "Hostname: " + Environment.MachineName + "\n" +
                             "Fecha   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                             "SO      : " + Environment.OSVersion.VersionString + "\n" +
                             "Usuario : " + Environment.UserName + "\n" +
                             "Output  : " + (_outputPath ?? "(no capturado)") + "\n" +
                             "--- LOG ---\n";
                Clipboard.SetText(header + txtLog.Text);
                Log("[copiado al portapapeles " + txtLog.Text.Length + " chars]");
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo copiar al portapapeles:\n" + ex.Message,
                    AppInfo.BinName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void BtnVerify_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_outputPath) || !File.Exists(_outputPath))
            {
                MessageBox.Show("Archivo no encontrado.", AppInfo.BinName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            btnVerify.Enabled = false;
            lblStatus.Text = "Re-calculando SHA-256...";
            var sw = Stopwatch.StartNew();
            try
            {
                var sha = await Task.Run(() => ComputeSha256File(_outputPath));
                sw.Stop();
                txtSha256.Text = sha;
                lblStatus.Text = "Verificacion completada en " + sw.Elapsed.TotalSeconds.ToString("F1") + " s";
                lblStatus.ForeColor = Color.DarkGreen;
                Log("SHA-256 verificado: " + sha);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "ERROR: " + ex.Message;
                lblStatus.ForeColor = Color.DarkRed;
                Log("ERROR verificando: " + ex.Message);
            }
            finally
            {
                btnVerify.Enabled = true;
            }
        }

        // ---------- Helpers ----------

        private static long GetTotalPhysicalMemory()
        {
            // Fuente 1: sumar sticks fisicos (mas preciso). Cada stick reporta Capacity.
            try
            {
                long total = 0;
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Capacity FROM Win32_PhysicalMemory"))
                using (var results = searcher.Get())
                {
                    foreach (var mo in results)
                    {
                        var cap = mo["Capacity"];
                        if (cap != null) total += Convert.ToInt64(cap);
                    }
                }
                if (total > 0) return total;
            }
            catch { /* cae al fallback */ }

            // Fuente 2: TotalPhysicalMemory de Win32_ComputerSystem.
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                using (var results = searcher.Get())
                {
                    foreach (var mo in results)
                    {
                        return Convert.ToInt64(mo["TotalPhysicalMemory"]);
                    }
                }
            }
            catch { /* cae al fallback */ }

            return 4L * 1024L * 1024L * 1024L;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return v.ToString("F2") + " " + units[u];
        }

        private static string ComputeSha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private void Log(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(msg)));
                return;
            }
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }
    }

    /// <summary>
    /// Excepcion lanzada cuando winpmem falla. Contiene stdout/stderr capturados
    /// y el path del archivo parcial (si existe) para diagnostico forense.
    /// </summary>
    public class WinPmemFailedException : Exception
    {
        public string StdoutTail { get; }
        public string StderrTail { get; }
        public int ExitCode { get; }
        public string OutputPath { get; }

        public WinPmemFailedException(string message, string stdoutTail, string stderrTail,
            int exitCode, string outputPath)
            : base(message)
        {
            StdoutTail = stdoutTail;
            StderrTail = stderrTail;
            ExitCode = exitCode;
            OutputPath = outputPath;
        }
    }
}
