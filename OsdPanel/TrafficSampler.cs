using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OsdPanel;

public sealed class TrafficSampler
{
    private string _id = "";
    private long _received, _sent, _stamp;

    public (string Name, string Ip, double? Download, double? Upload) Sample(string preferredId)
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            var adapter = string.IsNullOrEmpty(preferredId)
                ? candidates.Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
                    .ThenByDescending(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                    .FirstOrDefault()
                : candidates.FirstOrDefault(n => n.Id == preferredId && n.OperationalStatus == OperationalStatus.Up);
            if (adapter == null) { _id = ""; _stamp = 0; return ("нет подключения", "—", null, null); }
            var statistics = adapter.GetIPv4Statistics();
            var stamp = Stopwatch.GetTimestamp();
            var seconds = _stamp == 0 ? 0 : (stamp - _stamp) / (double)Stopwatch.Frequency;
            double? down = null, up = null;
            if (_id == adapter.Id && seconds > 0 && statistics.BytesReceived >= _received && statistics.BytesSent >= _sent)
            {
                down = (statistics.BytesReceived - _received) / seconds;
                up = (statistics.BytesSent - _sent) / seconds;
            }
            _id = adapter.Id; _stamp = stamp;
            _received = statistics.BytesReceived; _sent = statistics.BytesSent;
            var ip = adapter.GetIPProperties().UnicastAddresses.FirstOrDefault(a =>
                a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            return (adapter.Name, ip, down, up);
        }
        catch { _stamp = 0; _id = ""; return ("недоступно", "—", null, null); }
    }

    public static string FormatRate(double? bytes)
    {
        if (!bytes.HasValue || !double.IsFinite(bytes.Value) || bytes < 0) return "—";
        var value = bytes.Value;
        string[] units = { "Б/с", "КБ/с", "МБ/с", "ГБ/с" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString(unit == 0 ? "F0" : "F1", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} {units[unit]}";
    }
}
