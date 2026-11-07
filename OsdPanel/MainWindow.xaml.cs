using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace OsdPanel;

public partial class MainWindow : Window
{
    public bool AllowClose { get; set; }

    private AppSettings _settings;
    private readonly SensorsService _sensors;
    private readonly DispatcherTimer _hoverTimer;
    private IntPtr _hwnd;
    private bool _clickThrough;
    private bool _dragging;
    private int _dragOffsetX;
    private int _dragOffsetY;

    public MainWindow(AppSettings settings, SensorsService sensors)
    {
        InitializeComponent();
        _settings = settings;
        _sensors = sensors;
        _sensors.Updated += OnSensorsUpdated;
        Topmost = settings.TopMost;
        UpdateTopMostIndicator();
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverTimer.Tick += HoverTimer_Tick;
        SizeChanged += (_, _) => { if (IsLoaded && !_dragging) { UpdatePanelHeight(); PositionWithinScreen(); } };
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        settings.Normalize();
        _settings = settings;
        FontSize = settings.FontSize;
        Width = 345 * settings.FontSize / 18;
        TimeText.FontSize = settings.FontSize * 13 / 12;
        UptimeText.FontSize = TimeText.FontSize;
        PanelBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(settings.BackgroundOpacity * 255 / 100.0), 0x14, 0x16, 0x1A));
        foreach (var pair in new[] { (LoadsSection, settings.ShowLoads), (DiskSpaceSection, settings.ShowDiskSpace),
            (TemperatureSection, settings.ShowTemperatures), (FansSection, settings.ShowFans), (IpSection, settings.ShowIp),
            (NetworkSection, settings.ShowNetwork), (ActivitySection, settings.ShowDiskActivity), (MemorySection, settings.ShowFreeMemory),
            (HealthSection, settings.ShowDiskHealth), (DesSection, settings.ShowDesStatus) })
            pair.Item1.Visibility = pair.Item2 ? Visibility.Visible : Visibility.Collapsed;
        GrabStrip.Cursor = settings.Locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        _sensors.Configure(settings);
        OnSensorsUpdated(_sensors.Snapshot);
        if (IsLoaded) Dispatcher.BeginInvoke(() => { UpdatePanelHeight(); UpdateLayout(); PositionWithinScreen(); SavePosition(); });
    }

    public void RestorePanel()
    {
        Show();
        _hoverTimer.Start();
    }

    public bool IsTopMostOn => Topmost;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        Interop.RegisterHotKey(_hwnd, Interop.HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_ALT, Interop.VK_O);
        SetClickThrough(true);
        _hoverTimer.Start();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = _settings.Left;
        Top = _settings.Top;
        if (_settings.PixelLeft.HasValue && _settings.PixelTop.HasValue)
            MovePanel(_settings.PixelLeft.Value, _settings.PixelTop.Value);
        UpdatePanelHeight();
        PositionWithinScreen();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _hoverTimer.Stop();
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SavePosition();
    }

    private void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        if (Interop.GetWindowRect(_hwnd, out var bounds))
        {
            _settings.PixelLeft = bounds.Left;
            _settings.PixelTop = bounds.Top;
        }
        _settings.TopMost = Topmost;
        _settings.Save();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is 0x02E0 or 0x007E) // DPI or monitor arrangement changed.
            Dispatcher.BeginInvoke(() => { UpdatePanelHeight(); if (!_dragging) PositionWithinScreen(); });
        if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == Interop.HOTKEY_ID)
        {
            ToggleTopMost();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void ToggleTopMost()
    {
        Topmost = !Topmost;
        UpdateTopMostIndicator();
        SavePosition();
    }

    private void UpdateTopMostIndicator()
    {
        TopMostIndicator.Foreground = Topmost
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    }

    private void SetClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || _clickThrough == enabled) return;
        _clickThrough = enabled;
        var style = Interop.GetWindowLong(_hwnd, Interop.GWL_EXSTYLE);
        style |= Interop.WS_EX_LAYERED;
        if (enabled) style |= Interop.WS_EX_TRANSPARENT;
        else style &= ~Interop.WS_EX_TRANSPARENT;
        Interop.SetWindowLong(_hwnd, Interop.GWL_EXSTYLE, style);
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || _dragging) return;
        if (!Interop.GetCursorPos(out var cursor)) return;
        var area = GrabStripScreenRect();
        var over = cursor.X >= area.Left - 2 && cursor.X <= area.Right + 2 &&
                   cursor.Y >= area.Top - 2 && cursor.Y <= area.Bottom + 2;
        var point = PanelScroller.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        var overScrollBar = PanelScroller.ScrollableHeight > 0 && point.X >= PanelScroller.ActualWidth - SystemParameters.VerticalScrollBarWidth &&
            point.X <= PanelScroller.ActualWidth && point.Y >= 0 && point.Y <= PanelScroller.ActualHeight;
        SetClickThrough(!overScrollBar && (_settings.Locked || !over));
    }

    private Interop.RECT GrabStripScreenRect()
    {
        var topLeft = GrabStrip.PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = GrabStrip.PointToScreen(new System.Windows.Point(GrabStrip.ActualWidth, GrabStrip.ActualHeight));
        return new Interop.RECT
        {
            Left = (int)topLeft.X,
            Top = (int)topLeft.Y,
            Right = (int)Math.Ceiling(bottomRight.X),
            Bottom = (int)Math.Ceiling(bottomRight.Y)
        };
    }

    private void PositionWithinScreen()
    {
        if (!Interop.GetWindowRect(_hwnd, out var bounds)) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var position = PanelPlacement.Clamp(
            new System.Drawing.Point(bounds.Left, bounds.Top),
            new System.Drawing.Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), area);
        MovePanel(position.X, position.Y);
    }

    private void UpdatePanelHeight()
    {
        if (_hwnd == IntPtr.Zero) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        PanelScroller.MaxHeight = Math.Max(100, area.Height / scale - TimeText.ActualHeight - UptimeText.ActualHeight - GrabStrip.ActualHeight - 50);
    }

    private void MovePanel(int x, int y) => Interop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
        Interop.SWP_NOSIZE | Interop.SWP_NOZORDER | Interop.SWP_NOACTIVATE);

    private void OnSensorsUpdated(SensorSnapshot s)
    {
        Dispatcher.BeginInvoke(() =>
        {
            lock (s)
            {
                TimeText.Text = s.Time.ToString("dd.MM.yyyy  HH:mm:ss");
                var uptime = s.Uptime.ToString(@"hh\:mm\:ss");
                UptimeText.Text = s.Uptime.TotalDays >= 1 ? $"{(long)s.Uptime.TotalDays} д {uptime}" : uptime;
                CpuLoadText.Text = s.CpuLoad;
                GpuLoadText.Text = s.GpuLoad;
                RamLoadText.Text = s.RamLoad;
                VramLoadText.Text = s.VramLoad;
                RamFreeText.Text = MemoryReading.Format(s.RamFreeBytes);
                VramFreeText.Text = MemoryReading.Format(s.VramFreeBytes);
                RamFreeText.ToolTip = $"Всего RAM: {MemoryReading.Format(s.RamTotalBytes)}";
                VramFreeText.ToolTip = $"Всего VRAM: {MemoryReading.Format(s.VramTotalBytes)}";
                CpuTempText.Text = s.CpuTemp;
                GpuTempText.Text = s.GpuTemp;
                MbTempText.Text = s.MbTemp;
                CpuFanText.Text = s.CpuFan;
                GpuFanText.Text = s.GpuFan;
                IpText.Text = s.IpAddress;
                ExternalIpText.Text = s.ExternalIp;
                DesSummaryText.Text = s.Des.Summary;
                DesServersText.Text = s.Des.Servers;
                DesServersText.Visibility = string.IsNullOrEmpty(s.Des.Servers) ? Visibility.Collapsed : Visibility.Visible;
                DesSummaryText.Foreground = Brush(s.Des.Color);
                DesServersText.Foreground = Brush(s.Des.Color);
                AdapterText.Text = $"Сеть: {s.AdapterName}";
                DownloadText.Text = TrafficSampler.FormatRate(s.DownloadBytes);
                UploadText.Text = TrafficSampler.FormatRate(s.UploadBytes);
                CpuTempText.Foreground = Brush(Thresholds.Temperature(s.CpuTemperature, _settings.GetTemperatureLimits("CPU"), "White", _settings.ColorThresholds));
                GpuTempText.Foreground = Brush(Thresholds.Temperature(s.GpuTemperature, _settings.GetTemperatureLimits("GPU"), "White", _settings.ColorThresholds));
                MbTempText.Foreground = Brush(Thresholds.Temperature(s.MbTemperature, _settings.GetTemperatureLimits("MB"), "White", _settings.ColorThresholds));
                DiskList.ItemsSource = s.Disks.Select(d => new { d.Name, d.Free, d.Device,
                    Color = Thresholds.FreeSpace(d.FreePercent, _settings.GetFreeLimits(d.Letter), _settings.ColorThresholds) }).ToList();
                DiskTempList.ItemsSource = s.Disks.Select(d => new { Name = $"Темп. {d.Letter}", d.Temp, d.Device,
                    Color = Thresholds.Temperature(d.Temperature, _settings.GetTemperatureLimits(d.Letter), "#FFD700", _settings.ColorThresholds) }).ToList();
                DiskActivityList.ItemsSource = s.Disks.Select(d => new { d.Letter, d.Device,
                    Read = d.ReadBytes == 0 && d.WriteBytes == 0 ? "простой" : $"↓ {TrafficSampler.FormatRate(d.ReadBytes)}",
                    Write = d.ReadBytes == 0 && d.WriteBytes == 0 ? "" : $"↑ {TrafficSampler.FormatRate(d.WriteBytes)}" }).ToList();
                DiskHealthList.ItemsSource = s.Disks.Select(d => new {
                    Name = d.Health?.RemainingLife.HasValue == true ? $"Ресурс {d.Letter}" : $"Состояние {d.Letter}",
                    Health = (d.Health ?? DiskHealth.Unknown).Text,
                    Color = _settings.ColorThresholds ? (d.Health ?? DiskHealth.Unknown).Color : "#FFD700"
                }).ToList();
            }
        });
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void GrabStrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_settings.Locked) return;
        if (e.ClickCount == 2)
        {
            SnapToNearestCorner();
            e.Handled = true;
            return;
        }
        if (!Interop.GetCursorPos(out var cursor)) return;
        if (!Interop.GetWindowRect(_hwnd, out var bounds)) return;
        _dragging = true;
        _dragOffsetX = cursor.X - bounds.Left;
        _dragOffsetY = cursor.Y - bounds.Top;
        GrabStrip.CaptureMouse();
        SetClickThrough(false);
        e.Handled = true;
    }

    private void GrabStrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        if (!Interop.GetCursorPos(out var cursor)) return;
        // Keep native cursor and window coordinates in pixels, including negative monitor origins.
        // Constrain only on release so the panel can cross a monitor boundary freely.
        MovePanel(cursor.X - _dragOffsetX, cursor.Y - _dragOffsetY);
    }

    private void GrabStrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        GrabStrip.ReleaseMouseCapture();
        UpdatePanelHeight();
        PositionWithinScreen();
        SavePosition();
        e.Handled = true;
    }

    private void SnapToNearestCorner()
    {
        if (!Interop.GetWindowRect(_hwnd, out var bounds)) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var position = PanelPlacement.NearestCorner(
            new System.Drawing.Point(bounds.Left, bounds.Top),
            new System.Drawing.Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), area);
        MovePanel(position.X, position.Y);
        SavePosition();
    }
}
