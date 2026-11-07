using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OsdPanel;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;

internal static class Program
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Output);
            File.Delete(Path.Combine(Output,"checks-error.txt"));
            if (args.Contains("--des"))
            {
                var desStatus = new DesStatusReader().Read(DateTimeOffset.UtcNow);
                var desReport = JsonSerializer.Serialize(desStatus,new JsonSerializerOptions{WriteIndented=true});
                File.WriteAllText(Path.Combine(Output,"live-des.json"),desReport);
                Console.WriteLine(desReport);
                Assert(desStatus != DesStatus.Unavailable,"actual DES export parsed");
                return 0;
            }
            if (args.Contains("--memory"))
            {
                using var service = new SensorsService();
                using var ready = new ManualResetEventSlim();
                service.Updated += snapshot => { if (snapshot.RamFreeBytes.HasValue && snapshot.VramFreeBytes.HasValue) ready.Set(); };
                service.Configure(new AppSettings { ShowIp = false });
                Assert(ready.Wait(TimeSpan.FromSeconds(15)), "live memory readings available");
                var memory = service.Snapshot;
                Assert(memory.RamFreeBytes >= 0 && memory.RamFreeBytes <= memory.RamTotalBytes &&
                    memory.VramFreeBytes >= 0 && memory.VramFreeBytes <= memory.VramTotalBytes, "live memory in range");
                var report = JsonSerializer.Serialize(new { memory.RamLoad, memory.RamFreeBytes, memory.RamTotalBytes,
                    RamFree = MemoryReading.Format(memory.RamFreeBytes), memory.VramLoad, memory.VramFreeBytes, memory.VramTotalBytes,
                    VramFree = MemoryReading.Format(memory.VramFreeBytes) }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(Output,"live-memory.json"),report); Console.WriteLine(report);
                return 0;
            }
            if (args.Contains("--gpu"))
            {
                var gpuComputer = new Computer { IsGpuEnabled = true };
                try
                {
                    gpuComputer.Open();
                    foreach (var gpu in gpuComputer.Hardware) gpu.Update();
                    Console.WriteLine(JsonSerializer.Serialize(gpuComputer.Hardware.Select(gpu => new { gpu.Name,
                        Identifier = gpu.Identifier.ToString(), DeviceId = gpu.GetType().GetProperty("DeviceId")?.GetValue(gpu),
                        Capacity = Dxgi.GetDedicatedVramBytes((string?)gpu.GetType().GetProperty("DeviceId")?.GetValue(gpu)),
                        Sensors = gpu.Sensors.Select(s => new { s.Name, Type = s.SensorType.ToString(), s.Value }) }), new JsonSerializerOptions { WriteIndented = true }));
                }
                finally { gpuComputer.Close(); }
                return 0;
            }
            if (args.Contains("--live")) { Live(); return 0; }
            CheckLogic();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var sensors = new SensorsService(false);
            var settings = new AppSettings();
            var panel = new MainWindow(settings, sensors);
            var snapshot = new SensorSnapshot { CpuTemp = "49°C", CpuTemperature = 49, GpuTemp = "47°C", GpuTemperature = 47,
                Des = new("DES: всё в порядке","Серверов онлайн: 8/8","#4CAF50"),
                MbTemp = "45°C", MbTemperature = 45, CpuLoad = "6%", GpuLoad = "0%", RamLoad = "31%", VramLoad = "21%",
                RamFreeBytes = 11.04 * 1024 * 1024 * 1024, RamTotalBytes = 16.0 * 1024 * 1024 * 1024,
                VramFreeBytes = 6.32 * 1024 * 1024 * 1024, VramTotalBytes = 8.0 * 1024 * 1024 * 1024,
                CpuFan = "1722 RPM", GpuFan = "0 RPM", AdapterName = "Ethernet", DownloadBytes = 12345678, UploadBytes = 870000,
                ExternalIp = "203.0.113.1", IpAddress = "192.168.1.2", Time = new DateTime(2026,10,12,12,34,56), Uptime = new TimeSpan(1,2,34,56) };
            foreach (var tuple in new[] { ("C:","447,02 ГБ","365,63 ГБ",48f), ("D:","447,13 ГБ","171,12 ГБ",30f),
                ("E:","3,63 ТБ","2,16 ТБ",31f) })
                snapshot.Disks.Add(new(tuple.Item1,tuple.Item2,tuple.Item3,25,tuple.Item4,0,0,"Example SSD",0));
            snapshot.Disks[1] = snapshot.Disks[1] with { ReadBytes = 125 * 1024 * 1024, WriteBytes = 8 * 1024 * 1024 };
            snapshot.Disks[0] = snapshot.Disks[0] with { Health = new(100,DiskHealthState.Good,"SMART A9") };
            snapshot.Disks[1] = snapshot.Disks[1] with { Health = new(100,DiskHealthState.Good,"SMART A9") };
            snapshot.Disks[2] = snapshot.Disks[2] with { Health = new(null,DiskHealthState.Good,"SMART 05/C5/C6") };
            snapshot.Disks.Add(new("F:","111,79 ГБ","100,00 ГБ",89,41,0,0,"AMD R5M120G8",1,
                new(94,DiskHealthState.Good,"SMART A9 RAW")));
            GameOverlayChecks.Run(settings,sensors,snapshot,Render,Drain,args.Contains("--skip-native-overlay"));
            RenderPanel(panel, snapshot, "panel-preview.png");
            Assert(((TextBlock)panel.FindName("DesSummaryText")).Text=="DES: всё в порядке" &&
                ((TextBlock)panel.FindName("DesSummaryText")).Foreground.ToString()=="#FF4CAF50","DES status and color displayed");
            Assert(((TextBlock)panel.FindName("RamFreeText")).Text=="11,04 ГБ" &&
                ((TextBlock)panel.FindName("VramFreeText")).Text=="6,32 ГБ", "free memory displayed in GiB");
            var warning = new SensorSnapshot { CpuTemp = "85°C", CpuTemperature = 85, GpuTemp = "92°C", GpuTemperature = 92 };
            warning.Disks.Add(snapshot.Disks[0] with { FreePercent = 4, Temperature = 65 });
            var warningPanel = new MainWindow(settings, sensors);
            RenderPanel(warningPanel, warning, "threshold-preview.png");
            Assert(((TextBlock)warningPanel.FindName("CpuTempText")).Foreground.ToString()=="#FFFFAA40" &&
                ((TextBlock)warningPanel.FindName("GpuTempText")).Foreground.ToString()=="#FFFF6464", "actual WPF threshold colors");
            var desProblem = new SensorSnapshot { Des = new("DES: проблем: 2","Серверов онлайн: 6/8","#FF6464") };
            var desProblemPanel = new MainWindow(settings,sensors);
            RenderPanel(desProblemPanel,desProblem,"des-problem-preview.png");
            Assert(((TextBlock)desProblemPanel.FindName("DesSummaryText")).Foreground.ToString()=="#FFFF6464","DES problem uses red");
            var fontSettings = settings.Clone(); fontSettings.FontSize = 24;
            RenderPanel(new MainWindow(fontSettings, sensors), snapshot, "large-font-preview.png");
            var largest = settings.Clone(); largest.FontSize = 30;
            RenderPanel(new MainWindow(largest, sensors), snapshot, "largest-font-preview.png");
            var compact = settings.Clone(); compact.ShowDiskActivity = false; compact.ShowNetwork = false; compact.ShowFreeMemory = false; compact.ShowDiskHealth = false; compact.ShowDesStatus = false;
            var compactPanel = new MainWindow(compact, sensors);
            Assert(compactPanel.FindName("ActivitySection") is FrameworkElement { Visibility: Visibility.Collapsed }, "activity toggle");
            Assert(compactPanel.FindName("NetworkSection") is FrameworkElement { Visibility: Visibility.Collapsed }, "network toggle");
            Assert(compactPanel.FindName("MemorySection") is FrameworkElement { Visibility: Visibility.Collapsed }, "free memory toggle");
            Assert(compactPanel.FindName("HealthSection") is FrameworkElement { Visibility: Visibility.Collapsed }, "SMART section toggle");
            Assert(compactPanel.FindName("DesSection") is FrameworkElement { Visibility: Visibility.Collapsed }, "DES section toggle");
            RenderPanel(compactPanel, snapshot, "compact-preview.png");
            var settingsDialog = new SettingsWindow(settings);
            Render(settingsDialog, "settings-preview.png", 590, 750);
            var settingsViewer = ((DockPanel)settingsDialog.Content).Children.OfType<ScrollViewer>().First();
            settingsViewer.ScrollToEnd(); Drain();
            Render(settingsDialog, "settings-thresholds-preview.png", 590, 750);
            CheckDialog(settings);
            Console.WriteLine("PASS: WPF renders, section toggles, font scaling, settings apply/cancel/validation.");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Output,"checks-error.txt"),ex.ToString()); Console.Error.WriteLine(ex); return 1; }
    }

    private static void CheckLogic()
    {
        var samples = new[] { new StorageSample(3,"F device",new[]{"F:\\"},30), new StorageSample(1,"C device",new[]{"C:\\"},40),
            new StorageSample(2,"E device",new[]{"E:\\"},50), new StorageSample(0,"D device",new[]{"D:\\"},60) };
        var mapping = StorageMapping.ByLetter(samples);
        Assert(mapping["C:"].Index == 1 && mapping["D:"].Index == 0 && mapping["F:"].Index == 3, "shuffled physical disk mapping");
        Assert(!mapping.ContainsKey("Z:"), "missing disk does not inherit a temperature");
        var ambiguous = StorageMapping.ByLetter(samples.Concat(new[] { new StorageSample(4,"other",new[]{"C:\\"},99) }));
        Assert(!ambiguous.ContainsKey("C:"), "ambiguous disks stay unmapped");
        var split = StorageMapping.ByLetter(new[] { new StorageSample(5,"split",new[]{"G:\\","H:\\"},42) });
        Assert(split["G:"] == split["H:"], "two letters on one physical disk");
        var afterClone = StorageMapping.ByLetter(new[] {
            new StorageSample(2,"HDD formerly F",new[]{"E:\\"},37),
            new StorageSample(0,"unchanged SSD",new[]{"D:\\"},33),
            new StorageSample(1,"system SSD formerly E",new[]{"C:\\"},48) });
        Assert(afterClone.Count==3 && afterClone["C:"].Device=="system SSD formerly E" && afterClone["E:"].Device=="HDD formerly F" &&
            !afterClone.ContainsKey("F:"), "cloned OS, renamed volumes and disconnected old system disk");
        var oldLetters = new[] { new StorageSample(2,"Toshiba",new[]{"F:"},31,Health:new(null,DiskHealthState.Good,"SMART")),
            new StorageSample(1,"SSD",new[]{"E:"},48,Health:new(100,DiskHealthState.Good,"A9")) };
        var rebound = StorageMapping.ByTopology(oldLetters,new Dictionary<string,int>{{"C:",1},{"E:",2}});
        Assert(rebound["C:"].Health!.RemainingLife==100 && rebound["E:"].Device=="Toshiba" && !rebound.ContainsKey("F:"), "live letter rename rebinds SMART and temperatures");
        Assert(StorageMapping.ByTopology(oldLetters,new Dictionary<string,int>()).Count==0,"failed topology never reuses stale letters");
        CheckSmart();
        CheckDes();
        var legacyThresholds = new AppSettings();
        legacyThresholds.TemperatureLimits["C:"] = new(50,60);
        legacyThresholds.TemperatureLimits["F:"] = new(45,65);
        legacyThresholds.Normalize();
        Assert(legacyThresholds.GetTemperatureLimits("C:")==new ThresholdPair(55,60) && legacyThresholds.GetTemperatureLimits("E:")==new ThresholdPair(55,60) &&
            legacyThresholds.GetTemperatureLimits("F:")==new ThresholdPair(45,65) && legacyThresholds.GetTemperatureLimits("CPU")==new ThresholdPair(80,90), "disk threshold migration preserves custom settings");
        Assert(Thresholds.Temperature(54.9,new(55,60),"#FFD700",true)=="#FFD700" &&
            Thresholds.Temperature(55,new(55,60),"#FFD700",true)=="#FFAA40" &&
            Thresholds.Temperature(60,new(55,60),"#FFD700",true)=="#FF6464", "disk warning starts at 55 and critical stays at 60");
        legacyThresholds.TemperatureLimits["C:"] = new(50,60);
        legacyThresholds.Normalize();
        Assert(legacyThresholds.GetTemperatureLimits("C:")==new ThresholdPair(50,60), "one-time migration allows later user adjustments");
        Assert(Thresholds.Temperature(null,new(80,90),"White",true)=="White", "missing temperature is neutral");
        Assert(Thresholds.Temperature(80,new(80,90),"White",true)=="#FFAA40", "warning boundary");
        Assert(Thresholds.Temperature(90,new(80,90),"White",true)=="#FF6464", "critical boundary");
        Assert(Thresholds.FreeSpace(5,new(10,5),true)=="#FF6464", "free space lower boundary");
        Assert(Thresholds.FreeSpace(1,new(10,5),false)=="#FFD700", "disabled thresholds");
        Assert(TrafficSampler.FormatRate(null)=="—" && TrafficSampler.FormatRate(1024)=="1,0 КБ/с", "traffic units and unknown sample");
        var gpuMemory = MemoryReading.FromGpu(MemoryReading.ToBytes(8192,SensorType.SmallData), MemoryReading.ToBytes(2048,SensorType.SmallData), null);
        Assert(gpuMemory.Load=="25%" && MemoryReading.Format(gpuMemory.Free)=="6,00 ГБ", "GPU memory megabytes convert to GiB");
        Assert(MemoryReading.ToBytes(8,SensorType.Data)==MemoryReading.ToBytes(8192,SensorType.SmallData), "memory sensor units agree");
        Assert(MemoryReading.FromGpu(8192,null,4096).Load=="50%", "GPU free sensor fallback");
        Assert(MemoryReading.FromGpu(8192,9000,null).Free==null && MemoryReading.Format(null)=="—", "invalid and missing memory stay unknown");
        var settings = new AppSettings { FontSize=24, BackgroundOpacity=65, ShowNetwork=false, Locked=true, NetworkAdapterId="test" };
        settings.TemperatureLimits["F:"] = new(45,55); settings.DiskFreeLimits["F:"] = new(12,3);
        var path = Path.Combine(Output,"test-settings.json"); settings.Save(path);
        var loaded = AppSettings.Load(path);
        Assert(loaded.FontSize==24 && loaded.BackgroundOpacity==65 && !loaded.ShowNetwork && loaded.Locked && loaded.NetworkAdapterId=="test" &&
            loaded.GetTemperatureLimits("F:")==new ThresholdPair(45,55) && loaded.GetFreeLimits("F:")==new ThresholdPair(12,3), "settings persistence");
        File.WriteAllText(path,"{\"Left\":123,\"Top\":456,\"TopMost\":false}");
        var legacy = AppSettings.Load(path);
        Assert(legacy.Left==123 && legacy.Top==456 && legacy.FontSize==18 && !legacy.TopMost, "legacy settings migration");
        Console.WriteLine("PASS: disk identities, ambiguity, split volumes, thresholds, rates, persistence and legacy settings.");
    }
    private static void CheckSmart()
    {
        var limits = new Dictionary<byte,byte>{{0xA9,0},{0x05,50},{0xC5,0},{0xC6,0}};
        var life = new[]{new SmartValue(0xA9,100,100)};
        Assert(DiskHealthReader.Evaluate("SSD","HPS2227I",life,limits).RemainingLife==100,"system SSD A9 profile");
        Assert(DiskHealthReader.Evaluate("ADATA SU650","XD0R630B",life,limits).RemainingLife==100,"ADATA A9 profile");
        var amdLife = new[]{new SmartValue(0xA9,100,94)};
        Assert(DiskHealthReader.Evaluate("AMD R5M120G8","V0718B0",amdLife,limits).RemainingLife==94,"AMD uses A9 RAW 94 rather than normalized 100");
        Assert(DiskHealthReader.Evaluate("AMD R5M120G8","other",amdLife,limits).RemainingLife==null,"unknown AMD firmware stays unsupported");
        Assert(DiskHealthReader.Evaluate("AMD R5M120G8","V0718B0",new[]{new SmartValue(0xA9,100,101)},limits).RemainingLife==null,"invalid AMD RAW percent stays unknown");
        Assert(DiskHealthReader.Evaluate("AMD R5M120G8","V0718B0",new[]{new SmartValue(0xA9,100,0)},limits).State==DiskHealthState.Bad,"AMD zero RAW resource is critical");
        Assert(DiskHealthReader.Evaluate("Other SSD","unknown",life,limits)==DiskHealth.Unknown,"unknown A9 semantics are not guessed");
        Assert(DiskHealthReader.Evaluate("SSD","HPS2227I",new[]{new SmartValue(0xA9,255,255)},limits).RemainingLife==null,"invalid percent stays unknown");
        Assert(DiskHealthReader.Evaluate("SSD","HPS2227I",new[]{new SmartValue(0xA9,10,10)},limits).State==DiskHealthState.Warning,"low life warning");
        Assert(DiskHealthReader.Evaluate("SSD","HPS2227I",new[]{new SmartValue(0xA9,0,0)},limits).State==DiskHealthState.Bad,"zero life critical");
        var hdd = new[]{new SmartValue(0x05,100,0),new SmartValue(0xC5,100,0),new SmartValue(0xC6,100,0)};
        Assert(DiskHealthReader.Evaluate("TOSHIBA HDWT740","KQ0K0R",hdd,limits).Text=="Хорошо","HDD good has no invented percentage");
        hdd[1] = new(0xC5,100,1);
        Assert(DiskHealthReader.Evaluate("TOSHIBA HDWT740","KQ0K0R",hdd,limits).State==DiskHealthState.Warning,"HDD pending sector warning");
        hdd[0] = new(0x05,40,0);
        Assert(DiskHealthReader.Evaluate("TOSHIBA HDWT740","KQ0K0R",hdd,limits).State==DiskHealthState.Bad,"normalized SMART threshold failure");
        Assert(DiskHealthReader.Evaluate("TOSHIBA HDWT740","KQ0K0R",Array.Empty<SmartValue>(),limits)==DiskHealth.Unknown,"missing SMART data neutral");
    }
    private static void CheckDes()
    {
        var now = new DateTimeOffset(2026,10,26,5,0,0,TimeSpan.FromHours(7));
        const string line = "DES: всё в порядке · серверов 8/8";
        Dictionary<string,object?> Export() => new() {
            ["running"]=true,["health"]="Ok",["problems"]=0,["silenced"]=0,
            ["serversOnline"]=8,["serversTotal"]=8,["serversPending"]=0,
            ["problemList"]=Array.Empty<string>(),["warningList"]=Array.Empty<string>(),["updatedAt"]=now };
        DesStatus Status(Action<Dictionary<string,object?>>? edit=null)
        { var data=Export(); edit?.Invoke(data); return DesStatusReader.Parse(JsonSerializer.Serialize(data),line,now); }
        Assert(Status().Color=="#4CAF50" && Status().Servers=="Серверов онлайн: 8/8","DES healthy export");
        Assert(DesStatusReader.Parse(JsonSerializer.Serialize(Export()),"DES: проблем: 1",now).Summary=="DES: статус обновляется","DES new problem text overrides old healthy JSON until exports agree");
        Assert(DesStatusReader.Parse(JsonSerializer.Serialize(Export()),"DES: всё в порядке · серверов 7/8",now).Color!="#4CAF50","DES mismatched server count never green");
        Assert(Status(d=>d["problems"]=2).Color=="#FF6464","DES JSON problems override old healthy text");
        Assert(Status(d=>d["problemList"]=new[]{"failure"}).Color=="#FF6464","DES problem list overrides zero count");
        Assert(Status(d=>d["warningList"]=new[]{"warning"}).Color=="#FFAA40","DES warnings");
        Assert(Status(d=>d["silenced"]=1).Summary=="DES: приглушено: 1","DES silenced state visible");
        Assert(Status(d=>{d["serversOnline"]=7;}).Color=="#FF6464","DES missing server");
        Assert(Status(d=>{d["serversOnline"]=7;d["serversPending"]=1;}).Summary=="DES: идёт проверка","DES pending checks");
        Assert(Status(d=>d["running"]=false).Summary=="DES не запущен","DES stopped");
        Assert(Status(d=>d["updatedAt"]=now.AddMinutes(-4)).Summary=="DES: данные устарели","DES stale heartbeat");
        Assert(Status(d=>d["updatedAt"]=now.AddHours(1)).Summary=="DES: проверь время","DES future timestamp");
        Assert(Status(d=>d["updatedAt"]=now.ToUniversalTime()).Color=="#4CAF50","DES timezone-independent freshness");
        Assert(Status(d=>d["health"]="Unrecognized").Color!="#4CAF50","DES unknown health never green");
        Assert(Status(d=>d.Remove("updatedAt"))==DesStatus.Unavailable,"DES missing heartbeat");
        Assert(Status(d=>d["serversOnline"]=9)==DesStatus.Unavailable,"DES inconsistent counters");
        Assert(DesStatusReader.Parse("{",line,now)==DesStatus.Unavailable,"DES partial JSON");
        var directory=Path.Combine(Output,"build","des-fixture"); Directory.CreateDirectory(directory);
        var jsonPath=Path.Combine(directory,"status.json"); var linePath=Path.Combine(directory,"status-line.txt");
        File.WriteAllText(jsonPath,JsonSerializer.Serialize(Export()),new System.Text.UTF8Encoding(true));
        File.WriteAllText(linePath,line,new System.Text.UTF8Encoding(true));
        var reader=new DesStatusReader(directory);
        Assert(reader.Read(now).Color=="#4CAF50","DES UTF8 BOM files");
        File.Delete(linePath);
        Assert(reader.Read(now).Color=="#4CAF50","DES JSON fallback when text is absent");
        File.WriteAllText(jsonPath,"{");
        Assert(reader.Read(now)==DesStatus.Unavailable,"DES damaged file clears previous good state");
        File.WriteAllText(jsonPath,new string('x',65537));
        Assert(reader.Read(now)==DesStatus.Unavailable,"DES oversized file");
        File.Delete(jsonPath);
        Assert(reader.Read(now)==DesStatus.Unavailable,"DES missing file");
        Console.WriteLine("PASS: DES export health, stale/closed/invalid states, file replacement, BOM and JSON/text consistency.");
    }

    private static void RenderPanel(MainWindow panel, SensorSnapshot snapshot, string name)
    {
        typeof(MainWindow).GetMethod("OnSensorsUpdated",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,new object[]{snapshot});
        Drain();
        Assert(((TextBlock)panel.FindName("CpuTempText")).Text==snapshot.CpuTemp,"snapshot displayed");
        Render(panel,name,panel.Width,null);
    }
    private static void Render(Window window,string name,double width,double? height)
    {
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.SetValue(TextBlock.FontSizeProperty,window.FontSize);
        root.SetValue(TextBlock.FontFamilyProperty,window.FontFamily);
        root.Measure(new System.Windows.Size(width,height ?? double.PositiveInfinity));
        var measuredHeight = height ?? root.DesiredSize.Height;
        root.Arrange(new Rect(0,0,width,measuredHeight)); root.UpdateLayout(); Drain();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(measuredHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output,name)); encoder.Save(stream);
        window.Content = root;
    }
    private static void CheckDialog(AppSettings original)
    {
        var dialog = new SettingsWindow(original);
        var fields = (Dictionary<string,TextBox>)typeof(SettingsWindow).GetField("_numbers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
        dialog.Dispatcher.BeginInvoke(() =>
        {
            fields["FontSize"].Text="0";
            typeof(SettingsWindow).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
            Assert(dialog.IsVisible && dialog.Result.FontSize==18,"invalid settings stay open");
            fields["FontSize"].Text="24";
            typeof(SettingsWindow).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
        });
        Assert(dialog.ShowDialog()==true && dialog.Result.FontSize==24 && original.FontSize==18,"apply does not mutate original settings");
        var cancel = new SettingsWindow(original);
        cancel.Dispatcher.BeginInvoke(() => cancel.Close());
        Assert(cancel.ShowDialog()!=true && original.FontSize==18,"cancel leaves settings intact");
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    private static void Live()
    {
        var computer = new Computer { IsStorageEnabled = true };
        try
        {
            computer.Open(); var list = new List<StorageSample>();
            foreach (var hw in computer.Hardware) hw.Update();
            Thread.Sleep(1100);
            foreach (var hw in computer.Hardware.OfType<AbstractStorage>())
            {
                hw.Update();
                var temperature = hw.Sensors.FirstOrDefault(s=>s.SensorType==SensorType.Temperature && s.Value.HasValue)?.Value;
                list.Add(new(hw.Index,hw.Name,hw.DriveInfos.Select(d=>d.Name).ToArray(),temperature,
                    hw.Sensors.FirstOrDefault(s=>s.Name=="Read Rate")?.Value,
                    hw.Sensors.FirstOrDefault(s=>s.Name=="Write Rate")?.Value));
            }
            var mapping = StorageMapping.ByLetter(list);
            var activity = mapping.Select(a=>new { Letter=a.Key, Read=a.Value.ReadBytes, Write=a.Value.WriteBytes }).ToArray();
            var sampler = new TrafficSampler(); var first = sampler.Sample(""); Thread.Sleep(1100); var second = sampler.Sample("");
            var report = JsonSerializer.Serialize(new { Storage=mapping, Activity=activity,
                Profiles=computer.Hardware.OfType<AbstractStorage>().Select(d=>new { d.Name,d.FirmwareRevision }),
                Network=new { second.Name, second.Download, second.Upload }, Admin=new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) },new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(Path.Combine(Output,"live-readings.json"),report);
            Console.WriteLine(report);
            var expected = CurrentWindowsDisks();
            File.WriteAllText(Path.Combine(Output,"windows-disk-mapping.json"), JsonSerializer.Serialize(expected,new JsonSerializerOptions{WriteIndented=true}));
            Assert(expected.Count>0 && mapping.Keys.OrderBy(k=>k).SequenceEqual(expected.Keys.OrderBy(k=>k)),"all currently connected fixed disks mapped");
            Assert(expected.All(pair=>mapping[pair.Key].Index==pair.Value),"sensor identities match independent Windows disk associations");
            Assert(activity.Length==expected.Count && activity.All(a=>a.Read.HasValue && a.Write.HasValue) && second.Download.HasValue,"live disk and network counters");
            using var service = new SensorsService();
            using var received = new ManualResetEventSlim();
            var count = 0;
            service.Updated += snapshot => { if (Interlocked.Increment(ref count) >= 2) received.Set(); };
            service.Configure(new AppSettings { ShowIp = false });
            Assert(received.Wait(TimeSpan.FromSeconds(20)), "sensor service publishes updates");
            var serviceSnapshot = service.Snapshot;
            Assert(serviceSnapshot.Disks.Count == expected.Count && serviceSnapshot.Disks.All(d => expected[d.Letter] == d.PhysicalIndex), "production service disk identities");
            Assert(StorageTopology.Read().OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"production topology agrees with independent associations");
            File.WriteAllText(Path.Combine(Output,"live-service.json"), JsonSerializer.Serialize(new {
                serviceSnapshot.CpuTemp, serviceSnapshot.GpuTemp, serviceSnapshot.MbTemp, serviceSnapshot.Disks,
                serviceSnapshot.RamLoad, serviceSnapshot.RamFreeBytes, serviceSnapshot.RamTotalBytes,
                serviceSnapshot.VramLoad, serviceSnapshot.VramFreeBytes, serviceSnapshot.VramTotalBytes,
                serviceSnapshot.AdapterName, serviceSnapshot.DownloadBytes, serviceSnapshot.UploadBytes,
                Autostart = Autostart.IsEnabled() }, new JsonSerializerOptions { WriteIndented = true }));
            Assert(serviceSnapshot.DownloadBytes.HasValue && serviceSnapshot.Disks.All(d => d.ReadBytes.HasValue && d.WriteBytes.HasValue), "production traffic counters");
            Assert(serviceSnapshot.RamFreeBytes is > 0 && serviceSnapshot.RamTotalBytes >= serviceSnapshot.RamFreeBytes &&
                serviceSnapshot.VramFreeBytes.HasValue && serviceSnapshot.VramTotalBytes >= serviceSnapshot.VramFreeBytes, "actual available RAM and VRAM");
            foreach (var disk in serviceSnapshot.Disks)
            {
                if (disk.Device == "SSD" || disk.Device == "ADATA SU650")
                    Assert(disk.Health?.RemainingLife==100 && disk.Health.State==DiskHealthState.Good,$"{disk.Letter} remaining life matches supplied CrystalDiskInfo screenshot");
                if (disk.Device == "TOSHIBA HDWT740")
                    Assert(disk.Health is { RemainingLife:null,State:DiskHealthState.Good },"Toshiba SMART state matches CrystalDiskInfo");
                if (disk.Device == "AMD R5M120G8")
                    Assert(disk.Health is { RemainingLife:94,State:DiskHealthState.Good },"AMD A9 RAW matches CrystalDiskInfo 94 percent");
            }
            Console.WriteLine("PASS: production sensor service, disk mapping, I/O and network updates.");
        }
        finally { computer.Close(); }
    }
    private static Dictionary<string,int> CurrentWindowsDisks()
    {
        var expected = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed && d.IsReady))
        {
            var letter = drive.Name.TrimEnd('\\');
            using var logical = new ManagementObject($"Win32_LogicalDisk.DeviceID=\"{letter}\"");
            using var partitions = logical.GetRelated("Win32_DiskPartition");
            var indices = new HashSet<int>();
            foreach (ManagementObject partition in partitions)
                using (partition) indices.Add(Convert.ToInt32(partition["DiskIndex"]));
            Assert(indices.Count==1,$"unambiguous physical disk for {letter}");
            expected[letter] = indices.Single();
        }
        return expected;
    }
    private static void Assert(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); }
}
