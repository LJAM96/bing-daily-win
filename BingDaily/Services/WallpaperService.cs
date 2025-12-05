using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BingDaily.Models;

namespace BingDaily.Services;

public class WallpaperService
{
    private readonly HttpClient _httpClient = new();
    private readonly BingApiService _apiService = new();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public async Task<string> DownloadAndApplyAsync(BingImage image, Settings settings)
    {
        // Ensure download directory exists
        if (!Directory.Exists(settings.DownloadDirectory))
        {
            Directory.CreateDirectory(settings.DownloadDirectory);
        }

        // Build full image URL
        var imageUrl = _apiService.GetFullImageUrl(image);
        
        // Download image
        var imageData = await _httpClient.GetByteArrayAsync(imageUrl);
        
        // Generate filename
        var urlFileName = Path.GetFileName(new Uri(imageUrl).LocalPath);
        var fileName = $"{image.StartDate}_{settings.ResolvedMarket}_{urlFileName}";
        var filePath = Path.Combine(settings.DownloadDirectory, fileName);
        
        // Save image
        await File.WriteAllBytesAsync(filePath, imageData);
        
        // Set as wallpaper
        SetWallpaper(filePath);
        
        return filePath;
    }

    private static void SetWallpaper(string imagePath)
    {
        SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            imagePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    public void PruneOldImages(Settings settings)
    {
        if (settings.RetentionCount <= 0) return;
        
        if (!Directory.Exists(settings.DownloadDirectory)) return;
        
        var files = Directory.GetFiles(settings.DownloadDirectory)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
        
        foreach (var file in files.Skip(settings.RetentionCount))
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Ignore deletion errors (file may be in use)
            }
        }
    }
}
