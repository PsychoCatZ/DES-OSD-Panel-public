using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace OsdPanel;

public partial class GameOverlayWindow : Window, IDisposable
{
    private AppSettings _settings;
    private readonly SensorsService _sensors;
    private readonly Action<AppSettings> _save;
    private readonly DispatcherTimer _inputTimer;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _registered, _disposed, _dragging, _interactive;
    private volatile bool _watching;
    private int _offsetX, _offsetY;
    public int HotkeyError { get; private set; }

    public GameOverlayWindow(AppSettings settings, SensorsService sensors, Action<AppSettings>? saveSettings = null)
    {
        InitializeComponent();
        _settings = settings; _sensors = sensors;
        _save = saveSettings ?? (value => value.Save());
        _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _inputTimer.Tick += (_, _) => UpdateInputMode();
        SizeChanged += (_, _) => { if (IsVisible && !_dragging) ClampToMonitor(); };
        _sensors.Updated += OnSnapshot;
        ApplySettings(settings);
    }

    public bool RegisterToggleHotkey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        new WindowInteropHelper(this).EnsureHandle();
        if (_registered) return true;
        _registered = Interop.RegisterHotKey(_hwnd, Interop.GAME_HOTKEY_ID,
            Interop.MOD_CONTROL | Interop.MOD_ALT | Interop.MOD_NOREPEAT, Interop.VK_G);
        HotkeyError = _registered ? 0 : Marshal.GetLastWin32Error();
        return _registered;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WindowMessage);
        var style = Interop.GetWindowLong(_hwnd, Interop.GWL_EXSTYLE);
        Interop.SetWindowLong(_hwnd, Interop.GWL_EXSTYLE,
            style | Interop.WS_EX_NOACTIVATE | Interop.WS_EX_TOOLWINDOW | Interop.WS_EX_LAYERED | Interop.WS_EX_TRANSPARENT);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Interop.WM_HOTKEY && wParam.ToInt32() == Interop.GAME_HOTKEY_ID)
        { Toggle(); handled = true; }
        else if (message is 0x02E0 or 0x007E)
            Dispatcher.BeginInvoke(() => { if (!_disposed && IsVisible) { FitToMonitor(); if (!_dragging) ClampToMonitor(); } });
        return IntPtr.Zero;
    }

    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsVisible) { HideOverlay(); return; }
        new WindowInteropHelper(this).EnsureHandle();
        var initial = !_settings.GameOverlayPixelLeft.HasValue || !_settings.GameOverlayPixelTop.HasValue;
        var target = initial ? System.Windows.Forms.Screen.FromHandle(Interop.GetForegroundWindow()) :
            System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(_settings.GameOverlayPixelLeft!.Value, _settings.GameOverlayPixelTop!.Value));
        Move(initial ? target.WorkingArea.Left + 8 : _settings.GameOverlayPixelLeft!.Value,
            initial ? target.WorkingArea.Top + 8 : _settings.GameOverlayPixelTop!.Value);
        FitToMonitor(); UpdateSnapshot(_sensors.Snapshot);
        _watching = true;
        Show(); UpdateLayout(); FitToMonitor(); UpdateLayout();
        if (initial && Interop.GetWindowRect(_hwnd, out var bounds))
            Move(target.WorkingArea.Left + Math.Max(0, (target.WorkingArea.Width - bounds.Right + bounds.Left) / 2), target.WorkingArea.Top + 8);
        ClampToMonitor();
        _inputTimer.Start(); UpdateInputMode();
    }

    private void HideOverlay()
    {
        FinishDrag();
        _watching = false; _inputTimer.Stop(); SetInteractive(false); Hide();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        FontSize = settings.FontSize;
        OverlayBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(settings.BackgroundOpacity * 255 / 100.0), 0x14, 0x16, 0x1A));
        UpdateSnapshot(_sensors.Snapshot);
        if (IsVisible) { FitToMonitor(); UpdateLayout(); ClampToMonitor(); }
    }

    public void UpdateSnapshot(SensorSnapshot snapshot)
    {
        // Consolas has fixed-width glyphs: reserve 3 digits plus % / °C, even for unknown values.
        CpuValue.Text = snapshot.CpuLoad.PadLeft(4); GpuValue.Text = snapshot.GpuLoad.PadLeft(4);
        RamValue.Text = snapshot.RamLoad.PadLeft(4); VramValue.Text = snapshot.VramLoad.PadLeft(4);
        CpuTemperatureValue.Text = snapshot.CpuTemp.PadLeft(5); GpuTemperatureValue.Text = snapshot.GpuTemp.PadLeft(5);
        CpuTemperatureValue.Foreground = IsCritical(snapshot.CpuTemperature, _settings.GetTemperatureLimits("CPU").Critical) ? Brushes.Red : MetricsText.Foreground;
        GpuTemperatureValue.Foreground = IsCritical(snapshot.GpuTemperature, _settings.GetTemperatureLimits("GPU").Critical) ? Brushes.Red : MetricsText.Foreground;
    }

    public static bool IsCritical(float? temperature, double critical) => temperature.HasValue && float.IsFinite(temperature.Value) && temperature >= critical;

    private void OnSnapshot(SensorSnapshot snapshot)
    {
        if (!_watching) return;
        Dispatcher.BeginInvoke(() => { if (!_disposed && IsVisible) UpdateSnapshot(snapshot); });
    }

    private void FitToMonitor()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(_hwnd);
        MaxWidth = Math.Max(100, screen.WorkingArea.Width / VisualTreeHelper.GetDpi(this).DpiScaleX - 8);
    }

    private void ClampToMonitor()
    {
        if (!Interop.GetWindowRect(_hwnd, out var bounds)) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var position = PanelPlacement.Clamp(new System.Drawing.Point(bounds.Left, bounds.Top),
            new System.Drawing.Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), area);
        Move(position.X, position.Y);
    }

    private void Move(int x, int y) => Interop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
        Interop.SWP_NOSIZE | Interop.SWP_NOZORDER | Interop.SWP_NOACTIVATE);

    private void UpdateInputMode()
    {
        var modifiers = (Interop.GetAsyncKeyState(0x11) & 0x8000) != 0 && (Interop.GetAsyncKeyState(0x12) & 0x8000) != 0;
        SetInteractive(_dragging || modifiers);
    }

    private void SetInteractive(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || _interactive == enabled) return;
        _interactive = enabled;
        var style = Interop.GetWindowLong(_hwnd, Interop.GWL_EXSTYLE);
        Interop.SetWindowLong(_hwnd, Interop.GWL_EXSTYLE,
            enabled ? style & ~Interop.WS_EX_TRANSPARENT : style | Interop.WS_EX_TRANSPARENT);
        Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
    }

    private void DragStarted(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || !Interop.GetCursorPos(out var cursor) || !Interop.GetWindowRect(_hwnd, out var bounds)) return;
        _offsetX = cursor.X - bounds.Left; _offsetY = cursor.Y - bounds.Top;
        _dragging = CaptureMouse(); e.Handled = true;
    }
    private void DragMoved(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging && Interop.GetCursorPos(out var cursor)) Move(cursor.X - _offsetX, cursor.Y - _offsetY);
    }
    private void DragFinished(object sender, MouseButtonEventArgs e) { FinishDrag(); }
    private void DragCaptureLost(object sender, System.Windows.Input.MouseEventArgs e) { FinishDrag(); }
    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false; ReleaseMouseCapture(); ClampToMonitor();
        if (Interop.GetWindowRect(_hwnd, out var bounds))
        {
            _settings.GameOverlayPixelLeft = bounds.Left; _settings.GameOverlayPixelTop = bounds.Top;
            _save(_settings);
        }
        UpdateInputMode();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_disposed) { e.Cancel = true; HideOverlay(); }
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e) { ReleaseResources(); base.OnClosed(e); }
    private void ReleaseResources()
    {
        if (_disposed) return;
        _disposed = true; _watching = false; _inputTimer.Stop();
        _sensors.Updated -= OnSnapshot;
        if (_registered) Interop.UnregisterHotKey(_hwnd, Interop.GAME_HOTKEY_ID);
        _registered = false;
        _source?.RemoveHook(WindowMessage);
    }
    public void Dispose() { if (_disposed) return; FinishDrag(); ReleaseResources(); Close(); }
}
