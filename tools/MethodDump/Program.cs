using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;

// Usage: MethodDump <assembly> <out.tsv>
// Produces the same TSV shape as data/methods-v299-*.tsv: one row per method with token, RID,
// RVA, signature and flags. Used to build an evidence baseline for each new PCL sample.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: MethodDump <assembly> <out.tsv>");
    return 2;
}

var source = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args[1]);
if (!File.Exists(source))
{
    Console.Error.WriteLine("not found: " + source);
    return 2;
}

using var module = ModuleDefMD.Load(source);
var builder = new StringBuilder();
builder.AppendLine("# assembly\t" + (module.Assembly?.FullName ?? "(none)"));
builder.AppendLine("# module\t" + module.Name);
builder.AppendLine("# mvid\t" + module.Mvid.ToString().ToLowerInvariant());
builder.AppendLine("# file_sha256\t" + Sha256(source));
builder.AppendLine("token\trid\trva\tstatic\treturn_type\tparam_types\tdecl_type\tmethod\tflags");

var count = 0;
foreach (var type in module.GetTypes())
{
    foreach (var method in type.Methods)
    {
        var parameters = string.Join(",", method.Parameters.Select(p => ShortName(p.Type)));
        var flags = new List<string>();
        if (method.IsPublic) flags.Add("public");
        if (method.IsPrivate) flags.Add("private");
        if (method.IsStatic) flags.Add("static");
        if (method.IsVirtual) flags.Add("virtual");
        if (method.IsAbstract) flags.Add("abstract");
        builder.Append(method.MDToken.ToInt32().ToString("x8")).Append('\t')
            .Append(method.MDToken.Rid).Append('\t')
            .Append("0x").Append(((uint)method.RVA).ToString("X8")).Append('\t')
            .Append(method.IsStatic ? "1" : "0").Append('\t')
            .Append(ShortName(method.ReturnType)).Append('\t')
            .Append(parameters).Append('\t')
            .Append(type.FullName).Append('\t')
            .Append(method.Name).Append('\t')
            .Append(string.Join("|", flags)).AppendLine();
        count++;
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
File.WriteAllText(target, builder.ToString(), new UTF8Encoding(false));
Console.WriteLine($"methods={count} -> {target}");
return 0;

static string ShortName(TypeSig type)
{
    var name = type?.TypeName ?? "Void";
    var generic = name.IndexOf('<');
    if (generic > 0) name = name[..generic];
    var dot = name.LastIndexOf('.');
    if (dot >= 0) name = name[(dot + 1)..];
    return name;
}

static string Sha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
}
