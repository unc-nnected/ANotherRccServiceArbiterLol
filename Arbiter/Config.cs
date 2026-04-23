using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

static class Config
{
    public static string RCCDirectory { get; private set; } = "";
    public static string BaseURL { get; private set; } = "roblox.com";
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
    public static int port { get; private set; } = 7720;
    public static int cores { get; private set; } = 1;
    public static bool debug { get; private set; } = false;
    public static string SECRET = "my-mother-ate-fries-lol";
    public static string AccessKey = "my-mother-ate-fries-lol";
    public static string FakeSECRET = "";
    public static bool experimental { get; private set; } = false;
    public static bool removeRCCLogs { get; private set; } = false;
    public static bool Ready { get; set; } = false; // DO NOT CHANGE THIS. THIS WILL BE AUTO SET IF RCCSERVICES ARE READY.
    public static bool realtime { get; private set; } = false;
    public static string name = "RCCService";
    public static bool signing { get; private set; } = false;
    public static bool inject { get; private set; } = false;
    public static bool autistic { get; private set; } = false;
    public static bool poolgs { get; private set; } = false;
    public static bool service { get; private set; } = false;
    public static bool legacy { get; private set; } = false;

    public static void ReloadScripts()
    {
        try
        {
            void LoadScript(ref string scriptField, string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;

                string content = File.ReadAllText(path);
                content = content.Replace("\r\n", "\n").Trim();

                if (signing)
                {
                    var lines = content.Split('\n', StringSplitOptions.None);

                    bool hasSignature = lines.Length > 0 && lines[0].Trim().StartsWith("--anrsalsig%") && lines[0].Trim().EndsWith("%");

                    if (!hasSignature)
                    {
                        SignScript(path, content);
                        content = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
                        lines = content.Split('\n', StringSplitOptions.None);
                    }

                    string signatureLine = lines[0].Trim();
                    string scriptWithoutSig = string.Join("\n", lines.Skip(1)).Trim();

                    if (!Verification(scriptWithoutSig, signatureLine))
                    {
                        Logger.Error($"Signature invalid: {path}, {signatureLine}");
                        return;
                    }

                    scriptField = signatureLine + "\n" + scriptWithoutSig;
                }
                else
                {
                    scriptField = content;
                }
            }

            LoadScript(ref GSScript, GSScriptPath);
            LoadScript(ref RScript, RScriptPath);
            LoadScript(ref RAScript, RAScriptPath);
            LoadScript(ref RMScript, RMScriptPath);
            LoadScript(ref RMMScript, RMMScriptPath);

            if (debug)
                Logger.Info("Scripts reloaded successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error("Configuration reload failed: " + ex);
        }
    }

    public static string Signature(string script)
    {
        script = script.Replace("\r\n", "\n");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SECRET));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(script));

        string base64 = Convert.ToBase64String(hash);

        char[] arr = base64.ToCharArray();
        Array.Reverse(arr);
        string reversed = new string(arr);

        string finalSig = $"--anrsalsig%{reversed}%";

        if (debug)
            Logger.Print($"Signed script with {finalSig}");

        return finalSig;
    }

    public static bool Verification(string script, string signature)
    {
        try
        {
            string fakeahscript = script.TrimStart();
            if (fakeahscript.StartsWith("return ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(signature))
                return false;

            string sig = signature.Trim();

            string prefix = "--anrsalsig%";
            if (!sig.StartsWith(prefix) || !sig.EndsWith("%"))
                return false;

            string sigValue = sig.Substring(prefix.Length, sig.Length - prefix.Length - 1);

            char[] arr = sigValue.ToCharArray();
            Array.Reverse(arr);
            string originalBase64 = new string(arr);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SECRET));

            script = script.Replace("\r\n", "\n");

            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(script));
            string computedBase64 = Convert.ToBase64String(hash);

            if (debug)
            {
                Logger.Info($"Computed Base64: {computedBase64}");
            }

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computedBase64), Encoding.UTF8.GetBytes(originalBase64));
        }
        catch (Exception ex)
        {
            if (debug) Logger.Error($"Verification exception: {ex}");
            return false;
        }
    }

    public static void SignScript(string path, string script)
    {
        try
        {
            if (script.StartsWith("--anrsalsig%"))
            {
                int firstNewline = script.IndexOf('\n');
                if (firstNewline != -1)
                    script = script.Substring(firstNewline + 1);
            }

            script = script.Replace("\r\n", "\n");

            string sig = Signature(script);

            string signedContent = sig + "\n" + script;

            File.WriteAllText(path, signedContent);

            if (debug)
                Logger.Info($"Signed script: {path}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Couldn't save signed script {path}: {ex}");
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

                case "--verbose": // verbose
                    debug = true;
                    break;

                case "--debug": // debug mode (more information)
                    Logger.Warn("--debug is deprecated, use --verbose");
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

                case "--inject": // inject a script after spawning in new gameserver, its for JSON RCCServices because roblox sucks.
                    inject = true;
                    break;

                case "--autisticrcc": // FUCK YOU RCCSERVICE I HATE YOU
                    autistic = true;
                    break;

                case "--poolforgameservers": // use pooled rccservices for gameservers
                    poolgs = true;
                    break;

                case "--service": // act like a windows service
                    service = true;
                    break;

                case "--legacy": // use legacy stuff
                    legacy = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(RCCDirectory) || !Directory.Exists(RCCDirectory))
        {
            // I GUESS WE'LL JUST SET OUR OWN
            RCCDirectory = AppContext.BaseDirectory;
        }
    }
    public static JobType? ParseJobType(string? value)
    {
        return Enum.TryParse<JobType>(value, ignoreCase: true, out var type) ? type : null;
    }
}

// bunch of post data shit!
public record RenderRequest(string PlaceId);
public record ARenderRequest(string UserId, bool IsHeadshot, bool IsClothing);
public record MRenderRequest(string AssetId);
public record MMRenderRequest(string MeshId);
public record GameserverRequest(string PlaceId, bool TeamCreate);
public record KillRequest(int pid);
public record GSMJob
{
    public string JobId { get; init; } = "";
    public int Port { get; init; }
    public int SOAP { get; init; }
    public string PlaceId { get; init; }
    public int Pid { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public bool Alive { get; set; }
}
public record RenewLeaseBody(string gameId, int expirationInSeconds);
enum JobType
{
    GameServer,
    Avatar,
    Place,
    Model,
    Mesh
}