using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using dnlib.DotNet;

namespace PclSurface
{
    /// <summary>
    /// Dumps the complete callable surface of a PCL build into JSON so Swap addons can discover
    /// every callable member - public or internal, explicit or not - and reach it through the
    /// reflection bridge. Usage: PclSurface &lt;pcl.exe&gt; &lt;out.json&gt; [--include-generated]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: PclSurface <pcl.exe> <out.json> [--include-generated]");
                return 2;
            }

            var source = Path.GetFullPath(args[0]);
            var target = Path.GetFullPath(args[1]);
            var includeGenerated = args.Any(a => a == "--include-generated");
            if (!File.Exists(source))
            {
                Console.Error.WriteLine("not found: " + source);
                return 2;
            }

            var module = ModuleDefMD.Load(source);
            var types = new List<object>();
            var methodCount = 0;
            var propertyCount = 0;
            var fieldCount = 0;

            foreach (var type in module.GetTypes())
            {
                if (!includeGenerated && IsGenerated(type.Name, type)) continue;

                var methods = new List<object>();
                foreach (var method in type.Methods)
                {
                    if (!includeGenerated && IsGenerated(method.Name, method)) continue;
                    methods.Add(new Dictionary<string, object>
                    {
                        { "name", method.Name.ToString() },
                        { "static", method.IsStatic },
                        { "visibility", VisibilityOf(method) },
                        { "virtual", method.IsVirtual },
                        { "abstract", method.IsAbstract },
                        { "return", method.ReturnType == null ? null : method.ReturnType.FullName },
                        { "params", method.Parameters.Select(p => p.Type == null ? null : p.Type.FullName).ToArray() },
                        { "token", "0x" + method.MDToken.Raw.ToString("X8") }
                    });
                }

                var properties = new List<object>();
                foreach (var property in type.Properties)
                {
                    properties.Add(new Dictionary<string, object>
                    {
                        { "name", property.Name.ToString() },
                        {
                            "type", property.PropertySig == null || property.PropertySig.RetType == null
                                ? null
                                : property.PropertySig.RetType.FullName
                        },
                        { "get", property.GetMethod != null },
                        { "set", property.SetMethod != null },
                        { "static", (property.GetMethod ?? property.SetMethod)?.IsStatic ?? false }
                    });
                }

                var fields = new List<object>();
                foreach (var field in type.Fields)
                {
                    if (!includeGenerated && IsGenerated(field.Name, field)) continue;
                    fields.Add(new Dictionary<string, object>
                    {
                        { "name", field.Name.ToString() },
                        { "type", field.FieldType == null ? null : field.FieldType.FullName },
                        { "static", field.IsStatic },
                        { "visibility", VisibilityOf(field) },
                        { "literal", field.IsLiteral }
                    });
                }

                methodCount += methods.Count;
                propertyCount += properties.Count;
                fieldCount += fields.Count;
                types.Add(new Dictionary<string, object>
                {
                    { "fullName", type.FullName.Replace('/', '+') },
                    { "dnlibName", type.FullName },
                    { "name", type.Name.ToString() },
                    { "namespace", type.Namespace.ToString() },
                    { "kind", KindOf(type) },
                    { "visibility", VisibilityOf(type) },
                    { "abstract", type.IsAbstract },
                    { "sealed", type.IsSealed },
                    { "base", type.BaseType == null ? null : type.BaseType.FullName },
                    { "methods", methods.ToArray() },
                    { "properties", properties.ToArray() },
                    { "fields", fields.ToArray() }
                });
            }

            var root = new Dictionary<string, object>
            {
                { "schema", 1 },
                { "generator", "PclSurface 1.0" },
                { "generatedAtUtc", DateTime.UtcNow.ToString("O") },
                {
                    "assembly", new Dictionary<string, object>
                    {
                        { "name", module.Assembly == null ? null : module.Assembly.Name.String },
                        { "version", module.Assembly == null ? null : module.Assembly.Version.ToString() },
                        { "mvid", module.Mvid.ToString() },
                        { "sha256", Sha256(source) },
                        { "path", source }
                    }
                },
                { "includeGenerated", includeGenerated },
                { "typeCount", types.Count },
                { "methodCount", methodCount },
                { "propertyCount", propertyCount },
                { "fieldCount", fieldCount },
                { "types", types.ToArray() }
            };

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(target, serializer.Serialize(root));
            Console.WriteLine($"types={types.Count} methods={methodCount} properties={propertyCount} fields={fieldCount}");
            Console.WriteLine("out=" + target + " size=" + new FileInfo(target).Length);
            module.Dispose();
            return 0;
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsGenerated(string name, IHasCustomAttribute provider)
        {
            var text = name ?? string.Empty;
            if (text.Contains("_Closure") || text.Contains("<>") || text.Contains("_Lambda$") ||
                text.Contains("<PrivateImplementationDetails>"))
                return true;

            try
            {
                return provider.CustomAttributes.Any(attribute =>
                    attribute.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
            }
            catch
            {
                return false;
            }
        }

        private static string KindOf(TypeDef type)
        {
            if (type.IsInterface) return "interface";
            if (type.IsEnum) return "enum";
            if (type.IsValueType) return "struct";
            if (type.IsDelegate) return "delegate";
            return "class";
        }

        private static string VisibilityOf(TypeDef type)
        {
            if (type.IsPublic || type.IsNestedPublic) return "public";
            if (type.IsNotPublic || type.IsNestedAssembly) return "internal";
            if (type.IsNestedFamily) return "protected";
            if (type.IsNestedFamilyOrAssembly) return "protected-internal";
            if (type.IsNestedFamilyAndAssembly) return "private-protected";
            return "private";
        }

        private static string VisibilityOf(MethodDef method)
        {
            if (method.IsPublic) return "public";
            if (method.IsAssembly) return "internal";
            if (method.IsFamily) return "protected";
            if (method.IsFamilyOrAssembly) return "protected-internal";
            if (method.IsFamilyAndAssembly) return "private-protected";
            return "private";
        }

        private static string VisibilityOf(FieldDef field)
        {
            if (field.IsPublic) return "public";
            if (field.IsAssembly) return "internal";
            if (field.IsFamily) return "protected";
            if (field.IsFamilyOrAssembly) return "protected-internal";
            if (field.IsFamilyAndAssembly) return "private-protected";
            return "private";
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
