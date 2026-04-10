static class Logger
{
    private static readonly object _lock = new();

    public static void Info(string msg)
    {
        Write(ConsoleColor.Blue, msg);
    }

    public static void Warn(string msg)
    {
        Write(ConsoleColor.DarkYellow, msg);
    }

    public static void Error(string msg)
    {
        Write(ConsoleColor.Red, msg);
    }

    public static void Print(string msg)
    {
        Write(ConsoleColor.Gray, msg); 
    }

	private static void Write(ConsoleColor color, string msg)
	{
		lock (_lock)
		{
			Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ResetColor();
		}
	}
}
