using System.IO;
using System.Text;
using System.Text.Json;

namespace OsdPanel;

public sealed record DesStatus(string Summary, string Servers, string Color)
{
    public static DesStatus Unavailable { get; } = new("DES: статус недоступен", "Нет данных", "#A4ADB8");
}

public sealed class DesStatusReader
{
    private readonly string _directory;
    private long? _lastRead;
    private DesStatus _cached = DesStatus.Unavailable;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DesStatusReader(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesControlCenter");

    public DesStatus Sample()
    {
        var tick = Environment.TickCount64;
        if (_lastRead.HasValue && tick - _lastRead.Value < 3000) return _cached;
        _lastRead = tick;
        return _cached = Read(DateTimeOffset.UtcNow);
    }

    public DesStatus Read(DateTimeOffset now)
    {
        try
        {
            var json = ReadSmallFile(Path.Combine(_directory, "status.json"), 65536);
            string? line = null;
            try { line = ReadSmallFile(Path.Combine(_directory, "status-line.txt"), 4096); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            return Parse(json, line, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return DesStatus.Unavailable; }
    }

    private static string ReadSmallFile(string path, int max)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > max) throw new IOException("Status file is too large.");
        using var reader = new StreamReader(file, new UTF8Encoding(false, true), true);
        var text = new char[max + 1];
        var count = reader.ReadBlock(text, 0, text.Length);
        if (count > max) throw new IOException("Status file is too large.");
        return new string(text, 0, count);
    }

    public static DesStatus Parse(string json, string? line, DateTimeOffset now)
    {
        Export? data;
        try { data = JsonSerializer.Deserialize<Export>(json, JsonOptions); }
        catch (JsonException) { return DesStatus.Unavailable; }
        if (data?.Running == false) return new("DES не запущен", "", "#A4ADB8");
        if (data?.Running != true || !data.UpdatedAt.HasValue || data.ServersTotal is null or <= 0 ||
            data.ServersOnline is null or < 0 || data.ServersOnline > data.ServersTotal || data.Problems is null or < 0 ||
            data.ServersPending is < 0 || data.ServersPending > data.ServersTotal || data.Silenced is < 0 ||
            data.ServersOnline + (data.ServersPending ?? 0) > data.ServersTotal)
            return DesStatus.Unavailable;
        var servers = $"Серверов онлайн: {data.ServersOnline}/{data.ServersTotal}";
        var age = now - data.UpdatedAt.Value;
        if (age < TimeSpan.FromMinutes(-1)) return new("DES: проверь время", servers, "#FFAA40");
        if (age > TimeSpan.FromMinutes(3)) return new("DES: данные устарели", "Нет свежего статуса", "#FFAA40");
        var problems = Math.Max(data.Problems.Value, data.ProblemList?.Length ?? 0);
        var offline = data.ServersTotal.Value - data.ServersOnline.Value - (data.ServersPending ?? 0);
        if (problems > 0) return new($"DES: проблем: {problems}", servers, "#FF6464");
        if (offline > 0) return new("DES: серверы недоступны", servers, "#FF6464");
        if (data.ServersPending > 0) return new("DES: идёт проверка", servers, "#FFAA40");
        if (data.WarningList is { Length: > 0 }) return new("DES: предупреждения", servers, "#FFAA40");
        if (data.Silenced > 0) return new($"DES: приглушено: {data.Silenced}", servers, "#FFAA40");
        if (!string.Equals(data.Health, "Ok", StringComparison.OrdinalIgnoreCase))
            return new("DES: состояние неизвестно", servers, "#FFAA40");
        // Only reuse human-readable text when it agrees with the structured state.
        // The files may be replaced separately; a mismatched old line must not hide a problem.
        const string healthy = "DES: всё в порядке";
        var expectedLine = $"{healthy} · серверов {data.ServersOnline}/{data.ServersTotal}";
        if (line != null && !string.Equals(line.Trim(), expectedLine, StringComparison.Ordinal))
            return new("DES: статус обновляется", servers, "#FFAA40");
        return new(healthy, servers, "#4CAF50");
    }

    private sealed class Export
    {
        public bool? Running { get; set; }
        public string? Health { get; set; }
        public int? Problems { get; set; }
        public int? Silenced { get; set; }
        public int? ServersOnline { get; set; }
        public int? ServersTotal { get; set; }
        public int? ServersPending { get; set; }
        public JsonElement[]? ProblemList { get; set; }
        public JsonElement[]? WarningList { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
