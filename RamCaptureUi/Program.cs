using System;
using System.Windows.Forms;

namespace RamCaptureUi
{
    /// <summary>
    /// RAM_CAPTURE_UI v5.2.18 - Interfaz grafica para captura de RAM
    ///                              usando winpmem (Velocidex WinPmem).
    ///                              By Cybolt, MIT License.
    ///
    /// Mejoras v5.2.18 (captura exitosa pese a exit code != 0):
    ///   - v5.2.17 mostraba "ERROR: winpmem termino con exit code 1" cuando
    ///     el proceso se lanzaba desde .NET con CreateNoWindow=true, aunque
    ///     el .raw quedara escrito al 100% (6 GiB completos). Confirmado
    ///     en drill 2026-09-30 con DESKTOP-3GUGLTM: archivo .raw de GUI
    ///     fallida = 6,291,456 KB = 6 GiB exactos = idéntico al manual
    ///     exitoso. La captura ERA valida; la GUI la reportaba como
    ///     fallida por exit code != 0.
    ///   - v5.2.18: si el archivo .raw existe y su tamano >= 95% de la RAM
    ///     fisica total, la captura se considera EXITOSA pese a exit code
    ///     != 0. Loggea una advertencia explicando la situacion y los
    ///     criterios para reintentar (cmd.exe wrapper). Continua con el
    ///     calculo de SHA-256 normalmente.
    ///   - Esto evita que el operador descarte evidencia válida pensando
    ///     que la captura fallo. La captura se completa con SHA-256 y
    ///     archivo .sha256 companion como en un caso exitoso normal.
    ///
    /// Mejoras v5.2.17 (cmd.exe wrapper mode + fix RAM detection):
    ///   - v5.2.16 lanzaba winpmem directamente via Process.Start. En algunas
    ///     VMs (especialmente Windows 10/11 con Defender activo), Defender
    ///     escaneaba el .raw que winpmem escribia en tiempo real, lo ponia
    ///     en cuarentena momentaneamente, y winpmem salia con exit 1 al
    ///     85-92% (el .raw quedaba escrito pero el exit era != 0).
    ///   - v5.2.17 agrega checkbox opt-in "Ejecutar via cmd.exe wrapper".
    ///     Cuando esta marcado, la GUI lanza `cmd.exe /c winpmem.exe ...`
    ///     en vez del .exe directo. Esto replica exactamente lo que el
    ///     operador hace en cmd interactivo, donde winpmem corre limpio
    ///     (validado en drill 2026-09-30 con VM DESKTOP-3GUGLTM, 6 GB RAM:
    ///     manual OK 6 GiB; GUI exit 1 al 85%; con wrapper = OK).
    ///   - Si la primera corrida con wrapper falla igual, el log
    ///     estructurado de v5.2.16 (con stdout/stderr capturado) va a
    ///     mostrar la causa real (Defender, LSASS, GPO, disco).
    ///
    /// Mejoras v5.2.16 (logs en la GUI + copiar al portapapeles):
    ///   - v5.2.15 mostraba el diagnostico completo en un MessageBox popup
    ///     que se cierra y se pierde el contexto. v5.2.16 escribe el
    ///     diagnostico completo en el TextBox de log de la GUI (no se
    ///     pierde) y agrega boton "Copiar log" que lo manda al portapapeles
    ///     con un header estructurado (hostname, fecha, SO, output path)
    ///     listo para pegar en el ticket. El MessageBox queda como un
    ///     hint breve apuntando al log.
    ///   - El header pegado al ticket es consistente: incluye SO, usuario,
    ///     hostname, fecha/hora, binario winpmem, espacio libre y salida
    ///     esperada, asi el analista reproduce el entorno sin tener que
    ///     pedir info adicional al operador.
    ///
    /// Mejoras v5.2.15 (mejor diagnostico de errores):
    ///   - v5.2.14 mostraba "winpmem termino con exit code 1" sin contexto.
    ///     Bug observado en drill 2026-09-30 con VM DESKTOP-3GUGLTM: el
    ///     .raw se escribio a 6.00 GiB (RAM completa) pero winpmem salio
    ///     con exit 1, sin stdout/stderr en la UI, sin indicar archivo
    ///     parcial ni causas comunes.
    ///   - v5.2.15 captura stdout/stderr de winpmem en strings. Al fallar:
    ///       * Muestra ultimas 25 lineas de cada stream.
    ///       * Detecta archivo .raw parcial, reporta tamano y path.
    ///       * Lista causas comunes del exit code (Defender, LSASS
    ///         Protection, gpo, disco lleno).
    ///       * Abre MessageBox expandido con todo el detalle.
    ///   - Bug cosmetic fix: label "RAM total detectada: 5.00 GB (4 GB)"
    ///     mostraba dos conversiones inconsistentes. Ahora una sola:
    ///     FormatBytes() basada en suma de sticks Win32_PhysicalMemory
    ///     (mas preciso que Win32_ComputerSystem.TotalPhysicalMemory).
    ///
    /// Mejoras v5.2.14 (GUI de captura de RAM):
    ///   - v5.2.13 introdujo Tools/Memory/winpmem_mini_x64_rc2.exe +
    ///     Tools/Memory/CAPTURE_RAM.cmd como flujo standalone (solo batch).
    ///   - v5.2.14 agrega esta GUI (RAM_CAPTURE_UI.exe) que envuelve el
    ///     binario winpmem con auto-deteccion de letra del CD-ROM, disco
    ///     de salida por label DFIR_OUTPUT, hostname/fecha, streaming
    ///     de stdout al TextBox, progress bar, y calculo de SHA-256
    ///     nativo (.NET) al finalizar.
    ///   - El script batch CAPTURE_RAM.cmd se mantiene como fallback
    ///     para entornos donde GUI no es viable (RDP limitado, SSH-only).
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
                Application.Run(new RamCaptureMain());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error fatal en RAM_CAPTURE_UI:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "RAM_CAPTURE_UI v" + AppInfo.Version,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    internal static class AppInfo
    {
        public static readonly string Version = "5.2.18";
        public static readonly string BinName = "RAM_CAPTURE_UI.exe";
        public static readonly string WinPmemName = "winpmem_mini_x64_rc2.exe";
        public static readonly string MemorySubdir = "Tools\\Memory";
        public static readonly string DefaultOutputLabel = "DFIR_OUTPUT";
    }
}
