using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OsdPanel;

public sealed class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    private readonly StackPanel _fields = new() { Margin = new Thickness(20) };
    private readonly Dictionary<string, TextBox> _numbers = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly List<(string Key, bool Temperature, TextBox Warning, TextBox Critical)> _limits = new();
    private readonly ComboBox _adapter = new() { MinWidth = 260, MaxWidth = 330, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public SettingsWindow(AppSettings settings)
    {
        Result = settings.Clone();
        Title = "Настройки OSD Panel";
        Width = 590; Height = 750; MinWidth = 530; MinHeight = 420;
        FontSize = 15;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Background = System.Windows.Media.Brushes.White };
        var footer = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        footer.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Применить", IsDefault = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8) };
        save.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8) };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Heading("Оформление и обновление");
        Number("FontSize", "Размер шрифта (12–30)", settings.FontSize);
        Number("BackgroundOpacity", "Плотность фона, % (20–100)", settings.BackgroundOpacity);
        Number("UpdateIntervalMs", "Обновление, мс (500–5000)", settings.UpdateIntervalMs);
        Check("Locked", "Закрепить положение панели", settings.Locked);
        Heading("Показывать на панели");
        Check("ShowLoads", "Загрузка CPU, GPU и памяти", settings.ShowLoads);
        Check("ShowFreeMemory", "Доступная RAM и свободная VRAM", settings.ShowFreeMemory);
        Check("ShowDiskSpace", "Ёмкость и свободное место дисков", settings.ShowDiskSpace);
        Check("ShowTemperatures", "Температуры", settings.ShowTemperatures);
        Check("ShowFans", "Вентиляторы", settings.ShowFans);
        Check("ShowDiskActivity", "Чтение и запись дисков", settings.ShowDiskActivity);
        Check("ShowDiskHealth", "Ресурс SSD и состояние HDD (SMART)", settings.ShowDiskHealth);
        Check("ShowDesStatus", "Статус DES Control Center", settings.ShowDesStatus);
        Check("ShowNetwork", "Скорость сети", settings.ShowNetwork);
        Check("ShowIp", "IP-адреса", settings.ShowIp);
        Heading("Сетевой адаптер");
        var adapters = new List<AdapterChoice> { new("", "Автоматически") };
        adapters.AddRange(NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => new AdapterChoice(n.Id, n.Name + (n.OperationalStatus == OperationalStatus.Up ? "" : " (отключён)"))));
        if (!adapters.Any(a => a.Id == settings.NetworkAdapterId)) adapters.Add(new(settings.NetworkAdapterId, "Сохранённый адаптер (недоступен)"));
        _adapter.ItemsSource = adapters; _adapter.SelectedValue = settings.NetworkAdapterId; _fields.Children.Add(_adapter);
        Heading("Цветовые пороги");
        Check("ColorThresholds", "Выделять предупреждения цветом", settings.ColorThresholds);
        _fields.Children.Add(new TextBlock { Text = "Оранжевый — предупреждение, красный — критический порог. Значения можно изменить под своё оборудование.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) });
        var letters = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name.TrimEnd('\\')).OrderBy(n => n).ToArray();
        foreach (var key in new[] { "CPU", "GPU", "MB" }.Concat(letters))
            Limit(key, true, settings.GetTemperatureLimits(key), $"Температура {key}, °C");
        foreach (var key in letters) Limit(key, false, settings.GetFreeLimits(key), $"Свободно {key}, %");
    }

    private void Heading(string text) => _fields.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
    private void Number(string key, string label, double value)
    {
        var box = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture), Width = 90, Margin = new Thickness(5), Padding = new Thickness(4) };
        _numbers[key] = box;
        Row(label, box);
    }
    private void Check(string key, string label, bool value)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 5, 0, 5) };
        _checks[key] = check; _fields.Children.Add(check);
    }
    private void Row(string label, FrameworkElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(control, Dock.Right); row.Children.Add(control);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        _fields.Children.Add(row);
    }
    private void Limit(string key, bool temperature, ThresholdPair pair, string label)
    {
        var warn = new TextBox { Text = pair.Warning.ToString(), Width = 65, Margin = new Thickness(4), Padding = new Thickness(4) };
        var critical = new TextBox { Text = pair.Critical.ToString(), Width = 65, Margin = new Thickness(4), Padding = new Thickness(4) };
        var boxes = new StackPanel { Orientation = Orientation.Horizontal };
        boxes.Children.Add(new TextBlock { Text = "оранж.", VerticalAlignment = VerticalAlignment.Center }); boxes.Children.Add(warn);
        boxes.Children.Add(new TextBlock { Text = "красн.", VerticalAlignment = VerticalAlignment.Center }); boxes.Children.Add(critical);
        Row(label, boxes); _limits.Add((key, temperature, warn, critical));
    }
    private static double Parse(string text) => double.Parse(text.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
    private void Apply()
    {
        try
        {
            var font = Parse(_numbers["FontSize"].Text);
            var opacity = Parse(_numbers["BackgroundOpacity"].Text);
            var interval = Parse(_numbers["UpdateIntervalMs"].Text);
            if (!double.IsFinite(font) || font < 12 || font > 30 || !double.IsFinite(opacity) || opacity < 20 || opacity > 100 ||
                !double.IsFinite(interval) || interval < 500 || interval > 5000) throw new ArgumentException("Проверь диапазоны размера шрифта, плотности фона и интервала.");
            var edited = Result.Clone();
            edited.FontSize = font; edited.BackgroundOpacity = (int)opacity; edited.UpdateIntervalMs = (int)interval;
            foreach (var entry in _checks) typeof(AppSettings).GetProperty(entry.Key)!.SetValue(edited, entry.Value.IsChecked == true);
            edited.NetworkAdapterId = (string?)_adapter.SelectedValue ?? "";
            foreach (var limit in _limits)
            {
                var pair = new ThresholdPair(Parse(limit.Warning.Text), Parse(limit.Critical.Text));
                if (!pair.IsValid(limit.Temperature)) throw new ArgumentException(limit.Temperature
                    ? $"{limit.Key}: температурные пороги должны возрастать и находиться в диапазоне 0–150 °C."
                    : $"{limit.Key}: критический остаток должен быть меньше предупредительного; диапазон 0–100 %.");
                (limit.Temperature ? edited.TemperatureLimits : edited.DiskFreeLimits)[limit.Key] = pair;
            }
            Result = edited;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        { _error.Text = ex is ArgumentException ? ex.Message : "Введи числовые значения; дробную часть можно отделить запятой или точкой."; }
    }
    private sealed record AdapterChoice(string Id, string Name);
}
