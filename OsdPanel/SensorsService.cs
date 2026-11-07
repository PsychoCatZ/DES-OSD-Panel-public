using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using LibreHardwareMonitor.Hardware.Gpu;

namespace OsdPanel;

public sealed class SensorSnapshot
{
    public DateTime Time { get; set; } = DateTime.Now;
    public TimeSpan Uptime { get; set; } = TimeSpan.FromMilliseconds(Environment.TickCount64);
    public DesStatus Des { get; set; } = DesStatus.Unavailable;
    public string CpuLoad { get; set; } = "—";
    public string GpuLoad { get; set; } = "—";
    public string RamLoad { get; set; } = "—";
    public string VramLoad { get; set; } = "—";
    public double? RamFreeBytes { get; set; }
    public double? RamTotalBytes { get; set; }
    public double? VramFreeBytes { get; set; }
    public double? VramTotalBytes { get; set; }
    public List<DiskReading> Disks { get; } = new();
    public string CpuTemp { get; set; } = "—";
    public string GpuTemp { get; set; } = "—";
    public string MbTemp { get; set; } = "—";
    public string CpuFan { get; set; } = "—";
    public string GpuFan { get; set; } = "—";
    public string IpAddress { get; set; } = "—";
    public string ExternalIp { get; set; } = "—";
    public string AdapterName { get; set; } = "—";
    public double? DownloadBytes { get; set; }
    public double? UploadBytes { get; set; }
    public float? CpuTemperature { get; set; }
    public float? GpuTemperature { get; set; }
    public float? MbTemperature { get; set; }
}

public class SensorsService : IDisposable
{
    private readonly Computer _computer;
    private readonly System.Threading.Timer _timer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private DateTime _lastExternalIpRefresh = DateTime.MinValue;
    private string _externalIp = "—";
    private readonly TrafficSampler _traffic = new();
    private AppSettings _options = new();
    private readonly object _pollGate = new();
    private bool _disposed;
    private readonly bool _enablePolling;
    private readonly Dictionary<string, ulong?> _gpuCapacities = new();
    private readonly StorageTopology _topology = new();
    private readonly DiskHealthReader _health = new();
    private readonly DesStatusReader _des = new();

    public event Action<SensorSnapshot>? Updated;

    public SensorSnapshot Snapshot { get; private set; } = new();

