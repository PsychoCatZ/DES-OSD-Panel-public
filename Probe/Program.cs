using System.Text;
using LibreHardwareMonitor.Hardware;

var sb = new StringBuilder();
var isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
sb.AppendLine($"IsAdmin: {isAdmin}");

var c = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMotherboardEnabled = true,
    IsStorageEnabled = true,
    IsMemoryEnabled = true
};
c.Open();

void Dump(IHardware hw, string indent)
{
    sb.AppendLine($"{indent}HW {hw.HardwareType} | {hw.Name}");
    foreach (var s in hw.Sensors)
        sb.AppendLine($"{indent}  [{s.SensorType}] {s.Name} = {(s.Value.HasValue ? s.Value.Value.ToString() : "null")}");
    foreach (var sub in hw.SubHardware)
    {
        sub.Update();
        Dump(sub, indent + "  ");
    }
}

foreach (var hw in c.Hardware)
{
    hw.Update();
    Dump(hw, "");
}
c.Close();

var path = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "probe_out.txt");
File.WriteAllText(path, sb.ToString());
Console.WriteLine("written to " + path);
