using LibreHardwareMonitor.Hardware.Storage;

namespace OsdPanel;

public enum DiskHealthState { Unknown, Good, Warning, Bad }
public sealed record SmartValue(byte Id, byte Current, ulong Raw);
public sealed record DiskHealth(int? RemainingLife, DiskHealthState State, string Source)
{
    public static DiskHealth Unknown { get; } = new(null, DiskHealthState.Unknown, "SMART недоступен или формат не распознан");
    public string Text => RemainingLife.HasValue ? State == DiskHealthState.Bad && RemainingLife > 0 ? $"{RemainingLife}% / плохо" : $"{RemainingLife}%" : State == DiskHealthState.Bad ? "Плохо" : State == DiskHealthState.Warning ? "Внимание" :
        State == DiskHealthState.Good ? "Хорошо" : "—";
    public string Color => State == DiskHealthState.Bad ? "#FF6464" : State == DiskHealthState.Warning ? "#FFAA40" : "#FFD700";
}

public sealed class DiskHealthReader
{
    private readonly Dictionary<AbstractStorage, (long Stamp, DiskHealth Value)> _cache = new();

    public DiskHealth Read(AbstractStorage storage)
    {
        var now = Environment.TickCount64;
        if (_cache.TryGetValue(storage, out var entry) && now - entry.Stamp < 60000) return entry.Value;
        var health = DiskHealth.Unknown;
        try
        {
            if (storage is AtaStorage ata && ata.Smart.IsValid)
            {
                // ATA SMART has 30 attribute slots; later vendor bytes are not attributes.
                var attributes = ata.Smart.ReadSmartData().Take(30).Where(a => a.Id != 0).Select(a =>
                {
                    ulong raw = 0;
                    for (var i = 0; i < Math.Min(6, a.RawValue?.Length ?? 0); i++) raw |= (ulong)a.RawValue![i] << (8 * i);
                    return new SmartValue(a.Id, a.CurrentValue, raw);
                }).ToArray();
                var thresholds = ata.Smart.ReadSmartThresholds().Take(30).Where(t => t.Id != 0)
                    .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Threshold);
                health = Evaluate(storage.Name, storage.FirmwareRevision, attributes, thresholds);
            }
        }
        catch { }
        _cache[storage] = (now, health);
        return health;
    }

    public static DiskHealth Evaluate(string model, string firmware, IReadOnlyList<SmartValue> values, IReadOnlyDictionary<byte, byte> thresholds)
    {
        if (values.Count == 0 || values.GroupBy(v => v.Id).Any(g => g.Count() > 1)) return DiskHealth.Unknown;
        var byId = values.ToDictionary(v => v.Id);
        var profileA9 = (model.Equals("SSD", StringComparison.OrdinalIgnoreCase) && firmware.Equals("HPS2227I", StringComparison.OrdinalIgnoreCase)) ||
            (model.Equals("ADATA SU650", StringComparison.OrdinalIgnoreCase) && firmware.Equals("XD0R630B", StringComparison.OrdinalIgnoreCase));
        int? remaining = profileA9 && byId.TryGetValue(0xA9, out var life) && life.Current <= 100 ? life.Current : null;
        var rawA9 = model.Equals("AMD R5M120G8", StringComparison.OrdinalIgnoreCase) && firmware.Equals("V0718B0", StringComparison.OrdinalIgnoreCase);
        if (rawA9 && byId.TryGetValue(0xA9, out var amdLife) && amdLife.Raw <= 100)
            remaining = (int)amdLife.Raw;
        var hddProfile = model.Equals("TOSHIBA HDWT740", StringComparison.OrdinalIgnoreCase);
        var exceeded = values.Any(v => thresholds.TryGetValue(v.Id, out var threshold) && threshold > 0 && v.Current <= threshold);
        if (exceeded || remaining == 0) return new(remaining, DiskHealthState.Bad, "Превышен порог SMART");
        if (remaining is <= 10) return new(remaining, DiskHealthState.Warning, "Остаточный ресурс SMART A9");
        if (hddProfile)
        {
            byte[] sectors = { 0x05, 0xC5, 0xC6 };
            if (sectors.Any(id => byId.TryGetValue(id, out var v) && v.Raw > 0))
                return new(null, DiskHealthState.Warning, "SMART 05/C5/C6: обнаружены проблемные сектора");
            if (sectors.All(byId.ContainsKey) && thresholds.Count > 0) return new(null, DiskHealthState.Good, "SMART: пороги и счётчики 05/C5/C6 без предупреждений");
        }
        return remaining.HasValue ? new(remaining, DiskHealthState.Good, "Остаточный ресурс SMART A9") : DiskHealth.Unknown;
    }
}
