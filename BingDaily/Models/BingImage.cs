using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BingDaily.Models;

public class BingResponse
{
    [JsonPropertyName("images")]
    public List<BingImage> Images { get; set; } = [];
}

public class BingImage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [JsonPropertyName("startdate")]
    public string StartDate { get; set; } = "";
    
    [JsonPropertyName("copyright")]
    public string? Copyright { get; set; }
    
    [JsonPropertyName("copyrightlink")]
    public string? CopyrightLink { get; set; }
    
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
