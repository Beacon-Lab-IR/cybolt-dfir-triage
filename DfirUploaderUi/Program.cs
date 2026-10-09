using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DfirUploaderUi
{
    /// <summary>
    /// DFIR_UPLOADER_UI v5.3.0 - Interfaz grafica para subida de evidencia
    ///                           DFIR a bucket S3-compatible. By Cybolt, MIT License.
    ///
    /// Mejoras v5.3.0 (initial release):
    ///   - WinForms .NET 4.5.2 GUI con manifest requireAdministrator (UAC forzado).
    ///   - Config JSON remoto (gist o beaconlab.us) con credenciales rotables.
    ///   - HTTPS con cert validation obligatoria (HttpClient default).
    ///   - SHA-256 streaming (System.Security.Cryptography.SHA256 + FileStream, 1 MiB
    ///     buffer) para archivos grandes sin OOM.
    ///   - S3 multipart upload con SigV4 firmado en el cliente (sin AWSSDK, sin
    ///     dependency hell). Single PUT para < 8 MiB, multipart para >= 8 MiB.
    ///   - Progress bar global + status label por archivo + log textbox.
    ///   - Cancel via CancellationTokenSource + AbortMultipartUpload.
    ///   - Post-upload HEAD verify (Content-Length match).
    ///   - Audit log JSON escrito en disco y subido al bucket como sidecar
    ///     <prefix>/audit-<ts>.json.
    ///
    /// Licencia: MIT
    /// Copyright (c) 2026 Cybolt
    /// Ver archivo LICENSE en la raiz del ISO para el texto completo.
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
                Application.Run(new DfirUploaderMain());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error fatal en DFIR_UPLOADER_UI:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "DFIR_UPLOADER_UI v" + ApplicationVersion,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public const string ApplicationVersion = "5.3.0";
        public const string BinName = "DFIR_UPLOADER_UI.exe";
    }
}