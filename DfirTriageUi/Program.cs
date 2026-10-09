using System;
using System.Windows.Forms;

namespace DfirTriageUi
{
    /// <summary>
    /// CYBOLT_DFIR_TRIAGE v5.3.0 - Wizard consolidado KapeTriage + RAM Capture.
    /// By Cybolt, MIT License.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
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
                    "CYBOLT_DFIR_TRIAGE v5.3.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}