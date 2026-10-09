using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DfirUploaderUi
{
    /// <summary>
    /// Construye y serializa el audit log JSON. Se escribe en disco local
    /// (E:\DFIR-OUTPUT\upload-audit-<ts>.json) y se sube al bucket como
    /// sidecar <prefix>/audit.json.
    ///
    /// Estructura:
    /// {
    ///   "schema_version": 1,
    ///   "started_utc": "...",
    ///   "completed_utc": "...",
    ///   "operator": "...",
    ///   "case_id": "...",
    ///   "incident_date": "...",
    ///   "notes": "...",
    ///   "bucket": "...",
    ///   "endpoint": "...",
    ///   "prefix": "...",
    ///   "host": { "name": "...", "user": "...", "os": "..." },
    ///   "files": [
    ///     {
    ///       "local_path": "...",
    ///       "s3_key": "...",
    ///       "size_bytes": 1234,
    ///       "sha256": "...",
    ///       "etag": "...",
    ///       "upload_id": "...",       // multipart only
    ///       "parts": 16,              // multipart only
    ///       "started_utc": "...",
    ///       "completed_utc": "...",
    ///       "status": "OK"|"FAILED"|"CANCELLED",
    ///       "error": "..."
    ///     }
    ///   ]
    /// }
    /// </summary>
    internal class AuditLog
    {
        public int SchemaVersion { get; set; } = 2;
        public string StartedUtc { get; set; }
        public string CompletedUtc { get; set; }
        public string Operator { get; set; }
        public string CaseId { get; set; }
        public string IncidentDate { get; set; }
        public string Notes { get; set; }
        public string Protocol { get; set; }
        public string Bucket { get; set; }
        public string Endpoint { get; set; }
        public string Prefix { get; set; }
        public HostInfo Host { get; set; }
        public List<FileEntry> Files { get; set; }

        public class HostInfo
        {
            public string Name { get; set; }
            public string User { get; set; }
            public string Os { get; set; }
        }

        public class FileEntry
        {
            public string LocalPath { get; set; }
            public string S3Key { get; set; }
            public long SizeBytes { get; set; }
            public string Sha256 { get; set; }
            public string ETag { get; set; }
            public string UploadId { get; set; }
            public int Parts { get; set; }
            public string StartedUtc { get; set; }
            public string CompletedUtc { get; set; }
            public string Status { get; set; }
            public string Error { get; set; }
        }

        public static AuditLog Create(
            string operatorName,
            string caseId,
            string incidentDate,
            string notes,
            UploadConfig config)
        {
            var log = new AuditLog();
            log.StartedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            log.Operator = operatorName ?? Environment.UserName;
            log.CaseId = caseId ?? string.Empty;
            log.IncidentDate = incidentDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            log.Notes = notes ?? string.Empty;
            log.Protocol = string.IsNullOrEmpty(config.Protocol) ? "s3" : config.Protocol;
            log.Bucket = config.Bucket;
            log.Endpoint = config.Endpoint;
            log.Prefix = config.Prefix;
            log.Host = new HostInfo
            {
                Name = Environment.MachineName,
                User = Environment.UserName,
                Os = Environment.OSVersion.VersionString
            };
            log.Files = new List<FileEntry>();
            return log;
        }

        public void MarkCompleted()
        {
            CompletedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        public void AddFile(string localPath, string s3Key, long sizeBytes, string sha256)
        {
            Files.Add(new FileEntry
            {
                LocalPath = localPath,
                S3Key = s3Key,
                SizeBytes = sizeBytes,
                Sha256 = sha256,
                Status = "PENDING",
                StartedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            });
        }

        public void MarkFileResult(string localPath, string etag, string uploadId, int partCount, string status, string error)
        {
            var f = Files.Find(x => x.LocalPath == localPath);
            if (f == null) return;
            f.ETag = etag;
            f.UploadId = uploadId;
            f.Parts = partCount;
            f.Status = status;
            f.Error = error;
            f.CompletedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        public string Serialize()
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(this);
        }

        public bool WriteToDisk(string path, Action<string> log)
        {
            try
            {
                var json = Serialize();
                File.WriteAllText(path, json, new UTF8Encoding(false));
                log("[+] Audit log escrito: " + path);
                return true;
            }
            catch (Exception ex)
            {
                log("[!] No se pudo escribir audit log a disco: " + ex.Message);
                return false;
            }
        }
    }
}