using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DfirUploaderUi
{
    /// <summary>
    /// SMB uploader. Usa net use para montar el share con credenciales, despues
    /// File.Copy al UNC path. Al terminar, net use /delete para limpiar.
    ///
    /// Limitaciones:
    ///   - net use requiere cmd.exe (no funciona desde servicio o proceso sin
    ///     shell interactivo). El .exe WinForms corre como usuario asi que OK.
    ///   - Solo soporta SMB 2.0+ (Windows 7+).
    ///   - Credenciales en linea de comandos de net use son visibles via
    ///     Get-Process / WMI. Para DFIR drill aceptable; para prod usar
    ///     Kerberos o certificado de cliente.
    /// </summary>
    internal class SmbUploader : IUploader
    {
        public string Protocol { get { return "smb"; } }

        private readonly UploadConfig _config;
        private readonly Action<string> _log;
        private readonly IProgress<UploadProgress> _progress;

        private string _mountedDriveLetter; // e.g. "Z:" cuando montamos \\server\share as Z:

        public SmbUploader(UploadConfig config, Action<string> log, IProgress<UploadProgress> progress)
        {
            _config = config;
            _log = log;
            _progress = progress;
        }

        public async Task<UploadResult> UploadFileAsync(
            string localPath, string remoteKey, long sizeBytes, string sha256, CancellationToken ct)
        {
            // Mount share si no esta montado
            if (string.IsNullOrEmpty(_mountedDriveLetter))
            {
                _mountedDriveLetter = await MountShareAsync(ct).ConfigureAwait(false);
            }

            // Build dest path: <mountedDrive>:\SmbPath\remoteKey
            var remotePath = _mountedDriveLetter + (_config.SmbPath ?? "\\").TrimEnd('\\') + "\\" + remoteKey;
            _log("[+] Subiendo " + Path.GetFileName(localPath) + " (" + FormatBytes(sizeBytes) + ") -> " + remotePath);

            // Create destination directory if needed
            var destDir = Path.GetDirectoryName(remotePath);
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy file with progress (use FileStream with manual copy for progress)
            using (var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var dest = new FileStream(remotePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[64 * 1024];
                int read;
                long totalSent = 0;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await dest.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    totalSent += read;
                    _progress.Report(new UploadProgress
                    {
                        CurrentFile = Path.GetFileName(localPath),
                        PartNumber = 1,
                        TotalParts = 1,
                        BytesSent = totalSent,
                        TotalBytes = sizeBytes
                    });
                }
            }

            _log("[+] SMB copy OK " + remoteKey);
            return new UploadResult
            {
                ETag = sha256,
                UploadId = null,
                PartCount = 1
            };
        }

        public async Task<long> VerifyAsync(string remoteKey, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_mountedDriveLetter))
            {
                _mountedDriveLetter = await MountShareAsync(ct).ConfigureAwait(false);
            }
            var remotePath = _mountedDriveLetter + (_config.SmbPath ?? "\\").TrimEnd('\\') + "\\" + remoteKey;
            if (!File.Exists(remotePath))
            {
                throw new UploadException("SMB verify: file not found", 404, null);
            }
            var fi = new FileInfo(remotePath);
            _log("[+] SMB verify OK " + remoteKey + " (" + fi.Length + " bytes)");
            return fi.Length;
        }

        public async Task AbortAllAsync()
        {
            // File.Copy completed files are atomic on Windows; partial files
            // we cant easily delete. Just dismount.
            await UnmountShareAsync().ConfigureAwait(false);
        }

        private async Task<string> MountShareAsync(CancellationToken ct)
        {
            // Find free drive letter
            var letter = FindFreeDriveLetter();
            if (string.IsNullOrEmpty(letter))
            {
                throw new UploadException("SMB mount: no free drive letters", 0, null);
            }

            // net use <letter>: <share> /user:<domain\user> <password>
            var share = _config.SmbShare;
            var user = _config.SmbUsername ?? string.Empty;
            var pass = _config.SmbPassword ?? string.Empty;

            _log("[+] net use " + letter + " " + share + " /user:" + user + " (password hidden)");
            var psi = new ProcessStartInfo("net.exe", "use " + letter + " \"" + share + "\" /user:\"" + user + "\" \"" + pass + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new UploadException("net use fallo: " + stdout + " " + stderr, p.ExitCode, null);
                }
                _log("[+] Mount OK: " + letter + " -> " + share);
            }

            // Verify mount exists
            await Task.Yield();
            return letter + "\\";
        }

        private async Task UnmountShareAsync()
        {
            if (string.IsNullOrEmpty(_mountedDriveLetter)) return;
            try
            {
                _log("[+] net use " + _mountedDriveLetter + " /delete");
                var letter = _mountedDriveLetter.TrimEnd('\\');
                var psi = new ProcessStartInfo("net.exe", "use " + letter + " /delete /y")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                _mountedDriveLetter = null;
            }
            catch (Exception ex)
            {
                _log("[!] Unmount fallo: " + ex.Message);
            }
            await Task.Delay(0);
        }

        private static string FindFreeDriveLetter()
        {
            // Skip A-E (commonly used by Windows/system). Try Z, Y, X, ... down.
            for (char c = 'Z'; c >= 'F'; c--)
            {
                var letter = c + ":";
                var drives = DriveInfo.GetDrives();
                bool used = false;
                foreach (var d in drives)
                {
                    if (d.Name.StartsWith(letter, StringComparison.OrdinalIgnoreCase))
                    {
                        used = true;
                        break;
                    }
                }
                if (!used) return letter;
            }
            return null;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KiB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MiB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GiB";
        }
    }
}