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
    /// Carga y valida el config JSON remoto. HTTPS con cert validation
    /// obligatoria (HttpClient default).
    ///
    /// Schema del config (v1):
    /// {
    ///   "version": 1,
    ///   "bucket": "inc-lena",
    ///   "endpoint": "https://inc-lena.s3.g.megas4.com",
    ///   "region": "us-east-1",
    ///   "prefix": "clients/acme-corp/incident-2026-10-08/",
    ///   "expires_at": "2026-10-08T23:00:00Z",
    ///   "access_key_id": "AKIA...",
    ///   "secret_access_key": "...",
    ///   "session_token": null,        // opcional, para STS temp creds
    ///   "chunk_size_bytes": 8388608   // opcional, default 8 MiB
    /// }
    /// </summary>
    internal static class ConfigFetcher
    {
        /// <summary>
        /// Fetch + parse + validate el config JSON. Devuelve null si falla
        /// (mensaje en outError).
        /// </summary>
        public static async Task<UploadConfig> FetchAsync(string url, CancellationToken ct, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(url)) return Fail(log, "URL del config vacia");
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(log, "Solo HTTPS esta permitido (cert validation obligatoria). URL debe empezar con https://");
            }

            log("[+] HTTPS GET " + url);
            string body;
            try
            {
                // HttpClient con default cert validation (ServicePointManager).
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
                        return Fail(log, "Config no encontrado en el server (404). El operador debe de generar uno nuevo.");
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        return Fail(log, "Config no disponible (HTTP " + statusCode + "). Verifica la URL con el operador.");
                    }
                    body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                return Fail(log, "Error HTTPS: " + ex.Message + " (cert invalido? server caido? DNS?)");
            }
            catch (TaskCanceledException)
            {
                return Fail(log, "Timeout (30s) fetching config");
            }
            catch (Exception ex)
            {
                return Fail(log, "Error desconocido fetching config: " + ex.GetType().Name + ": " + ex.Message);
            }

            // Parse JSON
            UploadConfig config;
            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 64 * 1024; // 64 KB cap (config is small)
                config = serializer.Deserialize<UploadConfig>(body);
            }
            catch (Exception ex)
            {
                return Fail(log, "JSON invalido: " + ex.Message);
            }
            if (config == null) return Fail(log, "JSON parseado pero resultado null");

            // Validate required fields
            var missing = new List<string>();
            if (config.Version != 1) missing.Add("version (debe ser 1)");
            if (string.IsNullOrWhiteSpace(config.Bucket)) missing.Add("bucket");
            if (string.IsNullOrWhiteSpace(config.Endpoint)) missing.Add("endpoint");
            if (string.IsNullOrWhiteSpace(config.Region)) missing.Add("region");
            if (string.IsNullOrWhiteSpace(config.Prefix)) missing.Add("prefix");
            if (config.ExpiresAt == default(DateTime)) missing.Add("expires_at");
            if (string.IsNullOrWhiteSpace(config.AccessKeyId)) missing.Add("access_key_id");
            if (string.IsNullOrWhiteSpace(config.SecretAccessKey)) missing.Add("secret_access_key");
            if (missing.Count > 0)
            {
                return Fail(log, "Config incompleto. Faltan: " + string.Join(", ", missing));
            }

            // Validate expires_at
            var now = DateTime.UtcNow;
            var timeLeft = config.ExpiresAt - now;
            if (timeLeft.TotalMinutes < 30)
            {
                return Fail(log, "Config EXPIRADO o por expirar (vence en " +
                                 timeLeft.TotalMinutes.ToString("F0") + " min, " +
                                 "minimo requerido 30 min). Pedile al operador un config nuevo.");
            }

            // Validate endpoint is HTTPS
            if (!config.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(log, "Endpoint no es HTTPS: " + config.Endpoint);
            }

            // Validate prefix
            if (!config.Prefix.EndsWith("/")) config.Prefix += "/";

            // Default chunk size
            if (config.ChunkSizeBytes <= 0) config.ChunkSizeBytes = 8 * 1024 * 1024; // 8 MiB

            log("[+] Config OK: bucket=" + config.Bucket +
                " prefix=" + config.Prefix +
                " expira en " + timeLeft.TotalMinutes.ToString("F0") + " min" +
                " chunk=" + (config.ChunkSizeBytes / 1024 / 1024) + " MiB");
            return config;
        }

        private static UploadConfig Fail(Action<string> log, string msg)
        {
            log("[!] " + msg);
            return null;
        }

        private const string ApplicationVersion = "5.3.0";
    }

    /// <summary>
    /// Schema del config JSON (v1).
    /// </summary>
    internal class UploadConfig
    {
        public int Version { get; set; }
        public string Bucket { get; set; }
        public string Endpoint { get; set; }
        public string Region { get; set; }
        public string Prefix { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
        public string SessionToken { get; set; }
        public long ChunkSizeBytes { get; set; }
    }
}