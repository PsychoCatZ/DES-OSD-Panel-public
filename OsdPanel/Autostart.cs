using System.Security.Principal;

namespace OsdPanel;

public static class Autostart
{
    private static string UserId => WindowsIdentity.GetCurrent().User!.Value;
    private static string TaskName => $"OSD Panel - {UserId}";
    public static bool IsEnabled()
    {
        try
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            scheduler.Connect();
            return scheduler.GetFolder("\\").GetTask(TaskName).Enabled;
        }
        catch { return false; }
    }
    public static void SetEnabled(bool enabled)
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        scheduler.Connect();
        dynamic folder = scheduler.GetFolder("\\");
        if (!enabled) { folder.DeleteTask(TaskName, 0); return; }
        dynamic task = scheduler.NewTask(0);
        task.RegistrationInfo.Description = "OSD Panel: elevated interactive startup at user logon.";
        task.Principal.UserId = UserId;
        task.Principal.LogonType = 3;
        task.Principal.RunLevel = 1;
        task.Settings.Enabled = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 2;
        task.Settings.StartWhenAvailable = true;
        dynamic trigger = task.Triggers.Create(9);
        trigger.UserId = UserId; trigger.Delay = "PT10S";
        dynamic action = task.Actions.Create(0);
        action.Path = Environment.ProcessPath!;
        action.WorkingDirectory = System.IO.Path.GetDirectoryName(Environment.ProcessPath!)!;
        folder.RegisterTaskDefinition(TaskName, task, 6, UserId, null, 3);
    }
}
