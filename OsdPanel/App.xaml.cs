using System.Drawing;
using System.Windows;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace OsdPanel;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private AppSettings _settings = new();
    private SensorsService? _sensors;
    private Icon? _panelIcon;
    private GameOverlayWindow? _gameOverlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settings = AppSettings.Load();
        _sensors = new SensorsService();
        _gameOverlay = new GameOverlayWindow(_settings, _sensors);
        var gameHotkeyRegistered = _gameOverlay.RegisterToggleHotkey();
        _mainWindow = new MainWindow(_settings, _sensors);
        MainWindow = _mainWindow;
        _mainWindow.Show();

        var topMostItem = new ToolStripMenuItem("Поверх всех окон", null, (_, _) => _mainWindow.ToggleTopMost());
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => topMostItem.Checked = _mainWindow.IsTopMostOn;
        menu.Items.Add(topMostItem);
        menu.Items.Add(new ToolStripMenuItem("Показать", null, (_, _) => ShowPanel()));
        var gameItem = new ToolStripMenuItem("Игровая строка (Ctrl+Alt+G)", null, (_, _) => _gameOverlay.Toggle())
            { ToolTipText = "Перемещение: удерживай Ctrl+Alt и перетаскивай строку мышью." };
        menu.Items.Add(gameItem);
        menu.Opening += (_, _) => gameItem.Checked = _gameOverlay.IsVisible;
        menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (_, _) => OpenSettings()));
        var lockItem = new ToolStripMenuItem("Закрепить положение", null, (_, _) =>
        {
            var edited = _settings.Clone(); edited.Locked = !edited.Locked;
            ApplySettings(edited);
        });
        menu.Items.Add(lockItem);
        var autostartItem = new ToolStripMenuItem("Автозапуск с правами администратора", null, (_, _) =>
        {
            try { Autostart.SetEnabled(!Autostart.IsEnabled()); }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Не удалось изменить автозапуск: {ex.Message}", "OSD Panel"); }
        });
        menu.Items.Add(autostartItem);
        menu.Opening += (_, _) => { lockItem.Checked = _settings.Locked; autostartItem.Checked = Autostart.IsEnabled(); };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitApp()));

        using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/blue-circle.ico")).Stream)
        {
            using var sourceIcon = new Icon(iconStream, 32, 32);
            _panelIcon = (Icon)sourceIcon.Clone();
        }
        _trayIcon = new NotifyIcon
        {
            Icon = _panelIcon,
            Text = "OSD Panel",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowPanel();
        if (!gameHotkeyRegistered)
            _trayIcon.ShowBalloonTip(8000, "OSD Panel: горячая клавиша",
                $"Не удалось зарегистрировать Ctrl+Alt+G: {new System.ComponentModel.Win32Exception(_gameOverlay.HotkeyError).Message}. Игровую строку можно включить через меню трея.", System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void ShowPanel()
    {
        if (_mainWindow == null) return;
        _mainWindow.RestorePanel();
        _mainWindow.WindowState = WindowState.Normal;
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings);
        if (dialog.ShowDialog() == true) ApplySettings(dialog.Result);
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _mainWindow?.ApplySettings(settings);
        _gameOverlay?.ApplySettings(settings);
        settings.Save();
    }

    private void ExitApp()
    {
        _gameOverlay?.Dispose();
        _gameOverlay = null;
        if (_mainWindow != null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
            _mainWindow = null;
        }
        _sensors?.Dispose();
        _trayIcon?.Dispose();
        _panelIcon?.Dispose();
        Shutdown();
    }
}
