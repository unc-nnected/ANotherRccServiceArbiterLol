using System;
using System.IO;

static class Logger
{
    private static readonly object _lock = new();
    private static readonly string logFile = Path.Combine(AppContext.BaseDirectory, "anrsal.log");
    public static void Info(string msg) => Write("Output", msg, false, 6);
    public static void NetworkAudit(string msg) => Write("NetworkAudit", msg, true, 19);
    public static void RCCServiceInit(string msg) => Write("RCCServiceInit", msg, false, 6);
    public static void RCCExecuteInfo(string msg) => Write("RCCExecuteInfo", msg, true, 6);
    public static void RCCServiceJobs(string msg) => Write("RCCServiceJobs", msg, false, 6);
    public static void Error(string msg) => Write("Error", msg, false, 6);

    private static void Write(string level, string msg, bool dynamic, int verbosity = 2)
    {
        lock (_lock)
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string threadId = Environment.CurrentManagedThreadId.ToString("x");
            string type = dynamic ? "DFLog" : "FLog";
            string line = $"{timestamp},{threadId},{verbosity} [{type}::{level}] {msg}";

            Console.WriteLine(line);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch { }
        }
    }
}