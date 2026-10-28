using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>Outcome of checking one package against the trust chain.</summary>
    internal enum PackageTrustKind
    {
        /// <summary>No signature block at all (any package built before the trust chain).</summary>
        Unsigned,

        /// <summary>Signed by a key in <c>trusted-keys</c> and every listed file matches its hash.</summary>
        Trusted,

        /// <summary>Signed, but the key is not in the trusted set - the signature proves nothing here.</summary>
        UnknownKey,

        /// <summary>Signed by a trusted key, but the signature does not verify.</summary>
        InvalidSignature,

        /// <summary>The file list does not match the package contents (missing, extra code file or wrong hash).</summary>
        TamperedFiles
    }

    internal sealed class PackageTrustResult
    {
        public PackageTrustKind Kind { get; set; }
        public string KeyId { get; set; }
        public string Summary { get; set; }
        public readonly List<string> Problems = new List<string>();

        /// <summary>Tampering and a broken signature block the install; the rest are advisory.</summary>
        public bool IsBlocking =>
            Kind == PackageTrustKind.TamperedFiles || Kind == PackageTrustKind.InvalidSignature;

        public override string ToString() => Summary;
    }

    /// <summary>
    /// Optional trust chain for <c>.swapaddon</c> packages - the module platform's answer to
    /// "who built this and did anything change since".
    ///
    /// A package may carry a <c>signature.json</c> whose RSA-SHA256 signature covers the exact bytes
    /// of <c>manifest.json</c>, and that manifest carries the SHA-256 of every payload file. Public
    /// keys the user trusts live in <c>&lt;data root&gt;\trusted-keys\*.xml</c> (RSA public key XML,
    /// as written by <c>PclAddonPack --gen-key</c>).
    ///
    /// Policy: a signature from a trusted key + matching file hashes is "Trusted"; an unknown key is
    /// reported but not fatal (the user may install it deliberately); a signature that does not
    /// verify, or payload files that do not match their hashes, is blocking.
    /// </summary>
    internal static class PackageTrust
    {
        private const string SignatureEntry = "signature.json";
        private const string Algorithm = "RSA-SHA256";

        public static string TrustedKeyDirectory => Path.Combine(SwapPaths.DataRoot, "trusted-keys");

        public static PackageTrustResult Verify(string archivePath, AddonManifest manifest, byte[] manifestBytes)
        {
            var result = new PackageTrustResult { KeyId = string.Empty };

            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    var signature = ReadSignature(zip);
                    var filesOk = VerifyFiles(zip, manifest, result);

                    if (signature == null)
                    {
                        result.Kind = PackageTrustKind.Unsigned;
                        result.Summary = manifest.Files.Count == 0
                            ? "未签名（旧格式包，没有文件清单）"
                            : filesOk
                                ? $"未签名；文件校验通过（{manifest.Files.Count} 个）"
                                : "未签名，且文件校验失败";
                        return result;
                    }

                    result.KeyId = signature.KeyId ?? string.Empty;
                    var key = FindTrustedKey(signature.KeyId);
                    if (key == null)
                    {
                        result.Kind = filesOk ? PackageTrustKind.UnknownKey : PackageTrustKind.TamperedFiles;
                        result.Summary = filesOk
                            ? $"签名密钥不在受信任列表（{Short(result.KeyId)}）"
                            : "签名密钥未知，且文件校验失败";
                        return result;
                    }

                    if (!VerifySignature(key, manifestBytes, signature.Signature))
                    {
                        result.Kind = PackageTrustKind.InvalidSignature;
                        result.Summary = $"签名校验失败（密钥 {Short(result.KeyId)}）";
                        return result;
                    }

                    if (manifest.Files.Count == 0)
                    {
                        // Signed but without an inventory: the signature covers the manifest only, so
                        // the payload itself is unverified. Say so instead of claiming "trusted".
                        result.Kind = PackageTrustKind.Trusted;
                        result.Summary = $"已签名（密钥 {Short(result.KeyId)}），但包内没有文件清单";
                        result.Problems.Add("签名未覆盖任何负载文件（旧格式包）");
                        return result;
                    }

                    if (!filesOk)
                    {
                        result.Kind = PackageTrustKind.TamperedFiles;
                        result.Summary = "签名有效，但包内文件与清单不一致";
                        return result;
                    }

                    result.Kind = PackageTrustKind.Trusted;
                    result.Summary = $"已签名（密钥 {Short(result.KeyId)}），文件校验通过（{manifest.Files.Count} 个）";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Kind = PackageTrustKind.InvalidSignature;
                result.Summary = "信任校验异常：" + ex.Message;
                result.Problems.Add(ex.GetType().Name + ": " + ex.Message);
                return result;
            }
        }

        private sealed class SignatureBlock
        {
            public string Algorithm;
            public string KeyId;
            public byte[] Signature;
        }

        private static SignatureBlock ReadSignature(ZipArchive zip)
        {
            var entry = zip.GetEntry(SignatureEntry);
            if (entry == null) return null;

            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();

            var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (map == null) return null;

            string Value(string key) => map.TryGetValue(key, out var raw) ? Convert.ToString(raw) : null;
            var algorithm = Value("algorithm");
            var encoded = Value("signature");
            if (string.IsNullOrEmpty(encoded)) return null;
            if (!string.IsNullOrEmpty(algorithm) &&
                !string.Equals(algorithm, Algorithm, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不支持的签名算法：" + algorithm);

            return new SignatureBlock
            {
                Algorithm = Algorithm,
                KeyId = Value("keyId"),
                Signature = Convert.FromBase64String(encoded)
            };
        }

        /// <summary>
        /// Recomputes every listed file hash. Missing files, wrong hashes and unlisted code files all
        /// count as problems - an unlisted dll would otherwise be code nobody signed.
        /// </summary>
        private static bool VerifyFiles(ZipArchive zip, AddonManifest manifest, PackageTrustResult result)
        {
            // Packages built before the trust chain carry no inventory; there is nothing to compare,
            // and treating their unlisted dlls as tampering would block every legacy package.
            if (manifest.Files.Count == 0) return true;

            var ok = true;
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                var path = (file.Path ?? string.Empty).Replace('\\', '/');
                if (path.Length == 0) continue;
                listed.Add(path);

                var entry = zip.GetEntry(path);
                if (entry == null)
                {
                    ok = false;
                    result.Problems.Add("清单里的文件不存在：" + path);
                    continue;
                }

                var actual = HashEntry(entry);
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    ok = false;
                    result.Problems.Add($"文件哈希不一致：{path}（清单 {Short(file.Sha256)}，实际 {Short(actual)}）");
                }
            }

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var path = entry.FullName.Replace('\\', '/');
                if (string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(path, SignatureEntry, StringComparison.OrdinalIgnoreCase)) continue;
                if (listed.Contains(path)) continue;
                if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                ok = false;
                result.Problems.Add("未列入清单的代码文件：" + path);
            }

            if (!ok && result.Problems.Count > 0 && string.IsNullOrEmpty(result.Summary))
                result.Summary = "文件校验失败：" + result.Problems[0];
            return ok;
        }

        private static RSA FindTrustedKey(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return null;
            try
            {
                var directory = TrustedKeyDirectory;
                if (!Directory.Exists(directory)) return null;

                foreach (var file in Directory.GetFiles(directory, "*.xml"))
                {
                    try
                    {
                        var rsa = RSA.Create();
                        rsa.FromXmlString(File.ReadAllText(file));
                        if (string.Equals(KeyIdFor(rsa), keyId, StringComparison.OrdinalIgnoreCase))
                            return rsa;
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn($"受信任密钥无法读取（{Path.GetFileName(file)}）：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("读取受信任密钥目录失败：" + ex.Message);
            }
            return null;
        }

        private static bool VerifySignature(RSA key, byte[] manifestBytes, byte[] signature)
        {
            try
            {
                return key.VerifyData(manifestBytes ?? new byte[0], signature,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("签名校验异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>Stable key id: first 16 hex of SHA-256 over the public key XML.</summary>
        internal static string KeyIdFor(RSA key)
        {
            using (var sha = SHA256.Create())
            {
                var xml = key.ToXmlString(false);
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(xml));
                var text = new StringBuilder(32);
                for (var i = 0; i < 8; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }

        private static string HashEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static string Short(string value) =>
            string.IsNullOrEmpty(value) ? "(none)" : value.Length <= 16 ? value : value.Substring(0, 16) + "…";
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
