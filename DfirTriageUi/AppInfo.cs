using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CYBOLT_DFIR_TRIAGE")]
[assembly: AssemblyDescription("DFIR Triage Wizard - KAPE Triage + RAM capture - By Cybolt, MIT License")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Cybolt")]
[assembly: AssemblyProduct("CYBOLT-DFIR-TRIAGE")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Cybolt")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("a1c2e3f4-5b6c-7d8e-9f0a-1b2c3d4e5f6a")]

// Una sola version: la misma del tag git / ZIP / UI (ej. 0.3.2).
[assembly: AssemblyVersion("0.3.2.0")]
[assembly: AssemblyFileVersion("0.3.2.0")]

namespace DfirTriageUi
{
    /// <summary>
    /// Version unica del producto (Major.Minor.Build de AssemblyVersion).
    /// </summary>
    internal static class AppVersion
    {
        public static string Current
        {
            get
            {
                var v = typeof(AppVersion).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
