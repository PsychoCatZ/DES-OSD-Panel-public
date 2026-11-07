using LibreHardwareMonitor.Hardware;

namespace OsdPanel;

public static class MemoryReading
{
    public static double? ToBytes(double value, SensorType type) =>
        !double.IsFinite(value) || value < 0 ? null : type == SensorType.Data ? value * 1024 * 1024 * 1024 :
        type == SensorType.SmallData ? value * 1024 * 1024 : null;

    public static (double? Total, double? Free, string Load) FromGpu(double? total, double? used, double? free)
    {
        if (!total.HasValue || !double.IsFinite(total.Value) || total <= 0) return (null, null, "—");
        // Prefer a matched total/used pair; free is a fallback from the same adapter.
        var available = used.HasValue && double.IsFinite(used.Value) && used >= 0 && used <= total
            ? total - used : free.HasValue && double.IsFinite(free.Value) && free >= 0 && free <= total ? free : null;
        return (total, available, available.HasValue ? $"{(total - available) / total * 100:F0}%" : "—");
    }

    public static string Format(double? bytes) => bytes.HasValue && double.IsFinite(bytes.Value) && bytes >= 0
        ? $"{(bytes.Value / (1024 * 1024 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} ГБ" : "—";
}
