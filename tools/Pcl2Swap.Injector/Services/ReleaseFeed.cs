using System.Text.Json;
using Pcl2Swap.Injector.Models;

namespace Pcl2Swap.Injector.Services;

/// <summary>
/// Loads the published release list through gh. The injector is the one component in this repo
/// that is allowed to touch the network - and only for this list, the shim download and the
/// issue it files when an injection fails.
/// </summary>
public static class ReleaseFeed
{
    public const string Repo = "BlockConnectExp/PCL2.dl.swap";

    public static async Task<(List<ReleaseInfo> Releases, string? Error)> LoadAsync(
        CancellationToken ct = default)
    {
        var result = await GhRunner.RunAsync(
            $"api \"repos/{Repo}/releases?per_page=40\" --paginate",
            ct).ConfigureAwait(false);

        if (!result.Ok)
        {
            var message = result.StdErr.Trim();
            if (string.IsNullOrEmpty(message)) message = "gh 调用失败（退出码 " + result.ExitCode + "）。";
            return (new List<ReleaseInfo>(), message);
        }

        try
        {
            var releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(result.StdOut) ??
                           new List<ReleaseInfo>();
            var visible = releases
                .Where(r => !r.Draft && r.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)
                .ToList();
            return (visible, null);
        }
        catch (JsonException ex)
        {
            return (new List<ReleaseInfo>(), "Release 列表解析失败：" + ex.Message);
        }
    }
}
