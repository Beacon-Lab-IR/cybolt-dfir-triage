using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DfirUploaderUi
{
    /// <summary>
    /// Carga y valida el config JSON remoto o de archivo local. HTTPS con cert
    /// validation obligatoria (HttpClient default), o file:// / local path.
    ///
    /// Schema del config (v2):
    /// {
    ///   "version": 2,
    ///   "protocol": "s3" | "ftp" | "sftp" | "smb",   // opcional, auto-detect
    ///
    ///   // Common
    ///   "expires_at": "...",
    ///
    ///   // S3-specific
    ///   "bucket": "...", "endpoint": "https://...", "region": "...",
    ///   "prefix": "...", "access_key_id": "...", "secret_access_key": "...",
    ///   "session_token": null, "chunk_size_bytes": 8388608,
    ///
    ///   // FTP-specific
    ///   "ftp_host": "...", "ftp_port": 21, "ftp_username": "...",
    ///   "ftp_password": "...", "ftp_path": "/uploads/", "ftp_passive": true,
    ///   "ftp_ssl": false,
    ///
    ///   // SFTP-specific
    ///   "sftp_host": "...", "sftp_port": 22, "sftp_username": "...",
    ///   "sftp_password": "...", "sftp_key_path": null, "sftp_path": "/uploads/",
    ///
    ///   // SMB-specific
    ///   "smb_share": "\\\\server\\share", "smb_username": "DOMAIN\\user",
    ///   "smb_password": "...", "smb_path": "uploads\\"
    /// }
    /// </summary>
    internal static class ConfigFetcher
    {
        public static async Task<UploadConfig> FetchAsync(string urlOrPath, CancellationToken ct, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(urlOrPath)) return Fail(log, "URL o path del config vacio");

            string body;

            if (urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                body = await FetchHttpsAsync(urlOrPath, ct, log).ConfigureAwait(false);
            }
            else if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(log, "Solo HTTPS esta permitido (cert validation obligatoria). URL debe empezar con https://");
            }
            else
            {
                // Local file path (Windows or Unix style)
                body = ReadLocalFile(urlOrPath, log);
            }

            if (string.IsNullOrEmpty(body)) return null;

            // Parse JSON
            UploadConfig config;
            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 64 * 1024;
                config = serializer.Deserialize<UploadConfig>(body);
            }
            catch (Exception ex)
            {
                return Fail(log, "JSON invalido: " + ex.Message);
            }
            if (config == null) return Fail(log, "JSON parseado pero resultado null");

            // Validate version
            if (config.Version != 1 && config.Version != 2)
            {
                return Fail(log, "Version del config no soportada: " + config.Version + " (esperado 1 o 2)");
            }

            // Validate expires_at
            if (config.ExpiresAt == default(DateTime))
            {
                return Fail(log, "Falta expires_at en el config");
            }
            var now = DateTime.UtcNow;
            var timeLeft = config.ExpiresAt - now;
            if (timeLeft.TotalMinutes < 30)
            {
                return Fail(log, "Config EXPIRADO o por expirar (vence en " +
                                 timeLeft.TotalMinutes.ToString("F0") + " min, " +
                                 "minimo requerido 30 min). Pedile al operador un config nuevo.");
            }

            // Auto-detect protocol if not set
            if (string.IsNullOrEmpty(config.Protocol))
            {
                config.Protocol = DetectProtocol(config);
            }
            else
            {
                config.Protocol = config.Protocol.ToLowerInvariant();
            }

            // Validate per-protocol required fields
            ValidateProtocol(config, log);

            // Defaults
            if (config.ChunkSizeBytes <= 0 && config.Protocol == "s3")
                config.ChunkSizeBytes = 8 * 1024 * 1024;
            if (config.FtpPort <= 0 && config.Protocol == "ftp")
                config.FtpPort = 21;
            if (config.SftpPort <= 0 && config.Protocol == "sftp")
                config.SftpPort = 22;

            log("[+] Config OK: protocol=" + config.Protocol +
                " expira en " + timeLeft.TotalMinutes.ToString("F0") + " min");
            return config;
        }

        private static async Task<string> FetchHttpsAsync(string url, CancellationToken ct, Action<string> log)
        {
            log("[+] HTTPS GET " + url);
            try
            {
                using (var handler = new HttpClientHandler())
                using (var http = new HttpClient(handler))
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("DFIR_UPLOADER_UI/" + ApplicationVersion);
                    var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                    var statusCode = (int)resp.StatusCode;
                    log("[<] HTTP " + statusCode + " " + resp.ReasonPhrase);
                    if (statusCode == 404)
                    {
                        Fail(log, "Config no encontrado en el server (404). El operador debe de generar uno nuevo.");
                        return null;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        Fail(log, "Config no disponible (HTTP " + statusCode + "). Verifica la URL con el operador.");
                        return null;
                    }
                    return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                Fail(log, "Error HTTPS: " + ex.Message);
                return null;
            }
            catch (TaskCanceledException)
            {
                Fail(log, "Timeout (30s) fetching config");
                return null;
            }
        }

        private static string ReadLocalFile(string path, Action<string> log)
        {
            log("[+] Leyendo config local: " + path);
            try
            {
                // Trim quotes if present (e.g. user pastes a path with quotes from a shortcut)
                path = path.Trim('"', '\'');
                if (!File.Exists(path))
                {
                    Fail(log, "Archivo no encontrado: " + path);
                    return null;
                }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Fail(log, "Error leyendo archivo: " + ex.Message);
                return null;
            }
        }

        private static string DetectProtocol(UploadConfig config)
        {
            // Try endpoint URL scheme first
            var url = config.Endpoint ?? string.Empty;
            if (url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "s3";
            }
            if (url.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
            {
                return "sftp";
            }
            if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
            {
                return "ftp";
            }
            if (url.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                return "smb";
            }

            // Fallback: si tiene campos de un protocolo especifico
            if (!string.IsNullOrEmpty(config.FtpHost)) return "ftp";
            if (!string.IsNullOrEmpty(config.SftpHost)) return "sftp";
            if (!string.IsNullOrEmpty(config.SmbShare)) return "smb";

            // Default: S3 (compatibilidad hacia atras con v1 configs)
            return "s3";
        }

        private static void ValidateProtocol(UploadConfig config, Action<string> log)
        {
            switch (config.Protocol)
            {
                case "s3":
                    var missingS3 = new List<string>();
                    if (string.IsNullOrWhiteSpace(config.Bucket)) missingS3.Add("bucket");
                    if (string.IsNullOrWhiteSpace(config.Endpoint)) missingS3.Add("endpoint");
                    if (string.IsNullOrWhiteSpace(config.Region)) missingS3.Add("region");
                    if (string.IsNullOrWhiteSpace(config.Prefix)) missingS3.Add("prefix");
                    if (string.IsNullOrWhiteSpace(config.AccessKeyId)) missingS3.Add("access_key_id");
                    if (string.IsNullOrWhiteSpace(config.SecretAccessKey)) missingS3.Add("secret_access_key");
                    if (missingS3.Count > 0)
                    {
                        throw new ArgumentException("Config S3 incompleto. Faltan: " + string.Join(", ", missingS3));
                    }
                    if (!config.Prefix.EndsWith("/")) config.Prefix += "/";
                    if (!config.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !config.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Endpoint S3 debe empezar con https:// (o http:// solo para testing local)");
                    }
                    break;

                case "ftp":
                    var missingFtp = new List<string>();
                    if (string.IsNullOrWhiteSpace(config.FtpHost)) missingFtp.Add("ftp_host");
                    if (string.IsNullOrWhiteSpace(config.FtpUsername)) missingFtp.Add("ftp_username");
                    if (missingFtp.Count > 0)
                    {
                        throw new ArgumentException("Config FTP incompleto. Faltan: " + string.Join(", ", missingFtp));
                    }
                    if (string.IsNullOrEmpty(config.FtpPath)) config.FtpPath = "/";
                    if (!config.FtpPath.EndsWith("/")) config.FtpPath += "/";
                    break;

                case "sftp":
                    var missingSftp = new List<string>();
                    if (string.IsNullOrWhiteSpace(config.SftpHost)) missingSftp.Add("sftp_host");
                    if (string.IsNullOrWhiteSpace(config.SftpUsername)) missingSftp.Add("sftp_username");
                    if (string.IsNullOrEmpty(config.SftpPassword) && string.IsNullOrEmpty(config.SftpKeyPath))
                    {
                        missingSftp.Add("sftp_password o sftp_key_path");
                    }
                    if (missingSftp.Count > 0)
                    {
                        throw new ArgumentException("Config SFTP incompleto. Faltan: " + string.Join(", ", missingSftp));
                    }
                    if (string.IsNullOrEmpty(config.SftpPath)) config.SftpPath = "/";
                    if (!config.SftpPath.EndsWith("/")) config.SftpPath += "/";
                    break;

                case "smb":
                    var missingSmb = new List<string>();
                    if (string.IsNullOrWhiteSpace(config.SmbShare)) missingSmb.Add("smb_share");
                    if (string.IsNullOrWhiteSpace(config.SmbUsername)) missingSmb.Add("smb_username");
                    if (missingSmb.Count > 0)
                    {
                        throw new ArgumentException("Config SMB incompleto. Faltan: " + string.Join(", ", missingSmb));
                    }
                    if (string.IsNullOrEmpty(config.SmbPath)) config.SmbPath = "\\";
                    break;
            }
        }

        private static UploadConfig Fail(Action<string> log, string msg)
        {
            log("[!] " + msg);
            return null;
        }

        private const string ApplicationVersion = "5.3.0";
    }

    /// <summary>
    /// Schema del config JSON (v1 + v2).
    /// </summary>
    internal class UploadConfig
    {
        public int Version { get; set; }
        public string Protocol { get; set; }

        // S3-specific
        public string Bucket { get; set; }
        public string Endpoint { get; set; }
        public string Region { get; set; }
        public string Prefix { get; set; }
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
        public string SessionToken { get; set; }
        public long ChunkSizeBytes { get; set; }

        // FTP-specific
        public string FtpHost { get; set; }
        public int FtpPort { get; set; }
        public string FtpUsername { get; set; }
        public string FtpPassword { get; set; }
        public string FtpPath { get; set; }
        public bool FtpPassive { get; set; }
        public bool FtpSsl { get; set; }

        // SFTP-specific
        public string SftpHost { get; set; }
        public int SftpPort { get; set; }
        public string SftpUsername { get; set; }
        public string SftpPassword { get; set; }
        public string SftpKeyPath { get; set; }
        public string SftpPath { get; set; }

        // SMB-specific
        public string SmbShare { get; set; }
        public string SmbUsername { get; set; }
        public string SmbPassword { get; set; }
        public string SmbPath { get; set; }

        // Common
        public DateTime ExpiresAt { get; set; }
    }
}