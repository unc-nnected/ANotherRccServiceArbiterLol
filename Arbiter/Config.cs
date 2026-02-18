static class Config
{
    public static string RCCDirectory { get; private set; } = "";
    public static string BaseURL { get; private set; } = "www.roblox.com";
    public static bool SkipSysStats { get; private set; } = false;
    public static string GSScript = "print('get a gameserver script nerd')";
    public static string RScript = "print('get a place render script nerd')";
    public static string RAScript = "print('get a avatar render script nerd')";
    public static string RMScript = "print('get a model render script nerd')";
    public static string RMMScript = "print('get a mesh render script nerd')";
    public static string GSScriptPath { get; private set; } = "";
    public static string RScriptPath { get; private set; } = "";
    public static string RAScriptPath { get; private set; } = "";
    public static string RMScriptPath { get; private set; } = "";
    public static string RMMScriptPath { get; private set; } = "";
    public static int port { get; private set; } = 7000;
    public static int cores { get; private set; } = 1;
    public static bool debug { get; private set; } = false;
    public static string SECRET = "my-mother-ate-fries-lol";
    public static string AccessKey = "my-mother-ate-fries-lol";
    public static string FakeSECRET = "";
    public static bool experimental { get; private set; } = false;
    public static void ReloadScripts()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(GSScriptPath) && File.Exists(GSScriptPath))
            {
                GSScript = File.ReadAllText(GSScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(RScriptPath) && File.Exists(RScriptPath))
            {
                RScript = File.ReadAllText(RScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(RAScriptPath) && File.Exists(RAScriptPath))
            {
                RAScript = File.ReadAllText(RAScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(RMScriptPath) && File.Exists(RMScriptPath))
            {
                RMScript = File.ReadAllText(RMScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(RMMScriptPath) && File.Exists(RMMScriptPath))
            {
                RMMScript = File.ReadAllText(RMMScriptPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Configuration reload failed: " + ex);
        }
    }
    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir": // path for rccservice
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--dir requires a value");

                    RCCDirectory = args[++i];
                    break;

                case "--skip-sysstats": // skip anti skid
                    SkipSysStats = true;
                    break;

                case "--gscript": // gameserver script
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--gscript requires a value");

                    var path = args[++i];

                    if (!File.Exists(path))
                        throw new FileNotFoundException("gameserver script not found", path);

                    GSScript = File.ReadAllText(path);
                    break;

                case "--rscript": // render script
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rscript requires a value");

                    var pathnumbertwo = args[++i];

                    if (!File.Exists(pathnumbertwo))
                        throw new FileNotFoundException("place render script not found", pathnumbertwo);

                    RScript = File.ReadAllText(pathnumbertwo);
                    break;

                case "--rascript": // avatar render script
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rascript requires a value");

                    var pathnumberthree = args[++i];

                    if (!File.Exists(pathnumberthree))
                        throw new FileNotFoundException("avatar render script not found", pathnumberthree);

                    RAScript = File.ReadAllText(pathnumberthree);
                    break;

                case "--rmscript": // model render script
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rmscript requires a value");

                    var pathnumberfour = args[++i];

                    if (!File.Exists(pathnumberfour))
                        throw new FileNotFoundException("model render script not found", pathnumberfour);

                    RMScript = File.ReadAllText(pathnumberfour);
                    break;

                case "--rmmscript": // model render script
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--rmmscript requires a value");

                    var pathnumberfive = args[++i];

                    if (!File.Exists(pathnumberfive))
                        throw new FileNotFoundException("mesh render script not found", pathnumberfive);

                    RMMScript = File.ReadAllText(pathnumberfive);
                    break;

                case "--baseurl": // baseURL for soap
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--baseurl requires a value");

                    BaseURL = args[++i];
                    break;

                case "--port": // what port to listen on
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--port requires a value");

                    port = int.Parse(args[++i]); // why are we parsing for this
                    break;

                case "--cores": // how much cpu cores should we use for RCCService
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--cores requires a value");

                    cores = int.Parse(args[++i]); // why are we parsing for this
                    break;

                case "--debug": // debug mode (more information)
                    debug = true;
                    break;

                case "--secret": // access key for apis
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--secret requires a value");

                    i++;
                    SECRET = args[i];
                    FakeSECRET = SECRET;
                    break;

                case "--accesskey": // access key for gameserver
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--accesskey requires a value");

                    AccessKey = args[++i];
                    break;

                case "--experimental": // experimental
                    experimental = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(RCCDirectory) || !Directory.Exists(RCCDirectory))
        {
            // I GUESS WE'LL JUST SET OUR OWN
            RCCDirectory = AppContext.BaseDirectory;
        }
    }
}