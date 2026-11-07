namespace OsdPanel;

public sealed record DiskReading(string Letter, string Capacity, string Free, double FreePercent,
    float? Temperature, double? ReadBytes, double? WriteBytes, string Device, int? PhysicalIndex, DiskHealth? Health = null)
{
    public string Name => $"{Letter} {Capacity}";
    public string Temp => Temperature.HasValue ? $"{Temperature.Value:F0}°C" : "—";
    public string Activity => ReadBytes.HasValue && WriteBytes.HasValue && ReadBytes == 0 && WriteBytes == 0
        ? "простой" : $"чт. {TrafficSampler.FormatRate(ReadBytes)}";
    public string WriteActivity => Activity == "простой" ? "" : $"зап. {TrafficSampler.FormatRate(WriteBytes)}";
}
