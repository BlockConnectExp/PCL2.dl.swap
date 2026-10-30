namespace Pcl2Swap.Injector.Models;

/// <summary>One entry of data/offset-manifest.json: a PCL build the payload knows how to hook.</summary>
public sealed class HostTarget
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string BuildType { get; set; } = "";
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Mvid { get; set; } = "";
    public string Role { get; set; } = "";

    public string Label => $"{Version} ({Id})";
}

/// <summary>Fingerprint of a candidate process or executable, plus its manifest match.</summary>
public sealed class TargetInfo
{
    public int Pid { get; set; }
    public string ExePath { get; set; } = "";
    public string? FileVersion { get; set; }
    public string? ProductVersion { get; set; }
    public string Sha256 { get; set; } = "";
    public string Mvid { get; set; } = "";
    public HostTarget? Match { get; set; }
    public string? ProbeError { get; set; }

    public bool Supported => Match != null;

    public string ProcessLabel => Pid > 0 ? $"{Path.GetFileName(ExePath)} · PID {Pid}" : Path.GetFileName(ExePath);

    public string VersionLabel => string.IsNullOrWhiteSpace(FileVersion) ? "未知版本" : FileVersion!;

    public string SupportLabel =>
        Match != null ? $"支持：{Match.Version}（清单 {Match.Id}）"
        : ProbeError != null ? "无法读取：" + ProbeError
        : "不在偏移清单中（可尝试注入，结果可能不稳定）";

    /// <summary>Checkmark for a manifest hit, warning glyph otherwise (Segoe MDL2 codepoints).</summary>
    public string SupportGlyph => Match != null ? "\uE73E" : "\uE7BA";
}
