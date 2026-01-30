static class Config
{
    public static string RCCDirectory { get; private set; } = "";
    public static string BaseURL { get; private set; } = "www.roblox.com";
    public static bool SkipSysStats { get; private set; } = false;
    public static string GSScript = "print('get a gameserver script nerd')";
    public static string RScript = "print('get a place render script nerd')";
    public static string RAScript = "print('get a avatar render script nerd')";
    public static int port { get; private set; } = 7000;
    public static int cores { get; private set; } = 1;
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

                case "--rscript":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rscript requires a value");

                    var pathnumbertwo = args[++i];

                    if (!File.Exists(pathnumbertwo))
                        throw new FileNotFoundException("place render script not found", pathnumbertwo);

                    RScript = File.ReadAllText(pathnumbertwo);
                    break;

                case "--rascript":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rascript requires a value");

                    var pathnumberthree = args[++i];

                    if (!File.Exists(pathnumberthree))
                        throw new FileNotFoundException("avatar render script not found", pathnumberthree);

                    RScript = File.ReadAllText(pathnumberthree);
                    break;

                case "--baseurl":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--baseurl requires a value");

                    BaseURL = args[++i];
                    break;

                case "--port":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--port requires a value");

                    port = int.Parse(args[++i]); // why are we parsing for this
                    break;

                case "--cores":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--cores requires a value");

                    port = int.Parse(args[++i]); // why are we parsing for this
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