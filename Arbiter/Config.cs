static class Config
{
    public static string RCCDirectory { get; private set; } = "";

    public static string BaseURL { get; private set; } = "www.roblox.com";
    public static bool SkipSysStats { get; private set; }

    public static string GSScript = "print('get a gameserver script nerd')";
    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--dir requires a value");

                    RCCDirectory = args[++i];
                    break;

                case "--skip-sysstats":
                    SkipSysStats = true;
                    break;

                case "--gscript":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--gscript requires a value");

                    var path = args[++i];

                    if (!File.Exists(path))
                        throw new FileNotFoundException("gameserver script not found", path);

                    GSScript = File.ReadAllText(path);
                    break;

                case "--baseurl":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--baseurl requires a value");

                    BaseURL = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(RCCDirectory) || !Directory.Exists(RCCDirectory))
        {
            RCCDirectory = AppContext.BaseDirectory;
        }
        Logger.Info("Config read");
    }
}