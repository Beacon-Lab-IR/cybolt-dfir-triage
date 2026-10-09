using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace DfirUploaderUi
{
    /// <summary>
    /// SHA-256 streaming hasher. Reads from a FileStream in 1 MB chunks,
    /// so memory footprint stays bounded (~1 MB + CryptoStream buffer)
    /// regardless of file size. Works for 32 GB .raw files without OOM.
    /// </summary>
    internal static class HashUtil
    {
        private const int BufferSize = 1 * 1024 * 1024; // 1 MiB

        /// <summary>
        /// Compute SHA-256 of a file, streamed. Returns lowercase hex string.
        /// Throws on IO error. Honors cancellation between buffer reads.
        /// </summary>
        public static string ComputeSha256Streaming(string path, CancellationToken ct = default(CancellationToken))
        {
            if (path == null) throw new ArgumentNullException("path");
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(buffer, 0, 0);
                return ToHex(sha.Hash);
            }
        }

        /// <summary>
        /// Lowercase hex without dashes, 64 chars, suitable for .sha256 sidecar file.
        /// </summary>
        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}