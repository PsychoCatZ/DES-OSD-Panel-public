namespace OsdPanel;

public sealed record StorageSample(int Index, string Device, string[] Letters, float? Temperature,
    double? ReadBytes = null, double? WriteBytes = null, DiskHealth? Health = null);

public static class StorageMapping
{
    public static Dictionary<string, StorageSample> ByTopology(IEnumerable<StorageSample> devices, IReadOnlyDictionary<string, int> topology) =>
        ByLetter(devices.Select(device => device with { Letters = topology.Where(pair => pair.Value == device.Index).Select(pair => pair.Key).ToArray() }));
    public static Dictionary<string, StorageSample> ByLetter(IEnumerable<StorageSample> devices) =>
        devices.SelectMany(device => device.Letters.Select(letter => (Letter: letter.TrimEnd('\\').ToUpperInvariant(), Device: device)))
            .GroupBy(item => item.Letter, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Device.Index).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Device, StringComparer.OrdinalIgnoreCase);

}
