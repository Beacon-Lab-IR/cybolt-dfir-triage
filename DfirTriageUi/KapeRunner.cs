using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DfirTriageUi
{
    /// <summary>
    /// Orquesta la corrida de KAPE 1.3.0.2 con el target set configurable.
    /// Stage del kape.exe desde el bundle (carpeta del .exe) a la carpeta de salida.
    /// Run de kape.exe con --tsource / --target / --tdest / --targetdir / --zip / --hv / --gui / --vss.
    /// Importante: close stdin antes de Process.Start para evitar que KAPE quede
    /// bloqueado en Console.ReadKey() al final (problema v5.2.10).
    ///
    /// Targets list: lee <bundleDir>/targets-full.txt (un target por linea, # = comentario).
    /// Aplica filtro defensivo: si un target del .txt NO existe como .tkape en Targets/,
    /// se skipea en vez de fallar. Esto permite actualizar el .txt independientemente
    /// del kape.zip — si agregamos un target al .txt pero kape.zip no lo trae, lo skipea.
    ///
    /// KAPE 1.3.0.2 constraints:
    ///   - NO acepta --targets-list (probado, falla "target is required").
    ///   - Acepta multiples --target separados pero solo procesa el primero.
    ///   - Soporta comma-separated en --target singular.
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
        /// Resultado de LoadAndFilterTargets: lista de targets que pasaron el filtro
        /// (existen como .tkape en Targets/), los que se skipean, y raw text original
        /// para mostrar al usuario en el panel de deteccion.
        /// </summary>
        public class TargetFilterResult
        {
            public List<string> Existing { get; set; } = new List<string>();
            public List<string> Skipped { get; set; } = new List<string>();
            public string RawText { get; set; }
            public bool UsedFallback { get; set; }
        }

        /// <summary>
        /// Carga targets-full.txt del bundle y filtra los que NO existan como .tkape.
        /// Fallback si el .txt no existe: solo KapeTriage (modo safe).
        /// </summary>
        public TargetFilterResult LoadAndFilterTargets(string bundleDir)
        {
            var result = new TargetFilterResult();
            string targetsFile = Path.Combine(bundleDir, "targets-full.txt");

            // Fallback si no hay targets-full.txt
            if (!File.Exists(targetsFile))
            {
                _log("[!] targets-full.txt NO encontrado en " + bundleDir + ", fallback KapeTriage");
                result.Existing.Add("KapeTriage");
                result.RawText = "(fallback) KapeTriage";
                result.UsedFallback = true;
                return result;
            }

            // Leer raw para mostrar al usuario
            result.RawText = File.ReadAllText(targetsFile);

            // Parsear
            var requested = result.RawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                .ToList();

            // Filtrar contra Targets/
            var targetdir = Path.Combine(bundleDir, "Targets");
            if (!Directory.Exists(targetdir))
            {
                _log("[!] Targets/ NO encontrado en " + bundleDir + ", fallback KapeTriage");
                result.Existing.Add("KapeTriage");
                result.Skipped.AddRange(requested.Where(t => t != "KapeTriage"));
                result.UsedFallback = true;
                return result;
            }

            // Cache de .tkape existentes: Set<string> lowercase para lookup rapido
            var existingTkapes = new HashSet<string>(
                Directory.EnumerateFiles(targetdir, "*.tkape", SearchOption.AllDirectories)
                    .Select(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in requested)
            {
                if (existingTkapes.Contains(t))
                {
                    result.Existing.Add(t);
                }
                else
                {
                    result.Skipped.Add(t);
                }
            }

            // Safety net: si por algun motivo la interseccion dio vacia, al menos correr KapeTriage
            if (result.Existing.Count == 0)
            {
                _log("[!] Filtro resulto vacio, agregando KapeTriage como safety net");
                if (existingTkapes.Contains("KapeTriage"))
                {
                    result.Existing.Add("KapeTriage");
                }
                else
                {
                    // Hay algo mal con el bundle, mejor fallar loud que silent
                    throw new KapeException(
                        "Bundle sin targets usables (KapeTriage tampoco existe en Targets/). " +
                        "Reinstalar bundle.", -2);
                }
            }

            return result;
        }

        /// <summary>
        /// Args default full (targets-full.txt filtrado) + paths de corrida.
        /// No stagea; sirve para preview en la UI y para RunAsync.
        /// </summary>
        public class DefaultArgs
        {
            public string Args { get; set; }
            public string StageDir { get; set; }
            public string OutBase { get; set; }
            public string OutZipName { get; set; }
            public string TargetsDst { get; set; }
            public int TargetCount { get; set; }
        }

        /// <summary>
        /// Arma el comando full DFIR (targets-full.txt + --vss + paths reales).
        /// </summary>
        public DefaultArgs BuildDefaultArgs(string bundleRoot, string outputFolder, string sourceDrive)
        {
            var host = Environment.MachineName;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var outBase = Path.Combine(outputFolder, host + "-" + stamp);
            var outZipName = host + "-KAPE-" + stamp;
            var stageDir = Path.Combine(outputFolder, "_kape_stage");
            var targetsDst = Path.Combine(stageDir, "Targets");

            var bundleDir = bundleRoot.TrimEnd('\\') + "\\";
            var filterResult = LoadAndFilterTargets(bundleDir);

            _log("[+] Targets a procesar (" + filterResult.Existing.Count + "):");
            foreach (var t in filterResult.Existing)
                _log("    OK  " + t);
            if (filterResult.Skipped.Count > 0)
            {
                _log("[!] Targets skipped por no existir en Targets/ (" + filterResult.Skipped.Count + "):");
                foreach (var t in filterResult.Skipped)
                    _log("    SKIP " + t + ".tkape (no esta en el bundle)");
            }

            var targetsJoined = string.Join(",", filterResult.Existing);

            // Rutas entrecomilladas, sin barra final (pitfall CommandLineToArgvW).
            // Sin --sync / --moduledir / --mdest (bugs KAPE 1.3.0.2).
            var args = "--tsource " + Quote(sourceDrive) +
                       " --target " + Quote(targetsJoined) +
                       " --tdest " + Quote(outBase) +
                       " --targetdir " + Quote(targetsDst) +
                       " --zip " + Quote(outBase + "\\" + outZipName) +
                       " --hv nc,vm" +
                       " --gui" +
                       " --vss";

            return new DefaultArgs
            {
                Args = args,
                StageDir = stageDir,
                OutBase = outBase,
                OutZipName = outZipName,
                TargetsDst = targetsDst,
                TargetCount = filterResult.Existing.Count
            };
        }

        /// <summary>
        /// Corre KapeTriage. Si argsOverride no es null/vacio, usa ese string
        /// (tras stage); si no, arma el default full.
        /// </summary>
        public async Task<Result> RunAsync(
            string bundleRoot,
            string outputFolder,
            string sourceDrive,
            CancellationToken ct,
            string argsOverride = null)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                if (!Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                throw new KapeException("No se pudo crear la carpeta de salida '" + outputFolder + "': " + ex.Message, -1);
            }

            var prepared = BuildDefaultArgs(bundleRoot, outputFolder, sourceDrive);
            var stageDir = prepared.StageDir;
            var outBase = prepared.OutBase;
            var outZipName = prepared.OutZipName;

            // 1. Stage kape.exe + Targets/ (bundle puede ser read-only)
            var stageKape = Path.Combine(stageDir, "kape.exe");
            await StageFileAsync(Path.Combine(bundleRoot, "kape.exe"), stageKape, ct);
            await StageDirectoryAsync(Path.Combine(bundleRoot, "Targets"), prepared.TargetsDst, ct);

            var useOverride = !string.IsNullOrWhiteSpace(argsOverride);
            var args = useOverride ? argsOverride.Trim() : prepared.Args;
            if (useOverride)
            {
                _log("[+] Usando args editados por el operador");
                var tdest = TryGetFlagValue(args, "--tdest");
                var zipBase = TryGetFlagValue(args, "--zip");
                if (!string.IsNullOrEmpty(tdest)) outBase = tdest.TrimEnd('\\');
                if (!string.IsNullOrEmpty(zipBase))
                    outZipName = Path.GetFileName(zipBase.TrimEnd('\\'));
            }
            _log("[+] KAPE args: kape.exe " + args);

            // 2. Launch
            Process proc = null;
            if (_useCmdWrapper)
            {
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

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) _log("[KAPE] " + e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _log("[KAPE-err] " + e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

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

            var zipPath = FindLatestZip(outBase, outZipName);
            if (zipPath == null)
            {
                throw new KapeException("KAPE no genero ZIP. Exit code: " + exit, exit);
            }

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

        /// <summary>
        /// Extrae el valor de un flag (--tdest "path" o --tdest path) del string de args.
        /// </summary>
        internal static string TryGetFlagValue(string args, string flag)
        {
            if (string.IsNullOrEmpty(args) || string.IsNullOrEmpty(flag)) return null;
            var idx = args.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var i = idx + flag.Length;
            while (i < args.Length && char.IsWhiteSpace(args[i])) i++;
            if (i >= args.Length) return null;
            if (args[i] == '"')
            {
                i++;
                var end = args.IndexOf('"', i);
                if (end < 0) return args.Substring(i);
                return args.Substring(i, end - i);
            }
            var j = i;
            while (j < args.Length && !char.IsWhiteSpace(args[j])) j++;
            return args.Substring(i, j - i);
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private async Task StageFileAsync(string source, string dest, CancellationToken ct)        {
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
            // KAPE 1.3.0.2 escribe el ZIP DENTRO de --tdest, con prefijo de timestamp:
            //   {tdest}\{timestamp}_E__{basename}.zip
            // Validado en el drill 2026-09-23 con KAPE_TRIAGE_UI v5.2.10, que
            // buscaba por patron dentro de tdest.
            // OJO: buscar en Path.GetDirectoryName(outBase) (la carpeta PADRE)
            // es un bug: el ZIP no esta ahi, esta dentro de outBase.
            var candidates = new List<string>();

            // Principal: --tdest (outBase)
            try
            {
                if (Directory.Exists(outBase))
                    candidates.AddRange(Directory.GetFiles(outBase, "*" + outZipName + "*.zip"));
            }
            catch (Exception ex)
            {
                _log("[!] Busqueda de ZIP en tdest fallo: " + ex.Message);
            }

            // Fallback: carpeta padre, por si otra version de KAPE escribe ahi
            try
            {
                var parent = Path.GetDirectoryName(outBase);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    candidates.AddRange(Directory.GetFiles(parent, "*" + outZipName + "*.zip"));
            }
            catch (Exception ex)
            {
                _log("[!] Busqueda de ZIP en la carpeta padre fallo: " + ex.Message);
            }

            if (candidates.Count == 0) return null;

            // Mas reciente por LastWriteTime (KAPE escribe uno, pero el operador
            // puede re-correr la adquisicion el mismo dia).
            return candidates.OrderByDescending(f => File.GetLastWriteTime(f)).First();
        }
    }

    internal class KapeException : Exception
    {
        public int ExitCode { get; set; }
        public KapeException(string msg, int code) : base(msg) { ExitCode = code; }
    }
}