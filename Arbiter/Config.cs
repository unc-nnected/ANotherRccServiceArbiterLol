using System.Security.Cryptography;
using System.Text;

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
    public static bool removeRCCLogs { get; private set; } = false;
    public static bool Ready { get; set; } = false; // DO NOT CHANGE THIS. THIS WILL BE AUTO SET IF RCCSERVICES ARE READY.
    public static bool realtime { get; set; } = false;
    public static string name = "RCCService";
    public static bool signing { get; set; } = false;

    public static void ReloadScripts()
    {
        try
        {
            void LoadScript(ref string scriptField, string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;

                string content = File.ReadAllText(path);

                string scriptToLoad = null;

                if (signing)
                {
                    string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    if (lines.Length == 0 || !lines[0].StartsWith("%") || !lines[0].EndsWith("%"))
                    {
                        SignScript(path, content);
                        content = File.ReadAllText(path);
                        lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    }

                    string signatureLine = lines[0];
                    scriptToLoad = lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1)) : "";

                    if (!Verification(scriptToLoad, signatureLine))
                    {
                        Logger.Error($"Signature invalid: {path}");
                        return;
                    }
                }
                else
                {
                    scriptToLoad = content;
                }

                scriptField = scriptToLoad;
            }

            LoadScript(ref GSScript, GSScriptPath);
            LoadScript(ref RScript, RScriptPath);
            LoadScript(ref RAScript, RAScriptPath);
            LoadScript(ref RMScript, RMScriptPath);
            LoadScript(ref RMMScript, RMMScriptPath);
        }
        catch (Exception ex)
        {
            Logger.Error("Configuration reload failed: " + ex);
        }
    }
    public static string Signature(string script)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SECRET)))
        {
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(script));
            string base64 = Convert.ToBase64String(hash);
            char[] arr = base64.ToCharArray();
            Array.Reverse(arr);
            string reversedBase64 = new string(arr);
            Logger.Print($"Signed script with {reversedBase64}");
            return "--anrsalsig" + reversedBase64;
        }
    }

    public static bool Verification(string script, string signature)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(signature) || !signature.StartsWith("--rbxsig"))
                return false;

            string base64 = signature.Substring(9);

            char[] arr = base64.ToCharArray();
            Array.Reverse(arr);
            string originalBase64 = new string(arr);

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SECRET)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(script));
                string computedBase64 = Convert.ToBase64String(hash);

                return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computedBase64), Encoding.UTF8.GetBytes(originalBase64));
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyScript(string signedScript, out string script)
    {
        script = "";

        var lines = signedScript.Split('\n');

        if (!lines[0].StartsWith("--anrsalsig"))
            return false;

        string signature = lines[0].Substring(12).Trim();

        script = string.Join("\n", lines.Skip(1));

        return Verification(script, signature);
    }

    public static void SignScript(string path, string script)
    {
        try
        {
            string sig = Signature(script);
            string signedContent = sig + Environment.NewLine + script;

            File.WriteAllText(path, signedContent);

            if (debug)
                Logger.Info($"Signed script saved: {path}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save signed script {path}: {ex}");
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

                // this is much better
                case "--gscript":
                case "--rscript":
                case "--rascript":
                case "--rmscript":
                case "--rmmscript":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{args[i]} requires a value");

                    string path = args[++i];
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"Script not found for {args[i]}", path);

                    string scriptContent = File.ReadAllText(path);

                    if (signing)
                    {
                        SignScript(path, scriptContent);
                        scriptContent = File.ReadAllText(path);
                    }

                    switch (args[i - 1])
                    {
                        case "--gscript": GSScript = scriptContent; GSScriptPath = path; break;
                        case "--rscript": RScript = scriptContent; RScriptPath = path; break;
                        case "--rascript": RAScript = scriptContent; RAScriptPath = path; break;
                        case "--rmscript": RMScript = scriptContent; RMScriptPath = path; break;
                        case "--rmmscript": RMMScript = scriptContent; RMMScriptPath = path; break;
                    }
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

                case "--removercclogs": // experimental
                    removeRCCLogs = true;
                    break;

                case "--realtime": // realtime priority
                    realtime = true;
                    break;

                case "--name": // rccservice name (example: ACCService)
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--name requires a value");

                    name = args[++i];
                    break;

                case "--sign": // enable signing scripts for security
                    signing = true;
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