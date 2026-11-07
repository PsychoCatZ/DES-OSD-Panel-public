using System.Management;
using System.Text.RegularExpressions;

namespace OsdPanel;

public sealed class StorageTopology
{
    private string _letters = "";
    private long _stamp;
    private Dictionary<string, int> _mapping = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> Get(string letters)
    {
        var now = Environment.TickCount64;
        if (_stamp != 0 && letters == _letters && now - _stamp < 5000) return _mapping;
        // On failure clear old associations, rather than attach a sensor to a stale letter.
        _mapping = Read(); _letters = letters; _stamp = now;
        return _mapping;
    }

    public static Dictionary<string, int> Read()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var partitions = new ManagementObjectSearcher("SELECT DeviceID, DiskIndex FROM Win32_DiskPartition")
                { Options = { Timeout = TimeSpan.FromSeconds(2) } };
            using var partitionRows = partitions.Get();
            foreach (ManagementObject row in partitionRows)
                using (row) indices[(string)row["DeviceID"]] = Convert.ToInt32(row["DiskIndex"]);
            using var links = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition")
                { Options = { Timeout = TimeSpan.FromSeconds(2) } };
            using var linkRows = links.Get();
            var associations = new List<(string Letter, int Index)>();
            foreach (ManagementObject row in linkRows)
                using (row)
                {
                    var partition = DeviceId((string)row["Antecedent"]);
                    var letter = DeviceId((string)row["Dependent"]);
                    if (letter.Length == 2 && letter[1] == ':' && indices.TryGetValue(partition, out var index))
                        associations.Add((letter, index));
                }
            foreach (var group in associations.GroupBy(a => a.Letter, StringComparer.OrdinalIgnoreCase))
                if (group.Select(a => a.Index).Distinct().Count() == 1) result[group.Key] = group.First().Index;
        }
        catch { result.Clear(); }
        return result;
    }

    private static string DeviceId(string path) => Regex.Match(path, @"DeviceID=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
}
