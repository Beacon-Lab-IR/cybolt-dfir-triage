using System;
using System.Linq;
using System.Windows.Forms;

namespace DfirTriageUi
{
    /// <summary>
    /// CYBOLT_DFIR_TRIAGE v5.5.1 - Wizard consolidado KapeTriage + RAM Capture (opt-in).
    /// By Cybolt, MIT License.
    ///
    /// Modo CLI (no GUI) para testing: --test-targets &lt;bundleDir&gt;
    ///   - Lee bundleDir/targets-full.txt, filtra contra Targets/*.tkape,
    ///     muestra OK vs SKIP en stdout, sale 0 si pasa, 1 si error.
    ///   - Util para validar el filtro defensivo sin abrir el GUI.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Modo testing: --test-targets <bundleDir>
            if (args.Length >= 2 && args[0] == "--test-targets")
            {
                Environment.Exit(RunTargetsTest(args[1]));
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new DfirTriageMain());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error fatal en CYBOLT_DFIR_TRIAGE:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "CYBOLT_DFIR_TRIAGE v5.5.1",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Headless test del filtro defensivo. Devuelve 0 si OK (al menos
        /// 1 target existente), 1 si el bundle es inutilizable.
        /// </summary>
        private static int RunTargetsTest(string bundleDir)
        {
            Console.Out.NewLine = "\n";
            var runner = new KapeRunner(Console.WriteLine, useCmdWrapper: false);
            try
            {
                var result = runner.LoadAndFilterTargets(bundleDir);
                Console.WriteLine("=== Filter test result ===");
                Console.WriteLine($"Bundle dir        : {bundleDir}");
                Console.WriteLine($"Used fallback     : {result.UsedFallback}");
                Console.WriteLine($"Existing (OK)     : {result.Existing.Count}");
                Console.WriteLine($"Skipped (no .tkape): {result.Skipped.Count}");
                if (result.Existing.Count > 0)
                {
                    var previewList = result.Existing.Count > 10
                        ? result.Existing.Take(10).ToList()
                        : result.Existing;
                    var preview = string.Join(", ", previewList);
                    if (result.Existing.Count > 10) preview += ", ...";
                    Console.WriteLine($"Existing preview  : {preview}");
                }
                if (result.Skipped.Count > 0)
                {
                    Console.WriteLine("Skipped targets:");
                    foreach (var t in result.Skipped)
                        Console.WriteLine("  SKIP " + t);
                }
                Console.WriteLine($"Selected for KAPE --target: {result.Existing.ToCommaSeparated()}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex.Message);
                return 1;
            }
        }
    }

    internal static class ListStringExtensions
    {
        public static string ToStringList(this System.Collections.Generic.List<string> list)
        {
            return string.Join(", ", list);
        }

        public static string ToCommaSeparated(this System.Collections.Generic.List<string> list)
        {
            return string.Join(",", list);
        }
    }
}