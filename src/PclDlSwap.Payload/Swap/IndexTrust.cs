using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Verifies the optional RSA-SHA256 signature of an addon repository index
    /// (<c>addons-index.json.sig</c>). Trust comes from the embedded official public key plus
    /// <c>&lt;data root&gt;\trusted-keys\*.xml</c> - the same key store the package trust chain uses.
    /// An index without a signature is accepted as "Unsigned" (compatibility), matching packages.
    /// </summary>
    internal static class IndexTrust
    {
        public sealed class Result
        {
            public bool Ok { get; set; }
            public string Message { get; set; }
            public string KeyId { get; set; }
        }

        public static Result Verify(byte[] json, byte[] signatureJson)
        {
            try
            {
                var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    Encoding.UTF8.GetString(signatureJson ?? new byte[0]));
                if (parsed == null) return Fail("签名文件为空");

                var algorithm = Convert.ToString(Get(parsed, "algorithm"));
                if (!string.Equals(algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
                    return Fail("不支持的签名算法：" + algorithm);

                var keyId = Convert.ToString(Get(parsed, "keyId"));
                var signature = Convert.FromBase64String(Convert.ToString(Get(parsed, "signature")) ?? "");
                var key = FindKey(keyId, out var source);
                if (key == null)
                    return Fail("签名密钥不受信任（keyId " + keyId + "）");

                using (key)
                {
                    var ok = key.VerifyData(json ?? new byte[0], signature,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    return new Result
                    {
                        Ok = ok,
                        KeyId = keyId,
                        Message = ok
                            ? $"签名有效（keyId {keyId}，{source}）"
                            : "签名不匹配（索引内容已被修改？）"
                    };
                }
            }
            catch (Exception ex)
            {
                return Fail("签名解析失败：" + ex.Message);
            }
        }

        private static Result Fail(string message) => new Result { Ok = false, Message = message };

        private static object Get(IDictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? value : null;

        private static RSA FindKey(string keyId, out string source)
        {
            source = null;
            if (string.IsNullOrWhiteSpace(keyId)) return null;

            var builtin = ReadBuiltinPublicKey();
            if (!string.IsNullOrEmpty(builtin))
            {
                try
                {
                    var rsa = RSA.Create();
                    rsa.FromXmlString(builtin);
                    if (string.Equals(PackageTrust.KeyIdFor(rsa), keyId, StringComparison.OrdinalIgnoreCase))
                    {
                        source = "内置公钥";
                        return rsa;
                    }

                    rsa.Dispose();
                }
                catch
                {
                    // fall through to the user key store
                }
            }

            try
            {
                if (Directory.Exists(PackageTrust.TrustedKeyDirectory))
                {
                    foreach (var file in Directory.GetFiles(PackageTrust.TrustedKeyDirectory, "*.xml"))
                    {
                        try
                        {
                            var rsa = RSA.Create();
                            rsa.FromXmlString(File.ReadAllText(file));
                            if (string.Equals(PackageTrust.KeyIdFor(rsa), keyId,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                source = Path.GetFileName(file);
                                return rsa;
                            }

                            rsa.Dispose();
                        }
                        catch
                        {
                            // ignore malformed key files
                        }
                    }
                }
            }
            catch
            {
                // no key store
            }

            return null;
        }

        private static string ReadBuiltinPublicKey()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("PclDlSwap.Payload.data.index-key.pub.xml"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
