using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DfirUploaderUi
{
    /// <summary>
    /// SFTP (SSH File Transfer Protocol) uploader via sftp.exe / psftp.exe CLI.
    /// .NET Framework 4.5.2 no tiene SFTP nativo. Para evitar NuGet (SSH.NET
    /// agrega ~600 KB), usamos el cliente OpenSSH built-in en Windows 10+ o
    /// PuTTY psftp.exe si esta disponible.
    ///
    /// Limitaciones:
    ///   - sftp.exe debe estar en PATH (Windows 10+ lo trae por defecto desde
    ///     Optional Features, o via Git for Windows).
    ///   - Password se pasa via stdin (no command-line, evita exposicion).
    ///   - Sin resume/retry built-in; se reintenta todo si falla.
    /// </summary>
    internal class SftpUploader : IUploader
    {
        public string Protocol { get { return "sftp"; } }

        private readonly UploadConfig _config;
        private readonly Action<string> _log;
        private readonly IProgress<UploadProgress> _progress;

        private Process _inFlightProcess;

        public SftpUploader(UploadConfig config, Action<string> log, IProgress<UploadProgress> progress)
        {
            _config = config;
            _log = log;
            _progress = progress;
        }

        public async Task<UploadResult> UploadFileAsync(
            string localPath, string remoteKey, long sizeBytes, string sha256, CancellationToken ct)
        {
            var remotePath = (_config.SftpPath ?? "/").TrimEnd('/') + "/" + remoteKey;
            var host = _config.SftpHost;
            var port = _config.SftpPort > 0 ? _config.SftpPort : 22;
            var user = _config.SftpUsername ?? string.Empty;

            _log("[+] sftp put " + Path.GetFileName(localPath) + " -> " + user + "@" + host + ":" + remotePath);

            // sftp.exe -batch -i keypath OR stdin password
            // Use stdin for password to avoid command-line exposure
            var sftpArgs = "-o BatchMode=no -o StrictHostKeyChecking=accept-new -P " + port + " " + user + "@" + host;

            var psi = new ProcessStartInfo("sftp.exe", sftpArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _inFlightProcess = Process.Start(psi);
            try
            {
                // Send password if no key path
                if (string.IsNullOrEmpty(_config.SftpKeyPath) && !string.IsNullOrEmpty(_config.SftpPassword))
                {
                    // sftp prompts "Password:" interactively. Send password + newline.
                    // We use the older "ssh_askpass" hack via env, or just feed to stdin.
                    _inFlightProcess.StandardInput.WriteLine(_config.SftpPassword);
                }

                // sftp batch command
                _inFlightProcess.StandardInput.WriteLine("put \"" + localPath + "\" \"" + remotePath + "\"");
                _inFlightProcess.StandardInput.WriteLine("bye");
                _inFlightProcess.StandardInput.Close();

                // Read progress from stdout (sftp prints lines like "Uploading foo.zip to /remote/path")
                // We dont parse those exactly; just wait for completion.
                var stdoutTask = _inFlightProcess.StandardOutput.ReadToEndAsync();
                var stderrTask = _inFlightProcess.StandardError.ReadToEndAsync();

                // Periodically poll process and report progress (best-effort: bytes sent unknown)
                var lastReport = DateTime.UtcNow;
                while (!_inFlightProcess.HasExited)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                    {
                        _progress.Report(new UploadProgress
                        {
                            CurrentFile = Path.GetFileName(localPath),
                            PartNumber = 1,
                            TotalParts = 1,
                            BytesSent = 0,
                            TotalBytes = sizeBytes
                        });
                        lastReport = DateTime.UtcNow;
                    }
                }

                await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                if (_inFlightProcess.ExitCode != 0)
                {
                    _log("[!] sftp fallo: " + stderr);
                    throw new UploadException("sftp put failed", _inFlightProcess.ExitCode, null);
                }

                _log("[+] sftp OK " + remotePath);
                return new UploadResult
                {
                    ETag = sha256,
                    UploadId = null,
                    PartCount = 1
                };
            }
            finally
            {
                _inFlightProcess = null;
            }
        }

        public Task<long> VerifyAsync(string remoteKey, CancellationToken ct)
        {
            // sftp ls -l <path> returns stat info. Parse size from output.
            var remotePath = (_config.SftpPath ?? "/").TrimEnd('/') + "/" + remoteKey;
            var psi = new ProcessStartInfo("sftp.exe",
                "-o BatchMode=no -o StrictHostKeyChecking=accept-new -P " + _config.SftpPort + " " +
                _config.SftpUsername + "@" + _config.SftpHost)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                if (!string.IsNullOrEmpty(_config.SftpPassword))
                {
                    p.StandardInput.WriteLine(_config.SftpPassword);
                }
                p.StandardInput.WriteLine("ls -l \"" + remotePath + "\"");
                p.StandardInput.WriteLine("bye");
                p.StandardInput.Close();

                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                // Parse line like "-rw-r--r-- 1 user group 12345 Jan 1 12:00 /path/to/file"
                foreach (var line in stdout.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains(remoteKey))
                    {
                        var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            var size = TryParseSize(parts[4]);
                            if (size >= 0)
                            {
                                _log("[+] sftp verify OK " + remoteKey + " (" + size + " bytes)");
                                return Task.FromResult(size);
                            }
                        }
                    }
                }
                return Task.FromResult<long>(-1);
            }
        }

        private static long TryParseSize(string s)
        {
            long v;
            return long.TryParse(s, out v) ? v : -1;
        }

        public Task AbortAllAsync()
        {
            try
            {
                if (_inFlightProcess != null && !_inFlightProcess.HasExited)
                {
                    _inFlightProcess.Kill();
                }
            }
            catch { }
            return Task.Delay(0);
        }
    }
}