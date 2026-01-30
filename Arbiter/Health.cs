using System;
using System.IO;
using System.Runtime.InteropServices;

static class Health
{
    private static readonly bool IsWindows =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static float GetRAM()
    {
        return IsWindows ? GetRAMWindows() : GetRAMLinux();
    }

    public static bool IsHealthy(out float ram)
    {
        ram = GetRAM();
        return ram < 85f;
    }

    private static float GetRAMWindows()
    {
        MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
        mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

        if (!GlobalMemoryStatusEx(ref mem))
            return 0;

        ulong used = mem.ullTotalPhys - mem.ullAvailPhys;
        return (float)used / mem.ullTotalPhys * 100f;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    private static float GetRAMLinux()
    {
        long total = 0, available = 0;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:"))
                total = ParseKb(line);
            else if (line.StartsWith("MemAvailable:"))
                available = ParseKb(line);

            if (total > 0 && available > 0)
                break;
        }

        if (total == 0)
            return 0;

        return (1f - (float)available / total) * 100f;
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(parts[1]) * 1024;
    }
}