    public SensorsService(bool enablePolling = true)
    {
        _enablePolling = enablePolling;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsMemoryEnabled = true
        };
        if (enablePolling) _computer.Open();
        _timer = new System.Threading.Timer(_ => Poll(), null,
            enablePolling ? TimeSpan.Zero : System.Threading.Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_pollGate)
        {
            _disposed = true;
            try { _computer.Close(); } catch { }
            try { _http.Dispose(); } catch { }
        }
    }

    public void Configure(AppSettings settings)
    {
        var options = settings.Clone();
        options.Normalize();
        System.Threading.Volatile.Write(ref _options, options);
        if (_enablePolling) _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(options.UpdateIntervalMs));
    }

    private void Poll()
    {
        if (!System.Threading.Monitor.TryEnter(_pollGate)) return;
        try
        {
            if (_disposed) return;
            var options = System.Threading.Volatile.Read(ref _options);
            var snap = new SensorSnapshot();
            var storageTemps = new List<StorageSample>();

            foreach (var hw in _computer.Hardware)
            {
                try { hw.Update(); } catch { }
                try { Collect(hw, snap, storageTemps); } catch { }
            }

            FillDisks(snap, storageTemps);
            var network = _traffic.Sample(options.NetworkAdapterId);
            snap.IpAddress = network.Ip;
            snap.AdapterName = network.Name;
            snap.DownloadBytes = network.Download;
            snap.UploadBytes = network.Upload;

            if (options.ShowIp && (DateTime.Now - _lastExternalIpRefresh).TotalSeconds >= 60)
            {
                _lastExternalIpRefresh = DateTime.Now;
                _ = RefreshExternalIp();
            }
            snap.ExternalIp = _externalIp;
            if (options.ShowDesStatus) snap.Des = _des.Sample();

            Snapshot = snap;
            Updated?.Invoke(snap);
        }
        catch { }
        finally { System.Threading.Monitor.Exit(_pollGate); }
    }

    private void Collect(IHardware hw, SensorSnapshot snap, List<StorageSample> storageTemps)
    {
        foreach (var sub in hw.SubHardware)
        {
            try { sub.Update(); } catch { }
            try { Collect(sub, snap, storageTemps); } catch { }
        }

        switch (hw.HardwareType)
        {
            case HardwareType.Cpu:
                snap.CpuLoad = Fmt(FindSensor(hw, SensorType.Load, "CPU Total"));
                snap.CpuTemp = FmtTemp(FindSensor(hw, SensorType.Temperature, "Core Max")
                                       ?? FindSensor(hw, SensorType.Temperature, "CPU Package")
                                       ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue));
                snap.CpuTemperature = ParseTemperature(snap.CpuTemp);
                break;

            case HardwareType.Memory:
                var used = FindSensor(hw, SensorType.Data, "Memory Used");
                var avail = hw.Sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Data && s.Name.Contains("Available", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
                if (used?.Value != null && avail?.Value != null)
                {
                    var total = used.Value.Value + avail.Value.Value;
                    snap.RamLoad = total > 0 ? $"{used.Value.Value / total * 100:F0}%" : "—";
                    snap.RamFreeBytes = MemoryReading.ToBytes(avail.Value.Value, avail.SensorType);
                    snap.RamTotalBytes = MemoryReading.ToBytes(total, SensorType.Data);
                }
                else
                {
                    snap.RamLoad = Fmt(FindSensor(hw, SensorType.Load, "Memory"));
                }
                break;

            case HardwareType.Motherboard:
            case HardwareType.SuperIO:
                var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
                var mbTemp = temps.FirstOrDefault(t => t.Name.Contains("System", StringComparison.OrdinalIgnoreCase))
                             ?? temps.FirstOrDefault(t => !t.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                             ?? temps.FirstOrDefault();
                if (mbTemp != null) { snap.MbTemp = $"{mbTemp.Value.GetValueOrDefault():F0}°C"; snap.MbTemperature = mbTemp.Value; }
                var fans = hw.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue).ToList();
                var spinning = fans.Where(f => f.Value.GetValueOrDefault() > 0).ToList();
                var cpuFan = fans.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                             ?? spinning.FirstOrDefault()
                             ?? fans.FirstOrDefault();
                if (cpuFan != null) snap.CpuFan = $"{cpuFan.Value.GetValueOrDefault():F0} RPM";
                break;

            case HardwareType.Storage:
                var t = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
                if (hw is AbstractStorage storage)
                    storageTemps.Add(new(storage.Index, storage.Name,
                        storage.DriveInfos.Select(d => d.Name).ToArray(), t?.Value,
                        FindSensor(hw, SensorType.Throughput, "Read Rate")?.Value,
                        FindSensor(hw, SensorType.Throughput, "Write Rate")?.Value,
                        System.Threading.Volatile.Read(ref _options).ShowDiskHealth ? _health.Read(storage) : null));
                break;

            default:
                if (hw.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
                {
                    snap.VramTotalBytes = null;
                    snap.VramFreeBytes = null;
                    snap.VramLoad = "—";
                    snap.GpuLoad = Fmt(FindSensor(hw, SensorType.Load, "GPU Core"));
                    snap.GpuTemp = FmtTemp(FindSensor(hw, SensorType.Temperature, "GPU Core"));
                    snap.GpuTemperature = ParseTemperature(snap.GpuTemp);
                    var gpuFan = hw.Sensors.FirstOrDefault(s =>
                                     s.SensorType == SensorType.Fan && s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                 ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value.HasValue);
                    snap.GpuFan = gpuFan != null ? $"{gpuFan.Value.GetValueOrDefault():F0} RPM" : "—";

                    var totalBytes = GetMemoryBytes(hw, "GPU Memory Total");
                    if (!totalBytes.HasValue && hw is GenericGpu gpu)
                    {
                        var key = hw.Identifier.ToString();
                        if (!_gpuCapacities.TryGetValue(key, out var capacity))
                        {
                            capacity = Dxgi.GetDedicatedVramBytes(gpu.DeviceId);
                            _gpuCapacities[key] = capacity;
                        }
                        totalBytes = capacity;
                    }
                    var usedBytes = GetMemoryBytes(hw, "GPU Memory Used") ?? GetMemoryBytes(hw, "D3D Dedicated Memory Used");
                    var freeBytes = GetMemoryBytes(hw, "GPU Memory Free");
                    if (totalBytes is > 0)
                    {
                        var memory = MemoryReading.FromGpu(totalBytes, usedBytes, freeBytes);
                        snap.VramTotalBytes = memory.Total;
                        snap.VramFreeBytes = memory.Free;
                        snap.VramLoad = memory.Load;
                    }
                    else snap.VramLoad = Fmt(FindSensor(hw, SensorType.Load, "GPU Memory"));
                }
                break;
        }
    }

    private void FillDisks(SensorSnapshot snap, List<StorageSample> storageTemps)
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .OrderBy(d => d.Name)
                .ToList();
            var topology = _topology.Get(string.Join("|", drives.Select(d => d.Name)));
            var mapping = StorageMapping.ByTopology(storageTemps, topology);
            for (var i = 0; i < drives.Count; i++)
            {
                var d = drives[i];
                var letter = d.Name.TrimEnd('\\');
                var free = FormatBytes(d.TotalFreeSpace);
                mapping.TryGetValue(letter, out var physical);
                snap.Disks.Add(new(letter, FormatBytes(d.TotalSize), free,
                    d.TotalSize > 0 ? (double)d.TotalFreeSpace / d.TotalSize * 100 : 100,
                    physical?.Temperature, physical?.ReadBytes, physical?.WriteBytes,
                    physical?.Device ?? "датчик не сопоставлен", physical?.Index, physical?.Health));
            }
        }
        catch { }
    }

    private static ISensor? FindSensor(IHardware hw, SensorType type, string name) =>
        hw.Sensors.FirstOrDefault(s => s.SensorType == type &&
                                       s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);

    private static string Fmt(ISensor? s) => s?.Value != null ? $"{s.Value.Value:F0}%" : "—";

    private static string FmtTemp(ISensor? s) => s?.Value != null ? $"{s.Value.Value:F0}°C" : "—";
    private static float? ParseTemperature(string text) => float.TryParse(text.Replace("°C", ""), out var value) ? value : null;
    private static double? GetMemoryBytes(IHardware hw, string name)
    {
        var sensor = hw.Sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            s.SensorType is SensorType.Data or SensorType.SmallData && s.Value.HasValue);
        return sensor?.Value is float value ? MemoryReading.ToBytes(value, sensor.SensorType) : null;
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        var u = 0;
        while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
        var format = u >= 3 ? "F2" : "F0";
        return $"{bytes.ToString(format, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} {units[u]}";
    }

    private static string GetLocalIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(ua.Address))
                        return ua.Address.ToString();
                }
            }
        }
        catch { }
        return "—";
    }

    private async Task RefreshExternalIp()
    {
        try
        {
            _externalIp = (await _http.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch { _externalIp = "—"; }
    }

}
