using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using dnlib.DotNet;

// Usage:
//   AddTarget <manifest.json> <sample.exe> <id> <role> <buildType> <version> <versionCode> <evidence.tsv>
//
// Appends (or replaces) a target entry in the offset manifest, deriving sha256, MVID and
// every method token/RVA from the sample itself. Keeping this mechanical is what makes
// "adapt only the offsets" a one-command operation for each new official build.

if (args.Length < 8)
{
    Console.Error.WriteLine("usage: AddTarget <manifest.json> <sample.exe> <id> <role> <buildType> <version> <versionCode> <evidence.tsv>");
    return 2;
}

var manifestPath = args[0];
var samplePath = args[1];
var id = args[2];
var role = args[3];
var buildType = args[4];
var version = args[5];
var versionCode = int.Parse(args[6]);
var evidence = args[7];

// method key -> (declaring type, method name). Signatures are looked up in the sample.
var tracked = new (string Key, string Type, string Name)[]
{
    ("PCL.ModSecret.UpdateStart",                       "PCL.ModSecret", "UpdateStart"),
    ("PCL.ModSecret.get_HasUpdatePermission",           "PCL.ModSecret", "get_HasUpdatePermission"),
    ("PCL.ModSecret.UpdateCheckByButton",               "PCL.ModSecret", "UpdateCheckByButton"),
    ("PCL.ModSecret.UpdateRestart",                     "PCL.ModSecret", "UpdateRestart"),
    ("PCL.ModSecret.UpdateReplace",                     "PCL.ModSecret", "UpdateReplace"),
    ("PCL.PageDownloadInstall.LoaderInit",              "PCL.PageDownloadInstall", "LoaderInit"),
};

using var module = ModuleDefMD.Load(samplePath);
var tokens = new JsonObject();
var rvas = new JsonObject();
var missing = new List<string>();

foreach (var (key, typeName, methodName) in tracked)
{
    var type = module.GetTypes().FirstOrDefault(t => t.FullName == typeName);
    var method = type?.Methods.FirstOrDefault(m => m.Name == methodName);
    if (method == null) { missing.Add(key); continue; }
    tokens[key] = "0x" + method.MDToken.Raw.ToString("x8");
    rvas[key] = "0x" + Convert.ToUInt64(method.RVA).ToString("x").ToLowerInvariant();
}

var sha = Sha256(samplePath);
var mvid = module.Mvid.ToString().ToLowerInvariant();

var node = new JsonObject
{
    ["id"] = id,
    ["role"] = role,
    ["buildType"] = buildType,
    ["version"] = version,
    ["versionCode"] = versionCode,
    ["file"] = Path.GetFileName(samplePath),
    ["sha256"] = sha,
    ["mvid"] = mvid,
    ["evidence"] = evidence,
    ["tokens"] = tokens,
    ["rva"] = rvas,
};

var document = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
var targets = document["targets"]!.AsArray();

// Replace an entry with the same id, otherwise append.
var existingIndex = -1;
for (int i = 0; i < targets.Count; i++)
{
    if (targets[i]!["id"]!.GetValue<string>() == id) { existingIndex = i; break; }
}
if (existingIndex >= 0) targets[existingIndex] = node; else targets.Add(node);

File.WriteAllText(manifestPath,
    document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

Console.WriteLine($"target {id}: sha256={sha[..16]}… mvid={mvid} methods={tokens.Count}");
if (missing.Count > 0)
    Console.WriteLine("  NOT FOUND (this build needs investigation): " + string.Join(", ", missing));
return missing.Count == 0 ? 0 : 1;

static string Sha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover