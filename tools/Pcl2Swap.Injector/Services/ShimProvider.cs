using Pcl2Swap.Injector.Models;

namespace Pcl2Swap.Injector.Services;

/// <summary>
/// Resolves the shim pair (dll + dat) for one release: a local seed shipped inside the injector
/// first, then the injector's own cache, then the release assets on GitHub.
/// </summary>
public static class ShimProvider
{
    private const string DllName = "Pcl2Swap.Shim.x64.dll";
    private const string DatName = "Pcl2Swap.Shim.x64.dat";

    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCL2Swap", "Injector", "shims");

    /// <summary>The copy that travels with this injector build (if the package shipped one).</summary>
    public static (string Dll, string Dat)? SeededShim()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "shim");
        var dll = Path.Combine(root, DllName);
        var dat = Path.Combine(root, DatName);
        return File.Exists(dll) && File.Exists(dat) ? (dll, dat) : null;
    }

    public static (string Dll, string Dat)? CachedShim(string tag)
    {
        var root = Path.Combine(CacheRoot, tag);
        var dll = Path.Combine(root, DllName);
        var dat = Path.Combine(root, DatName);
        return File.Exists(dll) && File.Exists(dat) ? (dll, dat) : null;
    }

    /// <summary>
    /// Makes sure a shim for the given release is on disk. Local sources win so the injector
    /// works offline for the release it shipped with; a foreign tag downloads from its assets.
    /// </summary>
    public static async Task<(string? Dll, string? Error)> EnsureAsync(
        ReleaseInfo release, IProgress<string>? progress, CancellationToken ct)
    {
        var cached = CachedShim(release.TagName);
        if (cached != null) return (cached.Value.Dll, null);

        var seeded = SeededShim();
        var seedTag = ReadSeedTag();
        if (seeded != null && string.Equals(seedTag, release.TagName, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"使用注入器自带的 {release.TagName} 注入包。");
            return (seeded.Value.Dll, null);
        }

        var dllAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, DllName, StringComparison.OrdinalIgnoreCase));
        var datAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, DatName, StringComparison.OrdinalIgnoreCase));

        if (dllAsset == null || datAsset == null)
        {
            if (seeded != null)
            {
                progress?.Report($"{release.TagName} 的 Release 里没有独立注入包资产，改用注入器自带的包。");
                return (seeded.Value.Dll, null);
            }

            return (null, $"{release.TagName} 没有可用的注入包资产，且本机没有自带包。");
        }

        try
        {
            var root = Path.Combine(CacheRoot, release.TagName);
            Directory.CreateDirectory(root);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pcl2Swap.Injector");

            var dllTarget = Path.Combine(root, DllName);
            var datTarget = Path.Combine(root, DatName);
            progress?.Report($"正在下载 {release.TagName} 注入包（{dllAsset.Size / 1024.0 / 1024.0:0.0} MB / {datAsset.Size / 1024.0 / 1024.0:0.0} MB）…");
            await DownloadAsync(http, dllAsset.DownloadUrl, dllTarget, ct).ConfigureAwait(false);
            await DownloadAsync(http, datAsset.DownloadUrl, datTarget, ct).ConfigureAwait(false);
            progress?.Report($"注入包已缓存到 {root}。");
            return (dllTarget, null);
        }
        catch (Exception ex)
        {
            return (null, "注入包下载失败：" + ex.Message);
        }
    }

    private static async Task DownloadAsync(
        HttpClient http, string url, string target, CancellationToken ct)
    {
        var temp = target + ".downloading";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(temp);
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        if (File.Exists(target)) File.Delete(target);
        File.Move(temp, target);
    }

    private static string? ReadSeedTag()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "shim", "version.txt");
        try
        {
            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
