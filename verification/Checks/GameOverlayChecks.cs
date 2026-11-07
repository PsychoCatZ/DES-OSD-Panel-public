using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using OsdPanel;

internal static class GameOverlayChecks
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public static void Run(AppSettings settings, SensorsService sensors, SensorSnapshot snapshot,
        Action<Window,string,double,double?> render, Action drain, bool skipNative = false)
    {
        using var preview = new GameOverlayWindow(settings.Clone(),sensors,_=>{});
        preview.UpdateSnapshot(snapshot);
        var text = (TextBlock)preview.FindName("MetricsText");
        var line = System.Text.RegularExpressions.Regex.Replace(new TextRange(text.ContentStart,text.ContentEnd).Text,@"\s+"," ");
        var labels = new[]{"CPU: 6%","GPU: 0%","RAM: 31%","VRAM: 21%","t. CPU: 49°C","t. GPU: 47°C"};
        var previous = -1;
        foreach(var label in labels)
        {
            var position = line.IndexOf(label,StringComparison.Ordinal);
            Assert(position>previous,"all six metrics have requested order: "+label);
            previous=position;
        }
        Assert(text.Foreground.ToString()=="#FFADFF2F","salad green base color");
        Assert(((Run)preview.FindName("CpuTemperatureValue")).Foreground.ToString()=="#FFADFF2F","normal CPU temperature stays green");
        text.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));
        var width = Math.Ceiling(text.DesiredSize.Width+16);
        render(preview,"game-overlay-preview.png",width,null);
        var fixedWidth = text.DesiredSize.Width;
        foreach(var value in new[]{0,9,10,99,100})
        {
            preview.UpdateSnapshot(new SensorSnapshot { CpuLoad=$"{value}%",GpuLoad=$"{value}%",RamLoad=$"{value}%",VramLoad=$"{value}%",
                CpuTemp=$"{value}°C",GpuTemp=$"{value}°C" });
            text.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));
            Assert(Math.Abs(text.DesiredSize.Width-fixedWidth)<0.01,"fixed column width for "+value);
        }
        preview.UpdateSnapshot(new SensorSnapshot());
        text.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));
        Assert(Math.Abs(text.DesiredSize.Width-fixedWidth)<0.01,"unknown readings preserve column width");
        preview.UpdateSnapshot(new SensorSnapshot{ CpuTemp="90°C", CpuTemperature=90, GpuTemp="93°C",GpuTemperature=93,
            CpuLoad="100%",GpuLoad="99%",RamLoad="90%",VramLoad="95%" });
        Assert(((Run)preview.FindName("CpuTemperatureValue")).Foreground.ToString()=="#FFFF0000" &&
            ((Run)preview.FindName("GpuTemperatureValue")).Foreground.ToString()=="#FFFF0000","critical temperatures turn red");
        Assert(text.Foreground.ToString()=="#FFADFF2F","other metrics stay green during overheating");
        text.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));
        render(preview,"game-overlay-critical-preview.png",Math.Ceiling(text.DesiredSize.Width+16),null);
        var custom = settings.Clone(); custom.TemperatureLimits["CPU"] = new(90,95);
        preview.ApplySettings(custom);
        preview.UpdateSnapshot(new SensorSnapshot{ CpuTemp="92°C",CpuTemperature=92 });
        Assert(((Run)preview.FindName("CpuTemperatureValue")).Foreground.ToString()=="#FFADFF2F","custom critical threshold respected");
        Assert(((Run)preview.FindName("GpuTemperatureValue")).Text.Trim()=="—","unavailable sensors stay unknown");
        Assert(!GameOverlayWindow.IsCritical(float.NaN,90) && !GameOverlayWindow.IsCritical(null,90),"unknown and invalid temperatures do not signal overheating");

        if (skipNative)
        {
            Console.WriteLine("PASS: overlay metrics, fixed widths, rendering, colors and thresholds. SKIP: native overlay lifecycle/hotkey tests (--skip-native-overlay).");
            return;
        }
        var foreground = Interop.GetForegroundWindow();
        using var liveWindow = new GameOverlayWindow(settings.Clone(),sensors,_=>{});
        Assert(liveWindow.RegisterToggleHotkey(),"Ctrl+Alt+G registered; error="+liveWindow.HotkeyError);
        Assert(!liveWindow.IsVisible,"registered window starts hidden");
        var handle = new WindowInteropHelper(liveWindow).Handle;
        Assert(PostMessage(handle,Interop.WM_HOTKEY,new IntPtr(Interop.GAME_HOTKEY_ID),IntPtr.Zero),"post toggle message");
        drain();
        Assert(liveWindow.IsVisible && liveWindow.Topmost && liveWindow.Owner==null,"hotkey shows independent topmost window");
        Assert(Interop.GetForegroundWindow()==foreground,"show does not steal foreground focus");
        var style=Interop.GetWindowLong(handle,Interop.GWL_EXSTYLE);
        Assert((style & Interop.WS_EX_NOACTIVATE)!=0 && (style & Interop.WS_EX_TRANSPARENT)!=0,"no-activate and click-through styles enabled");
        Assert(!liveWindow.ShowInTaskbar && !liveWindow.ShowActivated,"overlay stays out of taskbar and does not activate");
        Interop.GetWindowRect(handle,out var initialRectangle);
        var sensorEvent = (Action<SensorSnapshot>?)typeof(SensorsService).GetField("Updated",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(sensors);
        System.Threading.Tasks.Task.Run(()=>sensorEvent?.Invoke(snapshot)).GetAwaiter().GetResult(); drain();
        Assert(((Run)liveWindow.FindName("CpuValue")).Text.Trim()=="6%","visible overlay updates from the shared sensor event on a worker thread");
        liveWindow.UpdateLayout(); drain();
        Interop.GetWindowRect(handle,out var updatedRectangle);
        Assert(initialRectangle.Left==updatedRectangle.Left && initialRectangle.Top==updatedRectangle.Top &&
            initialRectangle.Right==updatedRectangle.Right && initialRectangle.Bottom==updatedRectangle.Bottom,"live data does not move or resize overlay");
        PostMessage(handle,Interop.WM_HOTKEY,new IntPtr(Interop.GAME_HOTKEY_ID),IntPtr.Zero); drain();
        Assert(!liveWindow.IsVisible,"second toggle hides window");
        PostMessage(handle,Interop.WM_HOTKEY,new IntPtr(Interop.GAME_HOTKEY_ID),IntPtr.Zero); drain();
        Assert(liveWindow.IsVisible,"hotkey remains active after hide");
        liveWindow.Close();
        Assert(!liveWindow.IsVisible,"normal close hides instead of destroying hotkey window");
        liveWindow.Toggle(); drain();
        Assert(liveWindow.IsVisible,"hidden overlay can be shown again");
        Assert(Interop.GetWindowRect(handle,out var bounds),"overlay has native window rectangle");
        var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        Assert(bounds.Left>=area.Left && bounds.Right<=area.Right && bounds.Top>=area.Top && bounds.Bottom<=area.Bottom,"overlay fits monitor work area");
        liveWindow.Dispose();
        using var registrationAfterDispose = new GameOverlayWindow(settings.Clone(),sensors,_=>{});
        Assert(registrationAfterDispose.RegisterToggleHotkey(),"hotkey is released on disposal");
        foreach(var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var placed = settings.Clone();
            placed.GameOverlayPixelLeft=screen.WorkingArea.Left+8;
            placed.GameOverlayPixelTop=screen.WorkingArea.Top+8;
            using var monitorWindow = new GameOverlayWindow(placed,sensors,_=>{});
            monitorWindow.Toggle();
            monitorWindow.UpdateSnapshot(new SensorSnapshot { CpuLoad="100%",GpuLoad="100%",RamLoad="100%",VramLoad="100%",CpuTemp="100°C",GpuTemp="100°C" });
            monitorWindow.UpdateLayout(); drain();
            var monitorHandle = new WindowInteropHelper(monitorWindow).Handle;
            Assert(Interop.GetWindowRect(monitorHandle,out var actual),"native bounds on "+screen.DeviceName);
            Assert(actual.Left>=screen.WorkingArea.Left && actual.Right<=screen.WorkingArea.Right && actual.Top>=screen.WorkingArea.Top && actual.Bottom<=screen.WorkingArea.Bottom,
                "long metrics fit actual monitor "+screen.DeviceName);
        }
        Console.WriteLine("PASS: game overlay metrics, green/red colors, custom thresholds, native toggle messages, focus, click-through, monitor fit and hotkey cleanup.");
    }

    private static void Assert(bool condition,string description)
    { if(!condition) throw new Exception("FAIL: game overlay: "+description); }
}
