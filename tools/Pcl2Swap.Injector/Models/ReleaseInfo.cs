using System.Text.Json.Serialization;

namespace Pcl2Swap.Injector.Models;

/// <summary>One GitHub release, trimmed to what the injector shows.</summary>
public sealed class ReleaseInfo
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = new();

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? TagName : Name!;

    [JsonIgnore]
    public string PublishedLabel => PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知时间";

    [JsonIgnore]
    public string BadgeLabel => Prerelease ? "Pre-release" : "Release";

    [JsonIgnore]
    public string SizeLabel
    {
        get
        {
            var bytes = Assets.Sum(a => a.Size);
            return bytes <= 0 ? "" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
        }
    }
}

public sealed class ReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
}
