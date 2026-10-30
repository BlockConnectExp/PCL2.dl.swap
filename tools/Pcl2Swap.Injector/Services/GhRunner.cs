using System.Diagnostics;
using System.Text;

namespace Pcl2Swap.Injector.Services;

/// <summary>Result of one gh CLI invocation.</summary>
public sealed record GhResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Wraps the GitHub CLI. The injector deliberately shells out to gh instead of talking to the
/// REST API itself: the launcher's own OTA story already tells users not to trust a random
/// network client, and gh carries the user's existing login (token never touches this app).
/// </summary>
public static class GhRunner
{
    private static string? _cachedPath;

    public static string? FindGh()
    {
        if (_cachedPath != null) return _cachedPath.Length == 0 ? null : _cachedPath;

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
        if (File.Exists(local)) return _cachedPath = local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "gh.exe");
                if (File.Exists(candidate)) return _cachedPath = candidate;
            }
            catch
            {
                // malformed PATH entry - skip
            }
        }

        _cachedPath = string.Empty;
        return null;
    }

    public static async Task<GhResult> RunAsync(string arguments, CancellationToken ct = default)
    {
        var gh = FindGh();
        if (gh == null) return new GhResult(-1, string.Empty, "找不到 gh.exe（GitHub CLI 未安装或不在 PATH）。");

        var psi = new ProcessStartInfo(gh, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new GhResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public static async Task<GhResult> CreateIssueAsync(
        string repo, string title, string bodyFile, CancellationToken ct = default)
    {
        var args = $"issue create --repo {repo} --title {Quote(title)} --body-file {Quote(bodyFile)}";
        return await RunAsync(args, ct).ConfigureAwait(false);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
