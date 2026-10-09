using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DfirUploaderUi
{
    /// <summary>
    /// Interface comun para todos los protocolos de subida. Cada protocolo
    /// (S3, FTP, SFTP, SMB) implementa esto. La GUI y el audit log hablan
    /// solo con esta interface.
    /// </summary>
    internal interface IUploader
    {
        /// <summary>Protocolo implementado (s3, ftp, sftp, smb).</summary>
        string Protocol { get; }

        /// <summary>
        /// Sube un archivo. Devuelve UploadResult con metadata de la subida
        /// (ETag/ID, partes). Lanza en error.
        /// </summary>
        Task<UploadResult> UploadFileAsync(
            string localPath,
            string remoteKey,
            long sizeBytes,
            string sha256,
            CancellationToken ct);

        /// <summary>
        /// Verifica presencia/bytes del archivo subido. Lanza si no esta o
        /// el size no matchea. Usado post-upload para confirmar exito.
        /// </summary>
        Task<long> VerifyAsync(string remoteKey, CancellationToken ct);

        /// <summary>
        /// Aborta cualquier upload en vuelo. Llamado en cancel o error.
        /// Idempotente.
        /// </summary>
        Task AbortAllAsync();
    }

    /// <summary>
    /// Factory: crea el uploader correcto segun UploadConfig.Protocol.
    /// Si Protocol es null/vacio, detecta por URL scheme o default a S3.
    /// </summary>
    internal static class UploaderFactory
    {
        public static IUploader Create(UploadConfig config, Action<string> log, IProgress<UploadProgress> progress)
        {
            if (config == null) throw new ArgumentNullException("config");

            // Detect protocol
            var protocol = string.IsNullOrEmpty(config.Protocol)
                ? DetectProtocol(config)
                : config.Protocol.ToLowerInvariant();

            log("[+] Protocolo: " + protocol);

            switch (protocol)
            {
                case "s3":
                    return new S3Uploader(config, log, progress);
                case "ftp":
                    return new FtpUploader(config, log, progress);
                case "sftp":
                    return new SftpUploader(config, log, progress);
                case "smb":
                    return new SmbUploader(config, log, progress);
                default:
                    throw new ArgumentException("Protocolo no soportado: " + protocol);
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

            // Fallback: si tiene campos FTP, es FTP; si SMB, SMB; etc.
            if (!string.IsNullOrEmpty(config.FtpHost)) return "ftp";
            if (!string.IsNullOrEmpty(config.SftpHost)) return "sftp";
            if (!string.IsNullOrEmpty(config.SmbShare)) return "smb";

            // Default: S3 (compatibilidad hacia atras con v1 configs)
            return "s3";
        }
    }

    internal class UploadResult
    {
        public string ETag { get; set; }
        public string UploadId { get; set; }
        public int PartCount { get; set; }
    }

    internal class UploadProgress
    {
        public string CurrentFile { get; set; }
        public int PartNumber { get; set; }
        public int TotalParts { get; set; }
        public long BytesSent { get; set; }
        public long TotalBytes { get; set; }
    }

    internal class UploadException : Exception
    {
        public int HttpStatus { get; set; }
        public string ETag { get; set; }

        public UploadException(string msg, int status, string etag) : base(msg)
        {
            HttpStatus = status;
            ETag = etag;
        }
    }
}