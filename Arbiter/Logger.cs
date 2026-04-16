using System;
using System.IO;

static class Logger
{
    private static readonly object _lock = new();
    private static readonly string logFile = Path.Combine(AppContext.BaseDirectory, "anrsal.log");
    public static void Info(string msg) => Write(ConsoleColor.Blue, "IMPRNT", msg);
    public static void Warn(string msg) => Write(ConsoleColor.DarkYellow, "WARN", msg);
    public static void Error(string msg) => Write(ConsoleColor.Red, "ERROR", msg);
    public static void Print(string msg) => Write(ConsoleColor.Gray, "INFO", msg);

    private static void Write(ConsoleColor color, string level, string msg)
    {
        lock (_lock)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";

            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            catch
            {
                Console.WriteLine(line);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch {}
        }
    }
}