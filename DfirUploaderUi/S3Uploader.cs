using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DfirUploaderUi
{
    /// <summary>
    /// S3 multipart upload + verify worker. Habla con un solo bucket por
    /// UploadConfig. Usa SigV4 signing (SigV4Signer.cs) para cada API call.
    /// Implementa IUploader.
    /// </summary>
    internal class S3Uploader : IUploader
    {
        public string Protocol { get { return "s3"; } }

        private readonly UploadConfig _config;
        private readonly Action<string> _log;
        private readonly IProgress<UploadProgress> _progress;

        public S3Uploader(UploadConfig config, Action<string> log, IProgress<UploadProgress> progress)
        {
            _config = config;
            _log = log;
            _progress = progress;
        }

        public async Task<UploadResult> UploadFileAsync(
            string localPath, string remoteKey, long sizeBytes, string sha256, CancellationToken ct)
        {
            _log("[+] Subiendo " + Path.GetFileName(localPath) + " (" + FormatBytes(sizeBytes) + ") -> s3://" + _config.Bucket + "/" + remoteKey);

            if (sizeBytes < _config.ChunkSizeBytes)
            {
                return await UploadSingleAsync(localPath, remoteKey, ct).ConfigureAwait(false);
            }
            else
            {
                return await UploadMultipartAsync(localPath, remoteKey, sizeBytes, ct).ConfigureAwait(false);
            }
        }

        private async Task<UploadResult> UploadSingleAsync(string localPath, string remoteKey, CancellationToken ct)
        {
            byte[] bytes;
            using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms, 81920, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }

            var canonicalUri = "/" + _config.Bucket + "/" + remoteKey;
            var headers = new Dictionary<string, string>();
            var signed = SigV4Signer.SignRequest(
                "PUT", _config.Endpoint, canonicalUri, string.Empty,
                headers, PayloadHashMode.Sha256, bytes,
                _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);

            var uri = new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri);
            using (var handler = new HttpClientHandler())
            using (var http = new HttpClient(handler))
            {
                http.Timeout = TimeSpan.FromMinutes(30);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                foreach (var kv in signed) content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                var resp = await http.PutAsync(uri, content, ct).ConfigureAwait(false);
                var etag = resp.Headers.ETag != null ? resp.Headers.ETag.Tag : null;
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _log("[!] PUT fallo HTTP " + (int)resp.StatusCode + ": " + ExtractErrorMessage(body));
                    throw new UploadException("PUT failed", (int)resp.StatusCode, etag);
                }
                _log("[+] Single PUT OK (ETag=" + etag + ")");
                return new UploadResult
                {
                    ETag = etag != null ? etag.Trim('"') : null,
                    UploadId = null,
                    PartCount = 1
                };
            }
        }

        private async Task<UploadResult> UploadMultipartAsync(string localPath, string remoteKey, long totalSize, CancellationToken ct)
        {
            var canonicalUri = "/" + _config.Bucket + "/" + remoteKey;

            // Step 1: CreateMultipartUpload
            _log("[+] CreateMultipartUpload -> s3://" + _config.Bucket + "/" + remoteKey);
            var createXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                            "<CreateMultipartUpload xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">" +
                            "</CreateMultipartUpload>";
            var createBodyBytes = System.Text.Encoding.UTF8.GetBytes(createXml);
            string uploadId;

            var createHeaders = new Dictionary<string, string>();
            var createSigned = SigV4Signer.SignRequest(
                "POST", _config.Endpoint, canonicalUri, "uploads",
                createHeaders, PayloadHashMode.Empty, null,
                _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);

            using (var handler = new HttpClientHandler())
            using (var http = new HttpClient(handler))
            {
                http.Timeout = TimeSpan.FromMinutes(30);
                var content = new ByteArrayContent(createBodyBytes);
                foreach (var kv in createSigned) content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                var resp = await http.PostAsync(new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri + "?uploads"), content, ct).ConfigureAwait(false);
                var respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _log("[!] CreateMultipartUpload fallo HTTP " + (int)resp.StatusCode + ": " + ExtractErrorMessage(respBody));
                    throw new UploadException("CreateMultipartUpload failed", (int)resp.StatusCode, null);
                }
                uploadId = ExtractUploadId(respBody);
                _log("[+] UploadId=" + uploadId);
            }

            // Step 2: Upload parts
            var partEtags = new Dictionary<int, string>();
            try
            {
                using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, (int)_config.ChunkSizeBytes, FileOptions.SequentialScan))
                {
                    int partNumber = 1;
                    long bytesSent = 0;
                    var buffer = new byte[_config.ChunkSizeBytes];
                    int read;
                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        var partBytes = new byte[read];
                        Buffer.BlockCopy(buffer, 0, partBytes, 0, read);

                        var partQuery = "partNumber=" + partNumber + "&uploadId=" + Uri.EscapeDataString(uploadId);
                        var partHeaders = new Dictionary<string, string>();
                        var partSigned = SigV4Signer.SignRequest(
                            "PUT", _config.Endpoint, canonicalUri, partQuery,
                            partHeaders, PayloadHashMode.Unsigned, null,
                            _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);

                        var partUri = new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri + "?" + partQuery);
                        using (var handler = new HttpClientHandler())
                        using (var http = new HttpClient(handler))
                        {
                            http.Timeout = TimeSpan.FromMinutes(10);
                            var content = new ByteArrayContent(partBytes);
                            foreach (var kv in partSigned) content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                            var resp = await http.PutAsync(partUri, content, ct).ConfigureAwait(false);
                            if (!resp.IsSuccessStatusCode)
                            {
                                var errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                _log("[!] UploadPart " + partNumber + " fallo HTTP " + (int)resp.StatusCode + ": " + ExtractErrorMessage(errBody));
                                throw new UploadException("UploadPart " + partNumber + " failed", (int)resp.StatusCode, null);
                            }
                            var etag = resp.Headers.ETag != null ? resp.Headers.ETag.Tag.Trim('"') : null;
                            partEtags[partNumber] = etag;
                            bytesSent += read;
                            _log("[+] Part " + partNumber + " OK (ETag=" + etag + ", " + FormatBytes(bytesSent) + "/" + FormatBytes(totalSize) + ")");
                            _progress.Report(new UploadProgress
                            {
                                CurrentFile = Path.GetFileName(localPath),
                                PartNumber = partNumber,
                                TotalParts = (int)((totalSize + _config.ChunkSizeBytes - 1) / _config.ChunkSizeBytes),
                                BytesSent = bytesSent,
                                TotalBytes = totalSize
                            });
                        }
                        partNumber++;
                    }
                }

                // Step 3: CompleteMultipartUpload
                var completeXml = new System.Text.StringBuilder();
                completeXml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                completeXml.Append("<CompleteMultipartUpload xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
                foreach (var p in partEtags.OrderBy(kv => kv.Key))
                {
                    completeXml.Append("<Part><PartNumber>" + p.Key + "</PartNumber><ETag>\"" + p.Value + "\"</ETag></Part>");
                }
                completeXml.Append("</CompleteMultipartUpload>");
                var completeBytes = System.Text.Encoding.UTF8.GetBytes(completeXml.ToString());

                var completeQuery = "uploadId=" + Uri.EscapeDataString(uploadId);
                var completeHeaders = new Dictionary<string, string>();
                var completeSigned = SigV4Signer.SignRequest(
                    "POST", _config.Endpoint, canonicalUri, completeQuery,
                    completeHeaders, PayloadHashMode.Empty, null,
                    _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);

                using (var handler = new HttpClientHandler())
                using (var http = new HttpClient(handler))
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    var content = new ByteArrayContent(completeBytes);
                    foreach (var kv in completeSigned) content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    var resp = await http.PostAsync(new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri + "?" + completeQuery), content, ct).ConfigureAwait(false);
                    var respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _log("[!] CompleteMultipartUpload fallo HTTP " + (int)resp.StatusCode + ": " + ExtractErrorMessage(respBody));
                        throw new UploadException("CompleteMultipartUpload failed", (int)resp.StatusCode, null);
                    }
                    var finalEtag = ExtractEtag(respBody);
                    _log("[+] Complete OK (ETag=" + finalEtag + ")");
                    return new UploadResult
                    {
                        ETag = finalEtag,
                        UploadId = uploadId,
                        PartCount = partEtags.Count
                    };
                }
            }
            catch
            {
                await AbortMultipartAsync(canonicalUri, uploadId).ConfigureAwait(false);
                throw;
            }
        }

        private async Task AbortMultipartAsync(string canonicalUri, string uploadId)
        {
            try
            {
                _log("[!] AbortMultipartUpload " + uploadId);
                var abortQuery = "uploadId=" + Uri.EscapeDataString(uploadId);
                var abortHeaders = new Dictionary<string, string>();
                var abortSigned = SigV4Signer.SignRequest(
                    "DELETE", _config.Endpoint, canonicalUri, abortQuery,
                    abortHeaders, PayloadHashMode.Empty, null,
                    _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);
                using (var handler = new HttpClientHandler())
                using (var http = new HttpClient(handler))
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri + "?" + abortQuery));
                    foreach (var kv in abortSigned) msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    var resp = await http.SendAsync(msg).ConfigureAwait(false);
                    _log("[+] Abort HTTP " + (int)resp.StatusCode);
                }
            }
            catch (Exception abortEx)
            {
                _log("[!] Abort fallo: " + abortEx.Message);
            }
        }

        public async Task<long> VerifyAsync(string remoteKey, CancellationToken ct)
        {
            var canonicalUri = "/" + _config.Bucket + "/" + remoteKey;
            var headers = new Dictionary<string, string>();
            var signed = SigV4Signer.SignRequest(
                "HEAD", _config.Endpoint, canonicalUri, string.Empty,
                headers, PayloadHashMode.Empty, null,
                _config.AccessKeyId, _config.SecretAccessKey, _config.Region, "s3", _config.SessionToken);
            var uri = new Uri(_config.Endpoint.TrimEnd('/') + canonicalUri);
            using (var handler = new HttpClientHandler())
            using (var http = new HttpClient(handler))
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, uri);
                foreach (var kv in signed) msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new UploadException("HEAD verify failed", (int)resp.StatusCode, null);
                }
                var len = resp.Content.Headers.ContentLength ?? -1;
                _log("[+] HEAD OK s3://" + _config.Bucket + "/" + remoteKey + " Content-Length=" + len);
                return len;
            }
        }

        public async Task AbortAllAsync()
        {
            // S3 multipart abort is per-uploadId, handled in UploadMultipartAsync catch block.
            await Task.Delay(0);
        }

        private static string ExtractUploadId(string xml)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return doc.GetElementsByTagName("UploadId")[0].InnerText;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractEtag(string xml)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return doc.GetElementsByTagName("ETag")[0].InnerText.Trim('"');
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractErrorMessage(string xmlOrText)
        {
            if (string.IsNullOrEmpty(xmlOrText)) return "(empty body)";
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlOrText);
                return doc.GetElementsByTagName("Message")[0].InnerText;
            }
            catch
            {
                return xmlOrText.Length > 200 ? xmlOrText.Substring(0, 200) : xmlOrText;
            }
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