using System.Runtime.InteropServices;
using System.Text;
using Pcl2Swap.Injector.Models;

namespace Pcl2Swap.Injector.Services;

public sealed class InjectionContext
{
    public string InjectorVersion { get; set; } = "";
    public TargetInfo? Target { get; set; }
    public ReleaseInfo? Release { get; set; }
    public InjectionResult? Result { get; set; }
    public string? ShimPath { get; set; }
    public List<string> Steps { get; } = new();
}

/// <summary>
/// Turns a failed (or unusual) injection into something actionable: a markdown report that can
/// be filed through gh, or saved next to the injector when gh is unavailable.
/// </summary>
public static class Diagnostics
{
    public static string BuildTitle(InjectionContext context)
    {
        var stage = context.Result?.Stage ?? "未知阶段";
        var version = context.Target?.FileVersion ?? "未知版本";
        return $"[Injector] 注入失败（{stage}）· PCL2 {version}";
    }

    public static string BuildBody(InjectionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 环境");
        sb.AppendLine();
        sb.AppendLine($"- 注入器：{context.InjectorVersion}");
        sb.AppendLine($"- 操作系统：{RuntimeInformation.OSDescription}（{RuntimeInformation.OSArchitecture}）");
        sb.AppendLine($"- GitHub CLI：{(GhRunner.FindGh() ?? "未安装")}");
        sb.AppendLine();
        sb.AppendLine("## 目标");
        sb.AppendLine();
        if (context.Target != null)
        {
            var t = context.Target;
            sb.AppendLine($"- 进程：{(t.Pid > 0 ? "PID " + t.Pid : "（文件选择）")}");
            sb.AppendLine($"- 路径：`{t.ExePath}`");
            sb.AppendLine($"- 文件版本：{t.FileVersion ?? "未知"}");
            sb.AppendLine($"- 产品版本：{t.ProductVersion ?? "未知"}");
            sb.AppendLine($"- SHA-256：`{t.Sha256}`");
            sb.AppendLine($"- MVID：`{t.Mvid}`");
            sb.AppendLine($"- 偏移清单匹配：{(t.Match != null ? t.Match.Label : "无")}");
        }
        else
        {
            sb.AppendLine("- （未选择目标）");
        }

        sb.AppendLine();
        sb.AppendLine("## 注入包");
        sb.AppendLine();
        sb.AppendLine($"- Release：{(context.Release != null ? context.Release.TagName : "未知")}");
        sb.AppendLine($"- 本地 shim：`{context.ShimPath ?? "无"}`");
        sb.AppendLine();
        sb.AppendLine("## 失败阶段");
        sb.AppendLine();
        sb.AppendLine($"- 阶段：**{context.Result?.Stage ?? "未知"}**");
        sb.AppendLine($"- 详情：{context.Result?.Detail ?? "（无）"}");
        if (context.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 执行步骤");
            sb.AppendLine();
            foreach (var step in context.Steps) sb.AppendLine($"1. {step}");
        }

        if (!string.IsNullOrWhiteSpace(context.Result?.LogTail))
        {
            sb.AppendLine();
            sb.AppendLine("## Swap 日志尾部");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(context.Result!.LogTail);
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("_由 PCL2 Swap Injector 自动生成。_");
        return sb.ToString();
    }

    public static string SaveLocally(string title, string body)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PCL2Swap", "reports");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root,
            $"inject-failure-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, "# " + title + Environment.NewLine + Environment.NewLine + body,
            new UTF8Encoding(false));
        return file;
    }

    public static async Task<(bool Ok, string Message)> SubmitAsync(
        string title, string body, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"pcl2swap-issue-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        await File.WriteAllTextAsync(temp, body, new UTF8Encoding(false), ct).ConfigureAwait(false);

        var result = await GhRunner.CreateIssueAsync(ReleaseFeed.Repo, title, temp, ct)
            .ConfigureAwait(false);
        if (result.Ok)
            return (true, result.StdOut.Trim());

        var saved = SaveLocally(title, body);
        var reason = result.StdErr.Trim();
        return (false, $"提交失败：{reason}\n诊断已保存到 {saved}");
    }
}
