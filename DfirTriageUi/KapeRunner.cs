using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DfirTriageUi
{
    /// <summary>
    /// Orquesta la corrida de KAPE 1.3.0.2 con el target KapeTriage.
    /// Stage del kape.exe desde CD-ROM (D:) a disco de salida (E:) writable.
    /// Run de kape.exe con --tsource / --target / --tdest / --targetdir / --zip / --hv / --gui.
    /// Importante: close stdin antes de Process.Start para evitar que KAPE quede
    /// bloqueado en Console.ReadKey() al final (problema v5.2.10).
    /// </summary>
    internal class KapeRunner
    {
        private readonly Action<string> _log;
        private readonly bool _useCmdWrapper;

        public KapeRunner(Action<string> log, bool useCmdWrapper)
        {
            _log = log;
            _useCmdWrapper = useCmdWrapper;
        }

        public class Result
        {
            public string ZipPath { get; set; }
            public long ZipSize { get; set; }
            public string Sha256 { get; set; }
            public int ExitCode { get; set; }
            public TimeSpan Duration { get; set; }
        }

        /// <summary>
        /// Corre KapeTriage. Devuelve Result con path del ZIP + SHA-256.
        /// Lanza KapeException si falla.
        /// </summary>
        public async Task<Result> RunAsync(
            string cdRomDrive,     // ej "D:"
            string outputDrive,    // ej "E:" (DFIR_OUTPUT)
            string sourceDrive,    // ej "C:"
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var host = Environment.MachineName;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var outBase = outputDrive + "\\" + host + "-" + stamp;
            var outZipName = host + "-KAPE-" + stamp;
            var stageDir = outputDrive + "\\_kape_stage";

            // 1. Stage kape.exe from CD-ROM to output drive (CD-ROM is read-only)
            var stageKape = Path.Combine(stageDir, "kape.exe");
            await StageFileAsync(cdRomDrive + "\\kape.exe", stageKape, ct);

            // 2. Stage Targets/ tree
            var targetsSrc = cdRomDrive + "\\Targets";
            var targetsDst = Path.Combine(stageDir, "Targets");
            await StageDirectoryAsync(targetsSrc, targetsDst, ct);

            // 3. Build args
            // Removed --sync (per bug). Without --sync, KAPE doesn't try to download.
            // Removed --moduledir and --mdest (per bug - they pointed to read-only D:\Modules).
            var args = "--tsource " + sourceDrive + "\\" +
                       " --target KapeTriage" +
                       " --tdest " + outBase + "\\" +
                       " --targetdir " + targetsDst +
                       " --zip " + outBase + "\\" + outZipName +
                       " --hv nc,vm" +
                       " --gui";
            _log("[+] KAPE args: kape.exe " + args);

            // 4. Launch
            Process proc = null;
            if (_useCmdWrapper)
            {
                // Wrapper: cmd /c start "" /B /WAIT kape.exe [args]
                // Esto le da a KAPE una consola real, Console.Title funciona.
                var wrapperArgs = "/c start \"\" /B /WAIT \"kape.exe\" " + args;
                _log("[+] cmd /c start ... kape.exe (wrapper mode)");
                var psi = new ProcessStartInfo("cmd.exe", wrapperArgs)
                {
                    WorkingDirectory = stageDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
                proc = Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo(Path.Combine(stageDir, "kape.exe"), args)
                {
                    WorkingDirectory = stageDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
                proc = Process.Start(psi);
            }

            // CRITICAL: close stdin immediately so KAPE's Console.ReadKey() returns -1
            proc.StandardInput.Close();

            // Stream output
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) _log("[KAPE] " + e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _log("[KAPE-err] " + e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Wait
            while (!proc.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    ct.ThrowIfCancellationRequested();
                }
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            proc.WaitForExit();
            var exit = proc.ExitCode;
            sw.Stop();

            // 5. Find the generated ZIP (KAPE writes inside --tdest with timestamp prefix)
            var zipPath = FindLatestZip(outBase, outZipName);
            if (zipPath == null)
            {
                throw new KapeException("KAPE no genero ZIP. Exit code: " + exit, exit);
            }

            // 6. SHA-256
            _log("[+] KAPE ZIP: " + zipPath);
            var zipSize = new FileInfo(zipPath).Length;
            _log("[+] SHA-256 calculando (" + HashUtil.FormatBytes(zipSize) + ")...");
            var sha256 = HashUtil.ComputeSha256Streaming(zipPath);
            _log("[+] SHA-256: " + sha256);

            return new Result
            {
                ZipPath = zipPath,
                ZipSize = zipSize,
                Sha256 = sha256,
                ExitCode = exit,
                Duration = sw.Elapsed
            };
        }

        private async Task StageFileAsync(string source, string dest, CancellationToken ct)
        {
            if (File.Exists(dest))
            {
                var srcInfo = new FileInfo(source);
                var dstInfo = new FileInfo(dest);
                if (srcInfo.Length == dstInfo.Length && srcInfo.LastWriteTime == dstInfo.LastWriteTime)
                {
                    _log("[+] Stage skip (cached): " + dest);
                    return;
                }
            }

            _log("[+] Stage: " + source + " -> " + dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            // AV/Defender may set READONLY on the staged exe; clean + retry
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(dest))
                    {
                        File.SetAttributes(dest, FileAttributes.Normal);
                        File.Delete(dest);
                    }
                    File.Copy(source, dest, true);
                    return;
                }
                catch (Exception ex) when (attempt < 2)
                {
                    _log("[!] Stage retry " + (attempt + 1) + ": " + ex.Message);
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
            throw new KapeException("Stage fallo: " + source + " -> " + dest, -1);
        }

        private async Task StageDirectoryAsync(string srcDir, string dstDir, CancellationToken ct)
        {
            if (!Directory.Exists(srcDir))
            {
                throw new KapeException("Targets/ no encontrado en " + srcDir, -1);
            }
            if (Directory.Exists(dstDir) && Directory.Exists(Path.Combine(dstDir, "Compound")))
            {
                _log("[+] Stage dir skip (cached): " + dstDir);
                return;
            }
            _log("[+] Stage dir: " + srcDir + " -> " + dstDir);
            Directory.CreateDirectory(dstDir);
            await CopyDirectoryAsync(srcDir, dstDir, ct);
        }

        private async Task CopyDirectoryAsync(string srcDir, string dstDir, CancellationToken ct)
        {
            Directory.CreateDirectory(dstDir);
            foreach (var file in Directory.GetFiles(srcDir))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                var dst = Path.Combine(dstDir, name);
                File.Copy(file, dst, true);
            }
            foreach (var dir in Directory.GetDirectories(srcDir))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                await CopyDirectoryAsync(dir, Path.Combine(dstDir, name), ct).ConfigureAwait(false);
            }
            await Task.Delay(0);
        }

        private string FindLatestZip(string outBase, string outZipName)
        {
            // KAPE writes: {outBase}\{timestamp}_E__{outZipName}.zip
            var dir = Path.GetDirectoryName(outBase);
            if (!Directory.Exists(dir)) return null;
            var prefix = Path.GetFileName(outBase);
            var files = Directory.GetFiles(dir, "*" + outZipName + "*.zip");
            if (files.Length == 0) return null;
            // Pick the most recent
            Array.Sort(files);
            return files[files.Length - 1];
        }
    }

    internal class KapeException : Exception
    {
        public int ExitCode { get; set; }
        public KapeException(string msg, int code) : base(msg) { ExitCode = code; }
    }
}