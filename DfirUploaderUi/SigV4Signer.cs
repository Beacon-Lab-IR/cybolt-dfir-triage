using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DfirUploaderUi
{
    /// <summary>
    /// AWS Signature Version 4 manual signer for S3-compatible APIs.
    /// Implementacion en C# puro, sin NuGet. Cubre:
    ///   - SigV4 con payload vacio (CreateMultipartUpload, CompleteMultipartUpload,
    ///     AbortMultipartUpload, ListParts)
    ///   - SigV4 con STREAMING-AWS4-HMAC-SHA256-PAYLOAD (UploadPart)
    ///   - SigV4 con payload hasheado (single PUT < 8 MB)
    /// Referencia: https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
    /// </summary>
    internal static class SigV4Signer
    {
        /// <summary>
        /// Firma una peticion HTTP contra S3.
        /// Devuelve un Dictionary con los headers a agregar a la peticion
        /// (Authorization, x-amz-date, x-amz-content-sha256, x-amz-security-token si aplica).
        /// </summary>
        /// <param name="httpMethod">PUT, POST, GET, DELETE, HEAD.</param>
        /// <param name="endpoint">URL completa del endpoint S3 (ej: https://inc-lena.s3.g.megas4.com).</param>
        /// <param name="canonicalUri">Path del recurso (ej: /bucket/key). Empieza con /.</param>
        /// <param name="canonicalQueryString">Query string canonical (alfabetico). Vacio si no hay.</param>
        /// <param name="headersToSign">Headers HTTP que se incluiran en la firma. El signer agrega host, x-amz-date, x-amz-content-sha256.</param>
        /// <param name="payloadHashMode">Tipo de payload hash a usar (STREAMING, UNSIGNED, EMPTY, SHA256).</param>
        /// <param name="payloadBytes">Solo si payloadHashMode = SHA256. Bytes a hashear.</param>
        /// <param name="accessKey">Access Key ID del IAM user/role.</param>
        /// <param name="secretKey">Secret Access Key. Solo vive en memoria durante la firma.</param>
        /// <param name="region">Region S3 (ej: us-east-1).</param>
        /// <param name="service">Servicio (siempre "s3").</param>
        /// <param name="sessionToken">Opcional, para credenciales temporales STS.</param>
        public static IDictionary<string, string> SignRequest(
            string httpMethod,
            string endpoint,
            string canonicalUri,
            string canonicalQueryString,
            IDictionary<string, string> headersToSign,
            PayloadHashMode payloadHashMode,
            byte[] payloadBytes,
            string accessKey,
            string secretKey,
            string region,
            string service,
            string sessionToken)
        {
            if (string.IsNullOrEmpty(httpMethod)) throw new ArgumentNullException("httpMethod");
            if (string.IsNullOrEmpty(endpoint)) throw new ArgumentNullException("endpoint");
            if (string.IsNullOrEmpty(accessKey)) throw new ArgumentNullException("accessKey");
            if (string.IsNullOrEmpty(secretKey)) throw new ArgumentNullException("secretKey");

            var now = DateTime.UtcNow;
            var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            // Step 1: Calculate payload hash
            string payloadHash;
            switch (payloadHashMode)
            {
                case PayloadHashMode.Empty:
                    payloadHash = EMPTY_PAYLOAD_HASH;
                    break;
                case PayloadHashMode.Unsigned:
                    payloadHash = UNSIGNED_PAYLOAD;
                    break;
                case PayloadHashMode.Streaming:
                    payloadHash = STREAMING_PAYLOAD;
                    break;
                case PayloadHashMode.Sha256:
                    if (payloadBytes == null) throw new ArgumentNullException("payloadBytes", "SHA256 mode requires payload bytes");
                    using (var sha = SHA256.Create())
                    {
                        var hash = sha.ComputeHash(payloadBytes);
                        payloadHash = ToHex(hash);
                    }
                    break;
                default:
                    throw new ArgumentException("Unknown payload hash mode");
            }

            // Step 2: Determine host header from endpoint URL
            var uri = new Uri(endpoint);
            var hostHeader = uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);

            // Step 3: Build canonical headers
            var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in headersToSign)
            {
                headers[kv.Key.ToLowerInvariant()] = kv.Value.Trim();
            }
            headers["host"] = hostHeader;
            headers["x-amz-date"] = amzDate;
            headers["x-amz-content-sha256"] = payloadHash;
            if (!string.IsNullOrEmpty(sessionToken))
            {
                headers["x-amz-security-token"] = sessionToken;
            }

            // Step 4: Build canonical request
            var canonicalHeaders = string.Empty;
            var signedHeaders = string.Empty;
            foreach (var kv in headers)
            {
                canonicalHeaders += kv.Key + ":" + kv.Value + "\n";
                signedHeaders += kv.Key + ";";
            }
            signedHeaders = signedHeaders.TrimEnd(';');

            var canonicalRequest = httpMethod.ToUpperInvariant() + "\n" +
                                 (canonicalUri ?? string.Empty) + "\n" +
                                 (canonicalQueryString ?? string.Empty) + "\n" +
                                 canonicalHeaders + "\n" +
                                 signedHeaders + "\n" +
                                 payloadHash;

            var hashedCanonicalRequest = ToHex(SHA256Hash.Create(canonicalRequest));

            // Step 5: Build string to sign
            var credentialScope = dateStamp + "/" + region + "/" + service + "/" + "aws4_request";
            var stringToSign = "AWS4-HMAC-SHA256\n" +
                               amzDate + "\n" +
                               credentialScope + "\n" +
                               hashedCanonicalRequest;

            // Step 6: Calculate signing key
            var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
            var kDate = HmacSha256(kSecret, dateStamp);
            var kRegion = HmacSha256(kDate, region);
            var kService = HmacSha256(kRegion, service);
            var kSigning = HmacSha256(kService, "aws4_request");

            // Step 7: Calculate signature
            var signature = ToHex(HmacSha256(kSigning, stringToSign));

            // Step 8: Build Authorization header
            var authorization = "AWS4-HMAC-SHA256 " +
                                "Credential=" + accessKey + "/" + credentialScope + ", " +
                                "SignedHeaders=" + signedHeaders + ", " +
                                "Signature=" + signature;

            // Build result dictionary
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            result["Authorization"] = authorization;
            result["x-amz-date"] = amzDate;
            result["x-amz-content-sha256"] = payloadHash;
            foreach (var kv in headersToSign)
            {
                // Preserve original-case header names in result
                if (!result.ContainsKey(kv.Key))
                {
                    result[kv.Key] = kv.Value;
                }
            }
            if (!string.IsNullOrEmpty(sessionToken))
            {
                result["x-amz-security-token"] = sessionToken;
            }
            return result;
        }

        // Payload hash constants
        public const string EMPTY_PAYLOAD_HASH = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        public const string UNSIGNED_PAYLOAD = "UNSIGNED-PAYLOAD";
        public const string STREAMING_PAYLOAD = "STREAMING-AWS4-HMAC-SHA256-PAYLOAD";

        private static byte[] HmacSha256(byte[] key, string data)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    internal enum PayloadHashMode
    {
        /// <summary>Empty body. Hash = SHA256("").</summary>
        Empty,
        /// <summary>Streaming/chunked body. Hash = "UNSIGNED-PAYLOAD" or "STREAMING-AWS4-HMAC-SHA256-PAYLOAD".</summary>
        Unsigned,
        Streaming,
        /// <summary>Small fixed body. Compute SHA256 of bytes.</summary>
        Sha256
    }

    internal static class SHA256Hash
    {
        public static byte[] Create(string data)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }
    }
}