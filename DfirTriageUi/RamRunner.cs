using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace DfirTriageUi
{
    /// <summary>
    /// Orquesta la captura de RAM con winpmem_mini_x64_rc2.exe (Velocidex WinPmem v4.0.rc1).
    /// Auto-detecta: letra del CD-ROM (donde esta Tools\Memory\winpmem), particion de salida
    /// (label DFIR_OUTPUT), RAM fisica total.
    /// Criterio de exito: archivo .raw existe y tamano >= 95% de la RAM fisica total
    /// (v5.2.18 fix). Aplica SHA-256 streaming al final.
    /// </summary>
    internal class RamRunner
    {
        private readonly Action<string> _log;
        private readonly bool _useCmdWrapper;

        public RamRunner(Action<string> log, bool useCmdWrapper)
        {
            _log = log;
            _useCmdWrapper = useCmdWrapper;
        }

        public class Result
        {
            public string RawPath { get; set; }
            public long RawSize { get; set; }
            public string Sha256 { get; set; }
            public int ExitCode { get; set; }
            public TimeSpan Duration { get; set; }
            public bool SuccessDespiteExitCode { get; set; }
        }

        public async Task<Result> RunAsync(
            string cdRomDrive,    // ej "D:"
            string outputDrive,   // ej "E:"
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            // 1. Detect winpmem
            var winpmemPath = Path.Combine(cdRomDrive, "Tools", "Memory", "winpmem_mini_x64_rc2.exe");
            if (!File.Exists(winpmemPath))
            {
                throw new RamException("winpmem no encontrado en " + winpmemPath, -1);
            }

            // 2. Detect RAM size
            var totalRam = DetectTotalRam();
            _log("[+] RAM fisica detectada: " + HashUtil.FormatBytes(totalRam));

            // 3. Build output path
            var host = Environment.MachineName;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var rawPath = Path.Combine(outputDrive, "RAM-" + host + "-" + stamp + ".raw");
            _log("[+] Output: " + rawPath);

            // 4. Check free space (RAM + 10% overhead)
            var free = new DriveInfo(outputDrive).AvailableFreeSpace;
            var needed = (long)(totalRam * 1.1);
            if (free < needed)
            {
                _log("[!] Espacio libre: " + HashUtil.FormatBytes(free) + " < necesario: " + HashUtil.FormatBytes(needed));
                _log("[!] Continuando igual (puede fallar si no hay espacio).");
            }

            // 5. Launch winpmem
            Process proc;
            if (_useCmdWrapper)
            {
                var wrapperArgs = "/c winpmem_mini_x64_rc2.exe \"" + rawPath + "\"";
                _log("[+] cmd /c winpmem (wrapper mode)");
                var psi = new ProcessStartInfo("cmd.exe", wrapperArgs)
                {
                    WorkingDirectory = Path.GetDirectoryName(winpmemPath),
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
                var psi = new ProcessStartInfo(winpmemPath, "\"" + rawPath + "\"")
                {
                    WorkingDirectory = Path.GetDirectoryName(winpmemPath),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
                proc = Process.Start(psi);
            }
            proc.StandardInput.Close();

            // Stream output (winpmem prints progress like "Progress: 23.45%")
            var lastReportBytes = 0L;
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                _log("[winpmem] " + e.Data);
                // Parse percent if present
                var pctIdx = e.Data.IndexOf("Progress:", StringComparison.OrdinalIgnoreCase);
                if (pctIdx >= 0)
                {
                    var pct = ParsePercent(e.Data, pctIdx);
                    if (pct >= 0)
                    {
                        var approx = (long)(totalRam * pct / 100.0);
                        if (approx > lastReportBytes)
                        {
                            lastReportBytes = approx;
                        }
                    }
                }
            };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _log("[winpmem-err] " + e.Data); };
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
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            proc.WaitForExit();
            var exit = proc.ExitCode;
            sw.Stop();

            // 6. Check file
            if (!File.Exists(rawPath))
            {
                throw new RamException("winpmem no genero el archivo .raw. Exit: " + exit, exit);
            }
            var rawSize = new FileInfo(rawPath).Length;
            _log("[+] winpmem .raw: " + HashUtil.FormatBytes(rawSize));

            // 7. v5.2.18 fix: capture is OK if file >= 95% of RAM even if exit != 0
            var successDespiteExit = false;
            if (exit != 0 && rawSize >= (long)(totalRam * 0.95))
            {
                _log("[!] winpmem exit " + exit + " pero .raw al " +
                     (rawSize * 100.0 / totalRam).ToString("F1") + "% de RAM fisica.");
                _log("[!] Captura considerada exitosa pese a exit code != 0 (v5.2.18 fix).");
                successDespiteExit = true;
            }
            else if (exit != 0)
            {
                throw new RamException("winpmem fallo exit " + exit + " (raw=" + HashUtil.FormatBytes(rawSize) +
                                       " < 95% de RAM " + HashUtil.FormatBytes(totalRam) + ")", exit);
            }

            // 9. SHA-256
            _log("[+] SHA-256 calculando (" + HashUtil.FormatBytes(rawSize) + ")...");
            var sha256 = HashUtil.ComputeSha256Streaming(rawPath);
            _log("[+] SHA-256: " + sha256);

            return new Result
            {
                RawPath = rawPath,
                RawSize = rawSize,
                Sha256 = sha256,
                ExitCode = exit,
                Duration = sw.Elapsed,
                SuccessDespiteExitCode = successDespiteExit
            };
        }

        private static long DetectTotalRam()
        {
            long total = 0;
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
                foreach (var obj in searcher.Get())
                {
                    var cap = obj["Capacity"];
                    if (cap != null) total += Convert.ToInt64(cap);
                }
            }
            catch
            {
                // Fallback
                var cs = new ManagementObject("Win32_ComputerSystem");
                total = Convert.ToInt64(cs["TotalPhysicalMemory"]);
            }
            if (total == 0) total = 4L * 1024 * 1024 * 1024; // 4 GiB fallback
            return total;
        }

        private static double ParsePercent(string line, int idx)
        {
            try
            {
                var rest = line.Substring(idx + "Progress:".Length).Trim();
                // Find % sign or just a number
                var pctStr = rest.TrimEnd('%', ' ', '\r', '\n');
                // Extract leading number
                var sb = new System.Text.StringBuilder();
                foreach (var c in pctStr)
                {
                    if (char.IsDigit(c) || c == '.') sb.Append(c);
                    else break;
                }
                double pct;
                return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out pct) ? pct : -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    internal class RamException : Exception
    {
        public int ExitCode { get; set; }
        public RamException(string msg, int code) : base(msg) { ExitCode = code; }
    }
}