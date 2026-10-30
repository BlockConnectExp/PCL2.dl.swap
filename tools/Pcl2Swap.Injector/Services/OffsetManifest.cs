using System.Reflection;
using System.Text.Json;
using Pcl2Swap.Injector.Models;

namespace Pcl2Swap.Injector.Services;

/// <summary>
/// Reads the same offset manifest that ships inside the payload, so the injector can tell the
/// user whether a target build is a known-good host before a single byte is injected.
/// The embedded copy is the fallback when the file next to the injector is missing.
/// </summary>
public static class OffsetManifest
{
    private static List<HostTarget>? _targets;

    public static IReadOnlyList<HostTarget> Targets => _targets ??= Load();

    public static void Reload() => _targets = null;

    public static HostTarget? Match(string? mvid, string? sha256)
    {
        foreach (var target in Targets)
        {
            if (!string.IsNullOrEmpty(mvid) &&
                string.Equals(target.Mvid, mvid, StringComparison.OrdinalIgnoreCase))
                return target;
        }

        if (!string.IsNullOrEmpty(sha256))
        {
            foreach (var target in Targets)
            {
                if (string.Equals(target.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                    return target;
            }
        }

        return null;
    }

    private static List<HostTarget> Load()
    {
        foreach (var json in ReadCandidates())
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("targets", out var targets)) continue;

                var list = new List<HostTarget>();
                foreach (var element in targets.EnumerateArray())
                {
                    list.Add(new HostTarget
                    {
                        Id = element.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Version = element.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                        BuildType = element.TryGetProperty("buildType", out var b) ? b.GetString() ?? "" : "",
                        File = element.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                        Sha256 = element.TryGetProperty("sha256", out var s) ? s.GetString() ?? "" : "",
                        Mvid = element.TryGetProperty("mvid", out var m) ? m.GetString() ?? "" : "",
                        Role = element.TryGetProperty("role", out var r) ? r.GetString() ?? "" : ""
                    });
                }

                if (list.Count > 0) return list;
            }
            catch
            {
                // try the next candidate
            }
        }

        return new List<HostTarget>();
    }

    private static IEnumerable<string> ReadCandidates()
    {
        var onDisk = Path.Combine(AppContext.BaseDirectory, "data", "offset-manifest.json");
        if (File.Exists(onDisk))
        {
            string? text = null;
            try { text = File.ReadAllText(onDisk); } catch { }
            if (text != null) yield return text;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith("offset-manifest.json", StringComparison.OrdinalIgnoreCase));
        if (name != null)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                yield return reader.ReadToEnd();
            }
        }
    }
}
