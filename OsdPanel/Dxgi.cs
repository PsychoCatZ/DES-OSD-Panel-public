using System.Runtime.InteropServices;

namespace OsdPanel;

public static class Dxgi
{
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersDelegate(IntPtr factory, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(IntPtr adapter, out DXGI_ADAPTER_DESC desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr obj);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
    }

    public static ulong? GetDedicatedVramBytes(string? deviceId = null)
    {
        var id = System.Text.RegularExpressions.Regex.Match(deviceId ?? "", @"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!id.Success) return null;
        var vendor = Convert.ToUInt32(id.Groups[1].Value, 16);
        var device = Convert.ToUInt32(id.Groups[2].Value, 16);
        IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == IntPtr.Zero) return null;

            var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdaptersDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 7 * IntPtr.Size));
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 2 * IntPtr.Size));

            ulong best = 0;
            var matches = 0;
            for (uint i = 0; ; i++)
            {
                IntPtr adapter = IntPtr.Zero;
                if (enumAdapters(factory, i, out adapter) != 0 || adapter == IntPtr.Zero) break;
                try
                {
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 8 * IntPtr.Size));
                    if (getDesc(adapter, out var desc) == 0 && desc.VendorId == vendor && desc.DeviceId == device && desc.DedicatedVideoMemory.ToUInt64() > 0)
                    {
                        best = desc.DedicatedVideoMemory.ToUInt64();
                        matches++;
                    }
                }
                finally
                {
                    Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 2 * IntPtr.Size))(adapter);
                }
            }
            release(factory);
            factory = IntPtr.Zero;
            return matches == 1 ? best : null;
        }
        catch { return null; }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                try
                {
                    Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 2 * IntPtr.Size))(factory);
                }
                catch { }
            }
        }
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);
}
