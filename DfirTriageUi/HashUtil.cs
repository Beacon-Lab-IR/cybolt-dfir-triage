using System;
using System.IO;
using System.Security.Cryptography;

namespace DfirTriageUi
{
    /// <summary>
    /// SHA-256 streaming hasher. Reads from a FileStream in 1 MB chunks.
    /// </summary>
    internal static class HashUtil
    {
        private const int BufferSize = 1 * 1024 * 1024;

        public static string ComputeSha256Streaming(string path)
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
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(buffer, 0, 0);
                return ToHex(sha.Hash);
            }
        }

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

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KiB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MiB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GiB";
        }
    }
}