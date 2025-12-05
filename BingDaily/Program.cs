using System.Drawing;
using System.IO;
using BingDaily.Services;
using BingDaily.Models;

namespace BingDaily;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        
        var app = new TrayApplication();
        Application.Run();
    }
}

public class TrayApplication : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly BingApiService _apiService = new();
    private readonly WallpaperService _wallpaperService = new();
    private readonly SettingsService _settingsService = new();
    private readonly StartupService _startupService = new();
    private System.Windows.Forms.Timer? _updateTimer;
    private Settings _settings;
    private bool _isUpdating;

    public TrayApplication()
    {
        _settings = _settingsService.Load();
        
        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Bing Daily Wallpaper"
        };
        
        BuildContextMenu();
        StartUpdateTimer();
        
        // Initial update on launch
        _ = UpdateWallpaperAsync("Launch");
    }

    private Icon LoadAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "logo_white.png");
            if (File.Exists(iconPath))
            {
                using var bitmap = new Bitmap(iconPath);
                // Resize for system tray (16x16 or 32x32 works best)
                using var resized = new Bitmap(bitmap, new Size(32, 32));
                var handle = resized.GetHicon();
                return Icon.FromHandle(handle);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Renderer = new ModernMenuRenderer();
        
        // Status header
        var statusItem = new ToolStripMenuItem("Bing Daily Wallpaper") { Enabled = false };
        statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        
        // Update Now
        var updateItem = new ToolStripMenuItem("🔄 Update Now");
        updateItem.Click += async (s, e) => await UpdateWallpaperAsync("Manual");
        menu.Items.Add(updateItem);
        
        menu.Items.Add(new ToolStripSeparator());
        
        // Resolution submenu
        var resolutionMenu = new ToolStripMenuItem("📐 Resolution");
        foreach (var res in ResolutionOption.Defaults)
        {
            var item = new ToolStripMenuItem(res.Label)
            {
                Checked = _settings.Resolution.Id == res.Id,
                Tag = res
            };
            item.Click += (s, e) =>
            {
                _settings.Resolution = res;
                SaveAndRefresh(true);
            };
            resolutionMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(resolutionMenu);
        
        // Region submenu
        var regionMenu = new ToolStripMenuItem("🌍 Region");
        foreach (var market in MarketOption.Defaults)
        {
            var item = new ToolStripMenuItem(market.Label)
            {
                Checked = _settings.Market == market.Code,
                Tag = market
            };
            item.Click += (s, e) =>
            {
                _settings.Market = market.Code;
                _settings.CustomMarket = "";
                SaveAndRefresh(true);
            };
            regionMenu.DropDownItems.Add(item);
        }
        regionMenu.DropDownItems.Add(new ToolStripSeparator());
        var customMarket = new ToolStripMenuItem("Custom...")
        {
            Checked = string.IsNullOrEmpty(_settings.Market) && !string.IsNullOrEmpty(_settings.CustomMarket)
        };
        customMarket.Click += (s, e) =>
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter market code (e.g., en-US, de-DE, ja-JP):",
                "Custom Region",
                _settings.CustomMarket);
            if (!string.IsNullOrWhiteSpace(input))
            {
                _settings.Market = "";
                _settings.CustomMarket = input.Trim();
                SaveAndRefresh(true);
            }
        };
        regionMenu.DropDownItems.Add(customMarket);
        menu.Items.Add(regionMenu);
        
        // Update Interval submenu
        var intervalMenu = new ToolStripMenuItem("⏱️ Update Interval");
        var intervals = new[] { 15, 30, 60, 120, 360, 720, 1440 };
        foreach (var mins in intervals)
        {
            var label = mins < 60 ? $"{mins} minutes" : 
                        mins == 60 ? "1 hour" : 
                        mins < 1440 ? $"{mins / 60} hours" : "24 hours";
            var item = new ToolStripMenuItem(label)
            {
                Checked = _settings.UpdateIntervalMinutes == mins,
                Tag = mins
            };
            item.Click += (s, e) =>
            {
                _settings.UpdateIntervalMinutes = mins;
                SaveAndRefresh(false);
            };
            intervalMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(intervalMenu);
        
        // Retention submenu
        var retentionMenu = new ToolStripMenuItem("🗂️ Keep Images");
        var retentionCounts = new[] { 7, 14, 30, 60, 100, 200 };
        foreach (var count in retentionCounts)
        {
            var item = new ToolStripMenuItem($"{count} images")
            {
                Checked = _settings.RetentionCount == count,
                Tag = count
            };
            item.Click += (s, e) =>
            {
                _settings.RetentionCount = count;
                SaveAndRefresh(false);
            };
            retentionMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(retentionMenu);
        
        menu.Items.Add(new ToolStripSeparator());
        
        // Launch at login toggle
        var launchItem = new ToolStripMenuItem("🚀 Launch at Login")
        {
            Checked = _settings.LaunchAtLogin
        };
        launchItem.Click += (s, e) =>
        {
            _settings.LaunchAtLogin = !_settings.LaunchAtLogin;
            _startupService.SetEnabled(_settings.LaunchAtLogin);
            SaveAndRefresh(false);
        };
        menu.Items.Add(launchItem);
        
        menu.Items.Add(new ToolStripSeparator());
        
        // Open folder
        var folderItem = new ToolStripMenuItem("📁 Open Wallpaper Folder");
        folderItem.Click += (s, e) =>
        {
            if (!Directory.Exists(_settings.DownloadDirectory))
                Directory.CreateDirectory(_settings.DownloadDirectory);
            System.Diagnostics.Process.Start("explorer.exe", _settings.DownloadDirectory);
        };
        menu.Items.Add(folderItem);
        
        menu.Items.Add(new ToolStripSeparator());
        
        // Quit
        var quitItem = new ToolStripMenuItem("❌ Quit");
        quitItem.Click += (s, e) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(quitItem);
        
        _trayIcon.ContextMenuStrip = menu;
    }

    private void SaveAndRefresh(bool triggerUpdate)
    {
        _settingsService.Save(_settings);
        BuildContextMenu(); // Rebuild to update checkmarks
        
        if (triggerUpdate)
        {
            _ = UpdateWallpaperAsync("Settings");
        }
        else
        {
            StartUpdateTimer();
        }
    }

    private void StartUpdateTimer()
    {
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        
        var interval = (int)TimeSpan.FromMinutes(_settings.UpdateIntervalMinutes).TotalMilliseconds;
        _updateTimer = new System.Windows.Forms.Timer { Interval = interval };
        _updateTimer.Tick += async (s, e) => await UpdateWallpaperAsync("Scheduled");
        _updateTimer.Start();
    }

    private async Task UpdateWallpaperAsync(string trigger)
    {
        if (_isUpdating) return;
        _isUpdating = true;
        
        try
        {
            _trayIcon.Text = $"Bing Daily - {trigger}: checking...";
            
            var image = await _apiService.GetTodaysImageAsync(_settings);
            
            if (image != null)
            {
                var savedPath = await _wallpaperService.DownloadAndApplyAsync(image, _settings);
                _wallpaperService.PruneOldImages(_settings);
                
                _settings.LastPreview = new ImagePreview
                {
                    Title = image.Title ?? image.StartDate,
                    Description = image.Copyright ?? "Bing daily image",
                    FileName = Path.GetFileName(savedPath),
                    FilePath = savedPath
                };
                _settingsService.Save(_settings);
                
                var title = image.Title ?? "New wallpaper";
                _trayIcon.Text = $"Bing Daily - {title}".Length > 63 
                    ? $"Bing Daily - {title}"[..63] 
                    : $"Bing Daily - {title}";
                
                _trayIcon.ShowBalloonTip(3000, "Wallpaper Updated", title, ToolTipIcon.Info);
            }
            else
            {
                _trayIcon.Text = "Bing Daily Wallpaper";
            }
        }
        catch (Exception ex)
        {
            _trayIcon.Text = "Bing Daily - Error";
            _trayIcon.ShowBalloonTip(3000, "Update Failed", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public void Dispose()
    {
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _trayIcon.Dispose();
    }
}

// Modern dark-themed menu renderer
public class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }
    
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(240, 240, 240);
        base.OnRenderItemText(e);
    }
    
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(180, 180, 180);
        base.OnRenderArrow(e);
    }
}

public class ModernColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(60, 60, 60);
    public override Color MenuItemBorder => Color.FromArgb(80, 80, 80);
    public override Color MenuItemSelected => Color.FromArgb(55, 55, 55);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(55, 55, 55);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(55, 55, 55);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(45, 45, 45);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(45, 45, 45);
    public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 30);
    public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 30);
    public override Color ToolStripDropDownBackground => Color.FromArgb(35, 35, 35);
    public override Color ImageMarginGradientBegin => Color.FromArgb(35, 35, 35);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(35, 35, 35);
    public override Color ImageMarginGradientEnd => Color.FromArgb(35, 35, 35);
    public override Color SeparatorDark => Color.FromArgb(60, 60, 60);
    public override Color SeparatorLight => Color.FromArgb(60, 60, 60);
    public override Color CheckBackground => Color.FromArgb(0, 120, 212);
    public override Color CheckSelectedBackground => Color.FromArgb(0, 100, 180);
}
