using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DfirUploaderUi
{
    /// <summary>
    /// Form principal de DFIR_UPLOADER_UI.
    /// Workflow:
    ///   1. Operador ingresa URL del config remoto + click Sync.
    ///   2. Operador completa metadata (case, operator, date, notes).
    ///   3. Operador selecciona path local + checkboxes de archivos.
    ///   4. Operador click Upload -> SHA-256 + upload + verify + audit log.
    /// </summary>
    public class DfirUploaderMain : Form
    {
        // UI controls
        private readonly TextBox txtConfigUrl = new TextBox();
        private readonly Button btnSync = new Button();
        private readonly Button btnHealth = new Button();
        private readonly Label lblBucketInfo = new Label();
        private readonly Label lblExpiresInfo = new Label();

        private readonly TextBox txtCaseId = new TextBox();
        private readonly TextBox txtOperator = new TextBox();
        private readonly DateTimePicker dtIncidentDate = new DateTimePicker();
        private readonly TextBox txtNotes = new TextBox();

        private readonly TextBox txtSourcePath = new TextBox();
        private readonly Button btnBrowse = new Button();
        private readonly Button btnRescan = new Button();
        private readonly CheckedListBox lstFiles = new CheckedListBox();
        private readonly Label lblTotal = new Label();

        private readonly Button btnUpload = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnClose = new Button();
        private readonly ProgressBar pbOverall = new ProgressBar();
        private readonly Label lblStatus = new Label();

        private readonly TextBox txtLog = new TextBox();
        private readonly Button btnSaveLog = new Button();
        private readonly Label lblFooter = new Label();

        // State
        private UploadConfig _config;
        private AuditLog _auditLog;
        private CancellationTokenSource _cts;
        private long _totalBytesSelected;
        private long _totalBytesUploaded;
        private const string ApplicationVersion = "5.3.0";

        public DfirUploaderMain()
        {
            Text = "DFIR Uploader - KAPE-MEDIA v" + ApplicationVersion + " - By Cybolt";
            Width = 980;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            Load += (s, e) => OnLoad();
            Shown += (s, e) => CheckAdminOrWarn();
            FormClosing += DfirUploaderMain_FormClosing;
        }

        private void BuildLayout()
        {
            int y = 12;
            int labelW = 110;
            int fieldW = 660;

            // Title
            var lblTitle = new Label
            {
                Text = "DFIR Uploader - Subida de evidencia a S3-compatible (KAPE-MEDIA v" + ApplicationVersion + ")",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblTitle);
            y += 30;

            // ===== 1. Config remoto =====
            var lblCfg = new Label
            {
                Text = "1. Config remoto (URL del JSON con credenciales + bucket):",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblCfg);
            y += 20;

            var lblUrl = new Label
            {
                Text = "URL:",
                Location = new Point(12, y + 3),
                AutoSize = true,
            };
            Controls.Add(lblUrl);
            txtConfigUrl.Location = new Point(labelW, y);
            txtConfigUrl.Width = fieldW - 180;
            // .NET 4.5.2 no tiene PlaceholderText nativo - usar Text o label hint.
            // El operador ve el placeholder en el manual.
            Controls.Add(txtConfigUrl);
            btnSync.Text = "🔄 Sync";
            btnSync.Location = new Point(txtConfigUrl.Right + 8, y);
            btnSync.Width = 80;
            btnSync.Click += BtnSync_Click;
            Controls.Add(btnSync);
            btnHealth.Text = "✓ Health";
            btnHealth.Location = new Point(btnSync.Right + 4, y);
            btnHealth.Width = 88;
            btnHealth.Click += BtnHealth_Click;
            btnHealth.Enabled = false;
            Controls.Add(btnHealth);
            y += 28;
            lblBucketInfo.Location = new Point(12, y);
            lblBucketInfo.AutoSize = true;
            lblBucketInfo.Text = "Bucket: (sin cargar)";
            Controls.Add(lblBucketInfo);
            lblExpiresInfo.Location = new Point(400, y);
            lblExpiresInfo.AutoSize = true;
            lblExpiresInfo.Text = "Expira: -";
            Controls.Add(lblExpiresInfo);
            y += 24;

            // Separator
            Controls.Add(new Label { Text = "", Location = new Point(12, y), Width = 900, BorderStyle = BorderStyle.Fixed3D, Height = 2 });
            y += 8;

            // ===== 2. Metadata =====
            var lblMeta = new Label
            {
                Text = "2. Metadata del incidente:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblMeta);
            y += 20;

            AddLabeledTextBox("Case ID:", ref y, labelW, fieldW, txtCaseId);
            AddLabeledTextBox("Operador:", ref y, labelW, fieldW, txtOperator);
            txtOperator.Text = Environment.UserName;
            var lblDate = new Label { Text = "Fecha:", Location = new Point(12, y + 3), AutoSize = true };
            Controls.Add(lblDate);
            dtIncidentDate.Location = new Point(labelW, y);
            dtIncidentDate.Width = 160;
            dtIncidentDate.Format = DateTimePickerFormat.Short;
            dtIncidentDate.Value = DateTime.Now;
            Controls.Add(dtIncidentDate);
            y += 26;
            AddLabeledTextBox("Notas:", ref y, labelW, fieldW, txtNotes);

            // Separator
            Controls.Add(new Label { Text = "", Location = new Point(12, y), Width = 900, BorderStyle = BorderStyle.Fixed3D, Height = 2 });
            y += 8;

            // ===== 3. File selector =====
            var lblFiles = new Label
            {
                Text = "3. Seleccion de artefactos (carpeta local con la evidencia):",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblFiles);
            y += 20;

            var lblPath = new Label { Text = "Path:", Location = new Point(12, y + 3), AutoSize = true };
            Controls.Add(lblPath);
            txtSourcePath.Location = new Point(labelW, y);
            txtSourcePath.Width = fieldW - 180;
            txtSourcePath.Text = @"E:\DFIR-OUTPUT\";
            // .NET 4.5.2 no tiene PlaceholderText nativo. Default text covers el caso.
            Controls.Add(txtSourcePath);
            btnBrowse.Text = "📁 Browse";
            btnBrowse.Location = new Point(txtSourcePath.Right + 8, y);
            btnBrowse.Width = 80;
            btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(btnBrowse);
            btnRescan.Text = "🔄 Rescan";
            btnRescan.Location = new Point(btnBrowse.Right + 4, y);
            btnRescan.Width = 88;
            btnRescan.Click += BtnRescan_Click;
            Controls.Add(btnRescan);
            y += 28;

            lstFiles.Location = new Point(12, y);
            lstFiles.Width = fieldW;
            lstFiles.Height = 140;
            lstFiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lstFiles.CheckOnClick = true;
            lstFiles.IntegralHeight = false;
            lstFiles.HorizontalScrollbar = true;
            lstFiles.MultiColumn = false;
            lstFiles.ItemCheck += lstFiles_ItemCheck;
            Controls.Add(lstFiles);
            y += lstFiles.Height + 4;
            lblTotal.Location = new Point(12, y);
            lblTotal.AutoSize = true;
            lblTotal.Text = "Total: 0 archivos seleccionados, 0 B";
            Controls.Add(lblTotal);
            y += 22;

            // Separator
            Controls.Add(new Label { Text = "", Location = new Point(12, y), Width = 900, BorderStyle = BorderStyle.Fixed3D, Height = 2 });
            y += 8;

            // ===== 4. Upload =====
            var lblUp = new Label
            {
                Text = "4. Upload:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblUp);
            y += 22;
            btnUpload.Text = "▶ Upload";
            btnUpload.Location = new Point(12, y);
            btnUpload.Width = 110;
            btnUpload.Click += BtnUpload_Click;
            btnUpload.Enabled = false;
            Controls.Add(btnUpload);
            btnCancel.Text = "■ Cancel";
            btnCancel.Location = new Point(128, y);
            btnCancel.Width = 110;
            btnCancel.Click += BtnCancel_Click;
            btnCancel.Enabled = false;
            Controls.Add(btnCancel);
            btnClose.Text = "✕ Close";
            btnClose.Location = new Point(244, y);
            btnClose.Width = 110;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
            y += 32;
            pbOverall.Location = new Point(12, y);
            pbOverall.Width = fieldW;
            pbOverall.Height = 22;
            Controls.Add(pbOverall);
            y += 26;
            lblStatus.Location = new Point(12, y);
            lblStatus.AutoSize = true;
            lblStatus.Text = "Estado: -";
            Controls.Add(lblStatus);
            y += 24;

            // Separator
            Controls.Add(new Label { Text = "", Location = new Point(12, y), Width = 900, BorderStyle = BorderStyle.Fixed3D, Height = 2 });
            y += 8;

            // ===== 5. Log =====
            var lblLog = new Label
            {
                Text = "5. Log:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, y),
                AutoSize = true,
            };
            Controls.Add(lblLog);
            y += 20;
            txtLog.Location = new Point(12, y);
            txtLog.Width = fieldW;
            txtLog.Height = 90;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            txtLog.BackColor = Color.Black;
            txtLog.ForeColor = Color.LightGreen;
            txtLog.Font = new Font("Consolas", 8F);
            Controls.Add(txtLog);
            btnSaveLog.Text = "💾 Save log";
            btnSaveLog.Location = new Point(txtLog.Right + 8, y);
            btnSaveLog.Width = 100;
            btnSaveLog.Click += BtnSaveLog_Click;
            Controls.Add(btnSaveLog);
            y += txtLog.Height + 6;
            lblFooter.Location = new Point(12, y);
            lblFooter.AutoSize = true;
            lblFooter.Text = "DFIR_UPLOADER_UI v" + ApplicationVersion + " - By Cybolt, MIT License";
            lblFooter.ForeColor = Color.Gray;
            Controls.Add(lblFooter);
        }

        private void AddLabeledTextBox(string label, ref int y, int labelW, int fieldW, TextBox tb)
        {
            var lbl = new Label { Text = label, Location = new Point(12, y + 3), AutoSize = true };
            Controls.Add(lbl);
            tb.Location = new Point(labelW, y);
            tb.Width = fieldW;
            Controls.Add(tb);
            y += 26;
        }

        private void OnLoad()
        {
            Log("[+] DFIR_UPLOADER_UI v" + ApplicationVersion + " iniciado");
            Log("[+] Hostname: " + Environment.MachineName + " | OS: " + Environment.OSVersion.VersionString);
            Log("[+] Usuario: " + Environment.UserName);
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
                            "Si llegaste hasta aca sin pasar por el prompt UAC, " +
                            "tu usuario no tiene privilegios suficientes para correr esta GUI.\n\n" +
                            "Pide a tu administrador local o de dominio que ejecute el .exe " +
                            "con permisos elevados.",
                            "DFIR_UPLOADER_UI v" + ApplicationVersion + " - Permisos",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[!] CheckAdmin: " + ex.Message);
            }
        }

        private void DfirUploaderMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                var result = MessageBox.Show(
                    "Hay un upload en curso. Cancelarlo y cerrar?",
                    "DFIR_UPLOADER_UI",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                _cts.Cancel();
            }

            // Best-effort wipe sensitive state
            _config = null;
            if (_cts != null) { _cts.Dispose(); _cts = null; }
        }

        // ===== Buttons =====

        private async void BtnSync_Click(object sender, EventArgs e)
        {
            btnSync.Enabled = false;
            Log("[+] Sync config...");
            _config = await ConfigFetcher.FetchAsync(txtConfigUrl.Text, CancellationToken.None, Log);
            if (_config != null)
            {
                lblBucketInfo.Text = "Bucket: " + _config.Bucket + "  Prefix: " + _config.Prefix;
                lblExpiresInfo.Text = "Expira: " + _config.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ") +
                                     " (en " + ((int)(_config.ExpiresAt - DateTime.UtcNow).TotalMinutes) + " min)";
                btnHealth.Enabled = true;
                btnUpload.Enabled = true;
            }
            else
            {
                btnHealth.Enabled = false;
                btnUpload.Enabled = false;
            }
            btnSync.Enabled = true;
        }

        private async void BtnHealth_Click(object sender, EventArgs e)
        {
            if (_config == null) return;
            btnHealth.Enabled = false;
            Log("[+] HEAD health check " + _config.Endpoint);
            try
            {
                var uploader = new S3Uploader(_config, Log, new Progress<UploadProgress>(p => { }), CancellationToken.None);
                // Use a dummy key just to verify the endpoint accepts our creds
                var len = await uploader.VerifyHeadAsync(_config.Prefix + "_health-probe-non-existent-key");
                Log("[+] Health OK (Content-Length=" + len + ")");
            }
            catch (UploadException ux)
            {
                // 404 is OK for non-existent key; we just want to confirm auth works
                if (ux.HttpStatus == 404)
                {
                    Log("[+] Health OK (HEAD 404 esperado - bucket alcanzable con las credenciales)");
                }
                else
                {
                    Log("[!] Health HTTP " + ux.HttpStatus + ": " + ux.Message);
                }
            }
            catch (Exception ex)
            {
                Log("[!] Health fallo: " + ex.GetType().Name + ": " + ex.Message);
            }
            btnHealth.Enabled = true;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.SelectedPath = Directory.Exists(txtSourcePath.Text) ? txtSourcePath.Text : @"E:\";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtSourcePath.Text = dlg.SelectedPath;
                    BtnRescan_Click(null, null);
                }
            }
        }

        private void BtnRescan_Click(object sender, EventArgs e)
        {
            var path = txtSourcePath.Text;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                Log("[!] Path no existe: " + path);
                return;
            }
            lstFiles.Items.Clear();
            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                                     .Where(f => !f.Contains("\\_kape_stage\\") &&
                                                 !f.Contains("\\Modules\\bin\\"))
                                     .OrderBy(f => f)
                                     .ToArray();
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    lstFiles.Items.Add(FormatFileEntry(fi), CheckState.Checked);
                }
                Log("[+] scan: " + files.Length + " archivos en " + path);
                UpdateTotal();
            }
            catch (Exception ex)
            {
                Log("[!] Scan fallo: " + ex.Message);
            }
        }

        private static string FormatFileEntry(FileInfo fi)
        {
            return string.Format("{0,-12} {1}", FormatBytes(fi.Length), fi.FullName);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KiB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MiB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GiB";
        }

        private void UpdateTotal()
        {
            long totalBytes = 0;
            int fileCount = 0;
            foreach (var item in lstFiles.CheckedItems)
            {
                var entry = (string)item;
                var path = entry.Substring(13); // strip "{bytes,-12} "
                var fi = new FileInfo(path);
                totalBytes += fi.Length;
                fileCount++;
            }
            _totalBytesSelected = totalBytes;
            lblTotal.Text = "Total: " + fileCount + " archivos seleccionados, " + FormatBytes(totalBytes);
        }

        private void lstFiles_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            // Defer update to allow CheckedItems to refresh
            BeginInvoke((MethodInvoker)delegate { UpdateTotal(); });
        }

        private async void BtnUpload_Click(object sender, EventArgs e)
        {
            if (_config == null)
            {
                Log("[!] Sin config cargado. Hacé Sync primero.");
                return;
            }
            if (lstFiles.CheckedItems.Count == 0)
            {
                Log("[!] No hay archivos seleccionados.");
                return;
            }

            // Build list of files to upload
            var filesToUpload = new List<Tuple<string, long>>();
            foreach (var item in lstFiles.CheckedItems)
            {
                var entry = (string)item;
                var path = entry.Substring(13);
                filesToUpload.Add(Tuple.Create(path, new FileInfo(path).Length));
            }

            // Build audit log
            _auditLog = AuditLog.Create(
                txtOperator.Text,
                txtCaseId.Text,
                dtIncidentDate.Value.ToString("yyyy-MM-dd"),
                txtNotes.Text,
                _config);
            foreach (var t in filesToUpload)
            {
                var fi = new FileInfo(t.Item1);
                var s3Key = _config.Prefix + Path.GetFileName(fi.Name);
                _auditLog.AddFile(fi.FullName, s3Key, t.Item2, "");
            }

            // UI state
            btnUpload.Enabled = false;
            btnSync.Enabled = false;
            btnHealth.Enabled = false;
            btnCancel.Enabled = true;
            btnRescan.Enabled = false;
            btnBrowse.Enabled = false;
            _cts = new CancellationTokenSource();
            _totalBytesUploaded = 0;
            pbOverall.Minimum = 0;
            pbOverall.Maximum = 100;
            pbOverall.Value = 0;

            var progress = new Progress<UploadProgress>(p =>
            {
                var pct = (int)((_totalBytesUploaded * 100) / Math.Max(1, _totalBytesSelected));
                pbOverall.Value = Math.Min(100, pct);
                lblStatus.Text = "Estado: parte " + p.PartNumber + "/" + p.TotalParts +
                                 " de " + p.CurrentFile +
                                 " (" + FormatBytes(_totalBytesUploaded) + "/" + FormatBytes(_totalBytesSelected) + ")";
            });

            var uploader = new S3Uploader(_config, Log, progress, _cts.Token);

            int uploaded = 0;
            int failed = 0;
            int cancelled = 0;
            try
            {
                foreach (var t in filesToUpload)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    var fi = new FileInfo(t.Item1);
                    var s3Key = _config.Prefix + Path.GetFileName(fi.Name);

                    // Hash first
                    Log("[+] SHA-256 " + Path.GetFileName(fi.FullName));
                    string sha256;
                    try
                    {
                        sha256 = HashUtil.ComputeSha256Streaming(fi.FullName, _cts.Token);
                        Log("[+] SHA-256: " + sha256);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception hex)
                    {
                        Log("[!] SHA-256 fallo: " + hex.Message);
                        _auditLog.MarkFileResult(fi.FullName, null, null, 0, "FAILED", "SHA-256: " + hex.Message);
                        failed++;
                        continue;
                    }

                    // Upload
                    try
                    {
                        var result = await uploader.UploadFileAsync(fi.FullName, s3Key, t.Item2, sha256);
                        _auditLog.MarkFileResult(fi.FullName, result.ETag, result.UploadId, result.PartCount, "OK", null);
                        _totalBytesUploaded += t.Item2;
                        uploaded++;
                    }
                    catch (OperationCanceledException)
                    {
                        _auditLog.MarkFileResult(fi.FullName, null, null, 0, "CANCELLED", "user cancelled");
                        cancelled++;
                        Log("[!] " + Path.GetFileName(fi.FullName) + " CANCELADO");
                        break;
                    }
                    catch (UploadException ux)
                    {
                        _auditLog.MarkFileResult(fi.FullName, null, null, 0, "FAILED", "HTTP " + ux.HttpStatus + ": " + ux.Message);
                        failed++;
                        Log("[!] " + Path.GetFileName(fi.FullName) + " FAILED HTTP " + ux.HttpStatus);
                    }
                    catch (Exception ex)
                    {
                        _auditLog.MarkFileResult(fi.FullName, null, null, 0, "FAILED", ex.GetType().Name + ": " + ex.Message);
                        failed++;
                        Log("[!] " + Path.GetFileName(fi.FullName) + " FAILED: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                // Verify HEAD (best-effort)
                if (cancelled == 0 && uploaded > 0)
                {
                    Log("[+] Post-upload HEAD verify...");
                    foreach (var t in filesToUpload)
                    {
                        var fi = new FileInfo(t.Item1);
                        var s3Key = _config.Prefix + Path.GetFileName(fi.Name);
                        try
                        {
                            var remoteSize = await uploader.VerifyHeadAsync(s3Key);
                            if (remoteSize != t.Item2)
                            {
                                Log("[!] HEAD size mismatch " + Path.GetFileName(fi.FullName) + ": local=" + t.Item2 + " remote=" + remoteSize);
                            }
                        }
                        catch (Exception vx)
                        {
                            Log("[!] HEAD verify fallo " + Path.GetFileName(fi.FullName) + ": " + vx.Message);
                        }
                    }
                }
            }
            finally
            {
                _auditLog.MarkCompleted();
                pbOverall.Value = 100;
                lblStatus.Text = "Estado: terminado (" + uploaded + " OK, " + failed + " failed, " + cancelled + " cancelled)";
                Log("[+] Upload completo: " + uploaded + " OK / " + failed + " failed / " + cancelled + " cancelled");

                // Audit log: write to disk
                var outDir = !string.IsNullOrEmpty(_config.Prefix) && Directory.Exists(@"E:\DFIR-OUTPUT\") ? @"E:\DFIR-OUTPUT\" : Path.GetTempPath();
                var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                var auditLocalPath = Path.Combine(outDir, "upload-audit-" + ts + ".json");
                _auditLog.WriteToDisk(auditLocalPath, Log);

                // Audit log: upload sidecar to bucket (best-effort)
                try
                {
                    var sidecarKey = _config.Prefix + "audit-" + ts + ".json";
                    var json = _auditLog.Serialize();
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    var uri = new Uri(_config.Endpoint.TrimEnd('/') + "/" + _config.Bucket + "/" + Uri.EscapeUriString(sidecarKey).Replace("%2F", "/"));
                    var headers = new Dictionary<string, string>();
                    var signed = SigV4Signer.SignRequest(
                        "PUT",
                        _config.Endpoint,
                        "/" + _config.Bucket + "/" + sidecarKey,
                        string.Empty,
                        headers,
                        PayloadHashMode.Sha256,
                        bytes,
                        _config.AccessKeyId,
                        _config.SecretAccessKey,
                        _config.Region,
                        "s3",
                        _config.SessionToken);
                    using (var handler = new HttpClientHandler())
                    using (var http = new HttpClient(handler))
                    {
                        http.Timeout = TimeSpan.FromSeconds(60);
                        var content = new ByteArrayContent(bytes);
                        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                        foreach (var kv in signed) content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        var resp = await http.PutAsync(uri, content).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            Log("[+] Audit sidecar uploaded: s3://" + _config.Bucket + "/" + sidecarKey);
                        }
                        else
                        {
                            Log("[!] Audit sidecar upload HTTP " + (int)resp.StatusCode);
                        }
                    }
                }
                catch (Exception ax)
                {
                    Log("[!] Audit sidecar upload fallo: " + ax.Message);
                }

                // Reset UI
                btnUpload.Enabled = _config != null;
                btnSync.Enabled = true;
                btnHealth.Enabled = _config != null;
                btnCancel.Enabled = false;
                btnRescan.Enabled = true;
                btnBrowse.Enabled = true;
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                Log("[!] Cancel solicitado por usuario");
                _cts.Cancel();
            }
        }

        private void BtnSaveLog_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dlg.FileName = "dfir-uploader-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(dlg.FileName, txtLog.Text, Encoding.UTF8);
                        Log("[+] Log saved: " + dlg.FileName);
                    }
                    catch (Exception ex)
                    {
                        Log("[!] Save log fallo: " + ex.Message);
                    }
                }
            }
        }

        // ===== Logging =====

        public void Log(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(msg)));
                return;
            }
            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
            txtLog.AppendText(line + Environment.NewLine);
            // Auto-scroll
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
    }
}