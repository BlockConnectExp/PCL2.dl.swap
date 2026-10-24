using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

// Usage: VerifyOffsets <offset-manifest.json> <sample1.exe> [sample2.exe ...]
// Validates, for every sample that matches a manifest entry: file SHA-256, module MVID,
// and that every declared token resolves to the declared method name with the declared RVA.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: VerifyOffsets <manifest.json> <sample.exe>...");
    return 2;
}

var manifestPath = args[0];
var samples = args.Skip(1).ToArray();
using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
var targets = document.RootElement.GetProperty("targets").EnumerateArray().ToList();

int failures = 0;
foreach (var sample in samples)
{
    if (!File.Exists(sample))
    {
        Console.WriteLine($"MISSING  {sample}");
        failures++;
        continue;
    }

    var sha = Sha256(sample);
    using var module = ModuleDefMD.Load(sample);
    var mvid = module.Mvid.ToString().ToLowerInvariant();

    var target = targets.FirstOrDefault(t =>
        string.Equals(t.GetProperty("mvid").GetString(), mvid, StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"== {Path.GetFileName(sample)} ==");
    Console.WriteLine($"   sha256 = {sha}");
    Console.WriteLine($"   mvid   = {mvid}");
    if (target.ValueKind == JsonValueKind.Undefined)
    {
        Console.WriteLine("   MATCH  = none (build not present in the offset manifest)");
        failures++;
        continue;
    }

    var id = target.GetProperty("id").GetString();
    var shaMatches = string.Equals(target.GetProperty("sha256").GetString(), sha, StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"   MATCH  = {id} ({(shaMatches ? "sha256 identical" : "sha256 differs")})");
    if (!shaMatches) failures++;

    if (target.TryGetProperty("tokens", out var tokens))
    {
        foreach (var token in tokens.EnumerateObject())
        {
            var key = token.Name;                       // e.g. PCL.ModSecret.UpdateStart
            var expectedToken = token.Value.GetString();
            var methodName = key[(key.LastIndexOf('.') + 1)..];
            var typeName = key[..key.LastIndexOf('.')];

            var numeric = Convert.ToInt32(expectedToken, 16);
            MethodDef resolved = null;
            try { resolved = module.ResolveToken((uint)numeric) as MethodDef; }
            catch (Exception ex) { Console.WriteLine($"   [FAIL] {key}: token resolution threw {ex.GetType().Name}"); failures++; continue; }

            if (resolved == null)
            {
                Console.WriteLine($"   [FAIL] {key}: token {expectedToken} does not resolve to a MethodDef");
                failures++;
                continue;
            }

            bool nameOk = resolved.Name == methodName && resolved.DeclaringType.FullName == typeName;
            Console.WriteLine($"   [{(nameOk ? "OK" : "FAIL")}] {expectedToken} -> {resolved.DeclaringType.FullName}.{resolved.Name} rva=0x{Convert.ToUInt64(resolved.RVA):X}");
            if (!nameOk) failures++;
        }
    }

    if (target.TryGetProperty("bodyFileOffset", out var rvas))
    {
        foreach (var rva in rvas.EnumerateObject())
        {
            var key = rva.Name;
            var expectedRva = rva.Value.GetString();
            var typeName = key[..key.LastIndexOf('.')];
            var methodName = key[(key.LastIndexOf('.') + 1)..];
            var method = module.GetTypes().FirstOrDefault(t => t.FullName == typeName)?
                .Methods.FirstOrDefault(m => m.Name == methodName);
            if (method == null)
            {
                Console.WriteLine($"   [FAIL] {key}: method not found for RVA check");
                failures++;
                continue;
            }
            // dnlib's RVA property on a MethodDef is the method body's file offset in the on-disk image.
            var actual = "0x" + Convert.ToUInt64(method.RVA).ToString("X").ToLowerInvariant();
            bool ok = string.Equals(actual, expectedRva, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"   [{(ok ? "OK" : "FAIL")}] rva {key}: manifest={expectedRva} actual={actual}");
            if (!ok) failures++;
        }
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
return failures == 0 ? 0 : 1;

static string Sha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover