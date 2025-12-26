using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using BingDaily.Models;
using BingDaily.Services;

namespace BingDaily;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\BingDaily_8D8D9E0B-5F29-4F67-9A7D-0E2B0B4B3F7A",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        
        var app = new TrayApplication();
        Application.ApplicationExit += (_, _) => app.Dispose();
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
        SyncStartupSetting();
        
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

    private void SyncStartupSetting()
    {
        var isEnabled = _startupService.IsEnabled;
        if (_settings.LaunchAtLogin == isEnabled) return;

        _settings.LaunchAtLogin = isEnabled;
        _settingsService.Save(_settings);
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

    private ToolStripItem? CreateLastPreviewMenuItem()
    {
        var preview = _settings.LastPreview;
        if (preview == null) return null;
        if (string.IsNullOrWhiteSpace(preview.FilePath) || !File.Exists(preview.FilePath)) return null;

        var panel = new Panel
        {
            BackColor = Color.FromArgb(35, 35, 35),
            Padding = new Padding(8),
            Width = 320,
            Height = 140,
        };

        var pictureBox = new PictureBox
        {
            Width = 128,
            Height = 72,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(25, 25, 25),
            Location = new Point(8, 8),
        };

        Image? thumbnail = null;
        try
        {
            thumbnail = CreateThumbnail(preview.FilePath, pictureBox.Width, pictureBox.Height);
            pictureBox.Image = thumbnail;
        }
        catch
        {
            thumbnail?.Dispose();
            thumbnail = null;
        }

        var titleLabel = new Label
        {
            Text = preview.Title,
            ForeColor = Color.FromArgb(240, 240, 240),
            BackColor = Color.Transparent,
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = false,
            Location = new Point(pictureBox.Right + 10, 8),
            Size = new Size(panel.Width - (pictureBox.Right + 18), 38),
        };

        var descLabel = new Label
        {
            Text = preview.Description,
            ForeColor = Color.FromArgb(200, 200, 200),
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(pictureBox.Right + 10, titleLabel.Bottom + 4),
            Size = new Size(panel.Width - (pictureBox.Right + 18), 52),
        };

        panel.Controls.Add(pictureBox);
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(descLabel);

        var host = new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        host.Disposed += (_, _) =>
        {
            try
            {
                pictureBox.Image?.Dispose();
                pictureBox.Image = null;
            }
            catch
            {
                // ignored
            }
        };

        return host;
    }

    private static Image CreateThumbnail(string filePath, int width, int height)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var src = Image.FromStream(stream);
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(Color.FromArgb(25, 25, 25));

        var scale = Math.Min((float)width / src.Width, (float)height / src.Height);
        var scaledWidth = (int)Math.Round(src.Width * scale);
        var scaledHeight = (int)Math.Round(src.Height * scale);
        var x = (width - scaledWidth) / 2;
        var y = (height - scaledHeight) / 2;
        g.DrawImage(src, new Rectangle(x, y, scaledWidth, scaledHeight));
        return bmp;
    }

    private void BuildContextMenu()
    {
        var oldMenu = _trayIcon.ContextMenuStrip;
        var menu = new ContextMenuStrip();
        menu.Renderer = new ModernMenuRenderer();
        
        // Status header
        var statusItem = new ToolStripMenuItem("Bing Daily Wallpaper") { Enabled = false };
        statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);
        menu.Items.Add(statusItem);

        var previewItem = CreateLastPreviewMenuItem();
        if (previewItem != null)
        {
            menu.Items.Add(previewItem);
        }
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
        
        // Wallpaper Style submenu
        var styleMenu = new ToolStripMenuItem("🖼️ Wallpaper Style");
        var styles = new[] { 
            (WallpaperStyle.Fill, "Fill (recommended)"),
            (WallpaperStyle.Fit, "Fit (may show bars)"),
            (WallpaperStyle.Stretch, "Stretch"),
            (WallpaperStyle.Center, "Center"),
            (WallpaperStyle.Tile, "Tile"),
            (WallpaperStyle.Span, "Span (multi-monitor)")
        };
        foreach (var (style, label) in styles)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = _settings.WallpaperStyle == style,
                Tag = style
            };
            item.Click += (s, e) =>
            {
                _settings.WallpaperStyle = style;
                SaveAndRefresh(true);
            };
            styleMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(styleMenu);
        
        menu.Items.Add(new ToolStripSeparator());
        
        // Launch at login toggle
        var startupEnabled = _startupService.IsEnabled;
        var launchItem = new ToolStripMenuItem("🚀 Launch at Login")
        {
            Checked = startupEnabled
        };
        launchItem.Click += (s, e) =>
        {
            var newValue = !_startupService.IsEnabled;
            _settings.LaunchAtLogin = newValue;
            _startupService.SetEnabled(newValue);
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

        if (oldMenu != null && !ReferenceEquals(oldMenu, menu))
        {
            try
            {
                if (oldMenu.Visible && oldMenu.IsHandleCreated)
                {
                    oldMenu.BeginInvoke(new Action(() =>
                    {
                        try { oldMenu.Close(); } catch { }
                        try { oldMenu.Dispose(); } catch { }
                    }));
                }
                else
                {
                    oldMenu.Dispose();
                }
            }
            catch
            {
                // ignored
            }
        }
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
                var imageId = $"{image.StartDate}_{_settings.ResolvedMarket}_{_settings.Resolution.Id}";
                var isNew = !string.Equals(_settings.LastAppliedImageId, imageId, StringComparison.Ordinal);
                var cachedPath = _settings.LastPreview?.FilePath;
                var hasCachedImage = !string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath);

                string? savedPath = null;
                if (isNew || !hasCachedImage)
                {
                    savedPath = await _wallpaperService.DownloadAndApplyAsync(image, _settings);
                    _wallpaperService.PruneOldImages(_settings);
                }
                
                if (savedPath != null)
                {
                    _settings.LastAppliedImageId = imageId;
                    _settings.LastPreview = new ImagePreview
                    {
                        Title = image.Title ?? image.StartDate,
                        Description = image.Copyright ?? "Bing daily image",
                        FileName = Path.GetFileName(savedPath),
                        FilePath = savedPath
                    };
                    _settingsService.Save(_settings);
                    BuildContextMenu();
                }
                
                var title = image.Title ?? "New wallpaper";
                _trayIcon.Text = $"Bing Daily - {title}".Length > 63 
                    ? $"Bing Daily - {title}"[..63] 
                    : $"Bing Daily - {title}";
                

            }
            else
            {
                _trayIcon.Text = "Bing Daily Wallpaper";
            }
        }
        catch (Exception ex)
        {
            _trayIcon.Text = "Bing Daily - Error";
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
