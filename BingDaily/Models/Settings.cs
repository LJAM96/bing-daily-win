using System.IO;
using System.Text.Json.Serialization;

namespace BingDaily.Models;

public class Settings
{
    public ResolutionOption Resolution { get; set; } = ResolutionOption.Uhd4K;
    public string Market { get; set; } = "en-US";
    public string CustomMarket { get; set; } = "";
    public int UpdateIntervalMinutes { get; set; } = 360;
    public int RetentionCount { get; set; } = 30;
    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".bing-wallpapers");
    public bool LaunchAtLogin { get; set; } = false;
    public ImagePreview? LastPreview { get; set; }
    public bool HasCompletedOnboarding { get; set; } = false;
    
    public string ResolvedMarket => string.IsNullOrEmpty(Market) 
        ? (string.IsNullOrEmpty(CustomMarket) ? "en-US" : CustomMarket) 
        : Market;
}

public class ResolutionOption
{
    public string Id { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label { get; set; } = "";

    public static readonly ResolutionOption Uhd4K = new() { Id = "4k", Width = 3840, Height = 2160, Label = "4K (3840×2160)" };
    public static readonly ResolutionOption Uhd1440 = new() { Id = "1440p", Width = 2560, Height = 1440, Label = "1440p (2560×1440)" };
    public static readonly ResolutionOption FullHD = new() { Id = "1080p", Width = 1920, Height = 1080, Label = "1080p (1920×1080)" };

    public static readonly ResolutionOption[] Defaults = [Uhd4K, Uhd1440, FullHD];

    public override bool Equals(object? obj) => obj is ResolutionOption other && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
}

public class MarketOption
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";

    public static readonly MarketOption[] Defaults =
    [
        new() { Code = "en-US", Label = "United States (en-US)" },
        new() { Code = "en-GB", Label = "United Kingdom (en-GB)" },
        new() { Code = "en-AU", Label = "Australia (en-AU)" },
        new() { Code = "fr-FR", Label = "France (fr-FR)" },
        new() { Code = "de-DE", Label = "Germany (de-DE)" },
        new() { Code = "ja-JP", Label = "Japan (ja-JP)" },
        new() { Code = "zh-CN", Label = "China (zh-CN)" },
    ];
}

public class ImagePreview
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
}
