using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BingDaily.Models;

namespace BingDaily.Services;

public class BingApiService
{
    private readonly HttpClient _httpClient = new();
    private const string BaseUrl = "https://www.bing.com/HPImageArchive.aspx";

    public async Task<BingImage?> GetTodaysImageAsync(Settings settings)
    {
        var url = BuildApiUrl(settings);
        
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var bingResponse = JsonSerializer.Deserialize<BingResponse>(json);
        
        return bingResponse?.Images.FirstOrDefault();
    }

    public string GetFullImageUrl(BingImage image)
    {
        if (image.Url.StartsWith("http"))
        {
            return image.Url;
        }
        return $"https://www.bing.com{image.Url}";
    }

    private static string BuildApiUrl(Settings settings)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["format"] = "js",
            ["idx"] = "0",
            ["n"] = "1",
            ["uhd"] = "1",
            ["uhdwidth"] = settings.Resolution.Width.ToString(),
            ["uhdheight"] = settings.Resolution.Height.ToString(),
            ["mkt"] = settings.ResolvedMarket
        };

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{BaseUrl}?{queryString}";
    }
}
