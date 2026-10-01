using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sanare.Browser.Tests;

internal static class ChromiumProcessTree
{
    public static int CountDescendants()
    {
        if (!OperatingSystem.IsWindows()) return 0;

        var parents = new Dictionary<int, int>();
        var entry = new ProcessEntry32 { DwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return 0;
        try
        {
            if (!Process32First(snapshot, ref entry)) return 0;
            do
            {
                parents[(int)entry.Th32ProcessID] = (int)entry.Th32ParentProcessID;
                entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }

        var descendants = new HashSet<int> { Environment.ProcessId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in parents)
            {
                if (descendants.Contains(pair.Value) && descendants.Add(pair.Key)) changed = true;
            }
        }

        return Process.GetProcesses().Count(process => descendants.Contains(process.Id) &&
            (process.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
             process.ProcessName.Contains("chromium", StringComparison.OrdinalIgnoreCase)));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessID;
        public IntPtr Th32DefaultHeapID;
        public uint Th32ModuleID;
        public uint CntThreads;
        public uint Th32ParentProcessID;
        public int PcPriClassBase;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string SzExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}