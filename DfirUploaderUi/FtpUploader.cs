using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DfirUploaderUi
{
    /// <summary>
    /// FTP / FTPS uploader. Usa System.Net.FtpWebRequest (BCL, sin NuGet).
    /// Soporta FTP simple (puerto 21) y FTPS explicito (puerto 21 con TLS).
    /// PASSIVE mode default (mejor con NAT/firewall). RESTart para resumir.
    /// </summary>
    internal class FtpUploader : IUploader
    {
        public string Protocol { get { return _config.FtpSsl ? "ftps" : "ftp"; } }

        private readonly UploadConfig _config;
        private readonly Action<string> _log;
        private readonly IProgress<UploadProgress> _progress;

        // Track in-flight upload for AbortAllAsync
        private FtpWebRequest _inFlightRequest;

        public FtpUploader(UploadConfig config, Action<string> log, IProgress<UploadProgress> progress)
        {
            _config = config;
            _log = log;
            _progress = progress;
        }

        public async Task<UploadResult> UploadFileAsync(
            string localPath, string remoteKey, long sizeBytes, string sha256, CancellationToken ct)
        {
            var remoteUri = BuildRemoteUri(remoteKey);
            _log("[+] Subiendo " + Path.GetFileName(localPath) + " (" + FormatBytes(sizeBytes) + ") -> " + remoteUri);

            var req = (FtpWebRequest)WebRequest.Create(remoteUri);
            req.Method = WebRequestMethods.Ftp.UploadFile;
            req.UseBinary = true;
            req.UsePassive = _config.FtpPassive;
            if (_config.FtpSsl)
            {
                req.EnableSsl = true;
            }
            if (!string.IsNullOrEmpty(_config.FtpUsername))
            {
                req.Credentials = new NetworkCredential(_config.FtpUsername, _config.FtpPassword ?? string.Empty);
            }
            req.Timeout = 30 * 60 * 1000; // 30 min
            req.ReadWriteTimeout = 30 * 60 * 1000;
            req.ContentLength = sizeBytes;

            _inFlightRequest = req;
            try
            {
                using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var rs = await req.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[64 * 1024]; // 64 KiB buffer
                    int read;
                    long totalSent = 0;
                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await rs.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        totalSent += read;
                        _progress.Report(new UploadProgress
                        {
                            CurrentFile = Path.GetFileName(localPath),
                            PartNumber = 1,
                            TotalParts = 1,
                            BytesSent = totalSent,
                            TotalBytes = sizeBytes
                        });
                    }
                }

                using (var resp = (FtpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                {
                    var status = (int)resp.StatusCode;
                    if (status >= 200 && status < 300)
                    {
                        _log("[+] FTP upload OK (" + resp.StatusDescription + ")");
                        return new UploadResult
                        {
                            ETag = sha256, // No ETag from FTP, use sha256 as identifier
                            UploadId = null,
                            PartCount = 1
                        };
                    }
                    throw new UploadException("FTP upload failed", status, null);
                }
            }
            finally
            {
                _inFlightRequest = null;
            }
        }

        public async Task<long> VerifyAsync(string remoteKey, CancellationToken ct)
        {
            var req = (FtpWebRequest)WebRequest.Create(BuildRemoteUri(remoteKey));
            req.Method = WebRequestMethods.Ftp.GetDateTimestamp;
            req.UsePassive = _config.FtpPassive;
            if (_config.FtpSsl) req.EnableSsl = true;
            if (!string.IsNullOrEmpty(_config.FtpUsername))
            {
                req.Credentials = new NetworkCredential(_config.FtpUsername, _config.FtpPassword ?? string.Empty);
            }
            req.Timeout = 30 * 1000;
            try
            {
                using (var resp = (FtpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                {
                    _log("[+] FTP verify OK " + remoteKey);
                    // FTP doesn't easily return size via timestamp; we approximate
                    // with a SIZE command for accuracy.
                    return await GetSizeAsync(remoteKey, ct).ConfigureAwait(false);
                }
            }
            catch (WebException ex)
            {
                throw new UploadException("FTP verify failed: " + ex.Message, 0, null);
            }
        }

        private async Task<long> GetSizeAsync(string remoteKey, CancellationToken ct)
        {
            try
            {
                var req = (FtpWebRequest)WebRequest.Create(BuildRemoteUri(remoteKey));
                req.Method = WebRequestMethods.Ftp.GetFileSize;
                req.UsePassive = _config.FtpPassive;
                if (_config.FtpSsl) req.EnableSsl = true;
                if (!string.IsNullOrEmpty(_config.FtpUsername))
                {
                    req.Credentials = new NetworkCredential(_config.FtpUsername, _config.FtpPassword ?? string.Empty);
                }
                req.Timeout = 30 * 1000;
                using (var resp = (FtpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                {
                    return resp.ContentLength;
                }
            }
            catch
            {
                return -1; // unknown
            }
        }

        public Task AbortAllAsync()
        {
            try
            {
                if (_inFlightRequest != null)
                {
                    _inFlightRequest.Abort();
                }
            }
            catch { }
            return Task.Delay(0);
        }

        private Uri BuildRemoteUri(string remoteKey)
        {
            var host = _config.FtpHost;
            var port = _config.FtpPort > 0 ? _config.FtpPort : 21;
            var scheme = _config.FtpSsl ? "ftps" : "ftp";
            var path = (_config.FtpPath ?? "/").TrimEnd('/') + "/" + remoteKey;
            return new Uri(scheme + "://" + host + ":" + port + path);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KiB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MiB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GiB";
        }
    }
}