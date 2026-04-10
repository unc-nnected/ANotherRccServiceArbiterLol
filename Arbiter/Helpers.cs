/*
i haven't even bothered to comment in this code because if you understand it enough to read it, you understand it enough to not need comments. also if you don't understand it, comments won't help you.
*/
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

static class Helpers
{
    private static readonly Dictionary<string, GSMJob> Jobs = new();
    private static readonly object JobsLock = new();
    private static bool _gsmStarted;
    private static readonly object PoolLock = new();
    private static readonly int TargetPool = 5;
    private static readonly Dictionary<int, Process> idle = new();
    private static readonly Dictionary<int, Process> pending = new();
    private static readonly Dictionary<int, Process> active = new();
    private static bool _isFilling;
    private static readonly HttpClient client = new HttpClient
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private static readonly Dictionary<int, int> usage = new();
    private const int MaxJobs = 5; // this is needed for avatars so particles dont break in renders (not showing up at all)
    private static readonly HashSet<int> dedicated = new();
    private const int MaxDedicated = 2;

    private static int howmuchRCCService()
    {
        return idle.Count + pending.Count + active.Count;
    }

    private static void keepPoolsFull()
    {
        lock (PoolLock)
        {
            if (_isFilling)
                return;

            int current = howmuchRCCService();
            if (current >= TargetPool)
                return;

            _isFilling = true;
        }

        ThreadPool.QueueUserWorkItem(_ => // i guess bro
        {
            try
            {
                while (true)
                {
                    int port;

                    lock (PoolLock)
                    {
                        if (howmuchRCCService() >= TargetPool)
                            break;

                        port = GetPort();
                        pending[port] = null!;
                    }

                    var proc = startPending(port);

                    lock (PoolLock)
                    {
                        pending.Remove(port);

                        if (proc != null)
                            idle[port] = proc;
                    }

                    Thread.Sleep(200);
                }
            }
            finally
            {
                lock (PoolLock)
                    _isFilling = false;
            }
        });
    }

    private static Process? startPending(int port)
    {
        var proc = RCCService(port);
        if (proc == null)
        {
            return null;
        }

        lock (PoolLock)
        {
            usage[port] = 0;
        }

        const int attempts = 10;
        var alive = false;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var resp = client.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                alive = true;
                break;
            }
            catch { Thread.Sleep(200); }
        }

        if (!alive)
        {
            if (Config.debug) Logger.Warn($"Failed to connect to port {port}. This process cannot be used");
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return null;
        }

        try
        {
            string? tmp;
            SOAP(Guid.NewGuid().ToString(), port, 0, "local plr=game:GetService('Players'):CreateLocalPlayer(0) plr:LoadCharacter(false) return game:GetService('ThumbnailGenerator'):Click('PNG', 420, 420, true)", 10, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx");
        }
        catch {}

        return proc;
    }

    private static (Process? proc, int port) startDedicatedRCCService()
    {
        int port = GetPort();
        var proc = RCCService(port);
        if (proc == null) return (null, 0);

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, __) => RCCServiceExit(proc.Id, port);

        const int attempts = 20;
        bool alive = false;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var resp = client.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                alive = true;
                break;
            }
            catch { Thread.Sleep(250); }
        }
        if (!alive) { Kill(proc); return (null, 0); }

        try { string? tmp; SOAP(Guid.NewGuid().ToString(), port, 0, "Instance.new('Part', workspace) game:GetService('RunService'):Run() return true", 5, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx"); } catch { } // we probably dont need to render if were just starting a gameserver.. just run physics

        lock (PoolLock)
        {
            usage[port] = 0;
            active[port] = proc;
            dedicated.Add(port);
        }

        return (proc, port);
    }

    private static (Process? proc, int port, bool panic) getRCCService()
    {
        while (true)
        {
            lock (PoolLock)
            {
                if (idle.Count > 0)
                {
                    var kv = idle.OrderBy(kv => usage.TryGetValue(kv.Key, out var c) ? c : 0).First();

                    int port = kv.Key;
                    var proc = kv.Value;
                    idle.Remove(port);

                    if (!AwaitRCCService(port, 2))
                    {
                        Kill(proc);
                        usage.Remove(port);
                        continue;
                    }

                    active[port] = proc;
                    usage[port] = usage.TryGetValue(port, out var c) ? c + 1 : 1;

                    return (proc, port, false);
                }

                if (howmuchRCCService() < TargetPool)
                {
                    int port = GetPort();
                    pending[port] = null!;

                    Monitor.Exit(PoolLock);

                    var proc = startPending(port);

                    Monitor.Enter(PoolLock);

                    pending.Remove(port);

                    if (proc == null)
                        continue;

                    active[port] = proc;
                    usage[port] = 1;

                    return (proc, port, false);
                }

                Monitor.Wait(PoolLock);

                Logger.Warn("All jobs are busy. Spawning new RCCService..");

                Monitor.Exit(PoolLock);

                int pport = GetPort();
                var pproc = RCCService(pport);

                if (pproc == null)
                {
                    Monitor.Enter(PoolLock);
                    Thread.Sleep(100);
                    continue;
                }

                bool alive = false;
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        client.GetAsync($"http://127.0.0.1:{pport}/").GetAwaiter().GetResult();
                        alive = true;
                        break;
                    }
                    catch { Thread.Sleep(200); }
                }

                Monitor.Enter(PoolLock);

                if (!alive)
                {
                    Kill(pproc);
                    continue;
                }

                return (pproc, pport, true);
            }
        }
    }

    private static void releaseRCCService(int port)
    {
        lock (PoolLock)
        {
            if (!active.TryGetValue(port, out var proc))
                return;

            if (dedicated.Contains(port))
                return;

            active.Remove(port);

            if (usage.TryGetValue(port, out var count) && count >= MaxJobs)
            {
                Kill(proc);
                usage.Remove(port);
            }
            else
            {
                idle[port] = proc;
            }

            Monitor.Pulse(PoolLock);
        }
    }

    public static void killallthefags()
    {
        lock (PoolLock)
        {
            foreach (var kv in idle) Kill(kv.Value);
            foreach (var kv in pending) Kill(kv.Value);
            foreach (var kv in active) Kill(kv.Value);

            idle.Clear();
            pending.Clear();
            active.Clear();
        }
    }

    public static void runPoolManager()
    {
        new Thread(() =>
        {
            while (true)
            {
                keepPoolsFull();

                bool ready;

                lock (PoolLock)
                {
                    int total = active.Count + idle.Count;

                    if (total < TargetPool || pending.Count > 0)
                    {
                        ready = false;
                    }
                    else
                    {
                        ready = active.Keys.Concat(idle.Keys).All(port => AwaitRCCService(port, 5000));
                    }

                    Config.Ready = ready;
                }

                Thread.Sleep(2000);
            }
        })
        { IsBackground = true }.Start();
    }

    public class LuaValue
    {
        public enum ValueKind { String, Number, Boolean }

        public ValueKind Kind { get; }
        public string? StringValue { get; }
        public double? NumberValue { get; }
        public bool? BooleanValue { get; }

        private LuaValue(ValueKind kind, string? s = null, double? n = null, bool? b = null)
        {
            Kind = kind;
            StringValue = s;
            NumberValue = n;
            BooleanValue = b;
        }

        public static LuaValue FromString(string s) => new LuaValue(ValueKind.String, s: s);
        public static LuaValue FromNumber(double n) => new LuaValue(ValueKind.Number, n: n);
        public static LuaValue FromBoolean(bool b) => new LuaValue(ValueKind.Boolean, b: b);

        public string XmlTypeName()
        {
            return Kind switch
            {
                ValueKind.String => "String",
                ValueKind.Number => "Number",
                ValueKind.Boolean => "Boolean",
                _ => "String"
            };
        }
    }
    public static bool SysStats()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (!exe.Contains("Arbiter", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!IsTCPPortBindable(Config.port))
                return false;

            var current = Process.GetCurrentProcess();
            var same = Process.GetProcessesByName(current.ProcessName);
            if (same.Length > 1)
                return false;

            return true;
        }
        catch { return false; }
    }
    public static void StartGSM()
    {
        lock (JobsLock)
        {
            if (_gsmStarted)
                return;
            _gsmStarted = true;
        }

        new Thread(() =>
        {
            while (true)
            {
                lock (JobsLock)
                {
                    foreach (var job in Jobs.Values.ToList())
                    {
                        if (DateTime.UtcNow > job.ExpiresAt)
                        {
                            KillbyID(job.Pid);
                            Jobs.Remove(job.JobId);
                        }
                    }
                }

                Thread.Sleep(5000);
            }
        })
        { IsBackground = true }.Start();
    }

    private static bool IsTCPPortBindable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch { return false; }
    }

    public static bool IsAuthorized(string header)
    {
        if (!header.StartsWith("Bearer ")) return false;
        string token = header["Bearer ".Length..];
        using var sha = SHA256.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(Config.SECRET)));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected));
    }

    public static int GetPort()
    {
        while (true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (port >= 60000 && port <= 64989)
            {
                listener.Stop();
                return port;
            }
        }
    }

    public static int GetGameServerPort()
    {
        while (true)
        {
            using var udp = new UdpClient(0);
            int port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            if (port >= 40000 && port <= 59999)
            {
                udp.Dispose();
                return port;
            }
        }
    }

    public static bool Render(string jobId, int placeId, out string? render)
    {
        render = null;
        var (proc, SOAPPort, panic) = getRCCService();
        if (proc == null) return false;
        int pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.RScript, 120, 2, out render, jobtype: "OpenJobEx")) // we use OpenJobEx because it times out
        {
            Kill(proc);
            return false;
        }

        if (panic)
        {
            Kill(proc);
        }
        else
        {
            releaseRCCService(SOAPPort);
        }
        return true;
    }

    public static bool ARender(string jobId, int placeId, out string? render, bool headshot, bool isclothing)
    {
        render = null;
        var (proc, SOAPPort, panic) = getRCCService();
        if (proc == null) return false;
        int pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.RAScript, 60, 2, out render, false, 53640, headshot, isclothing, jobtype: "BatchJobEx"))
        {
            Kill(proc);
            return false;
        }

        if (panic)
        {
            Kill(proc);
        }
        else
        {
            releaseRCCService(SOAPPort);
        }
        return true;
    }

    public static bool MRender(string jobId, int placeId, out string? render)
    {
        render = null;
        var (proc, SOAPPort, panic) = getRCCService();
        if (proc == null) return false;
        int pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.RMScript, 60, 2, out render, jobtype: "BatchJobEx"))
        {
            Kill(proc);
            return false;
        }

        if (panic)
        {
            Kill(proc);
        }
        else
        {
            releaseRCCService(SOAPPort);
        }
        return true;
    }

    public static bool MMRender(string jobId, int placeId, out string? render)
    {
        render = null;
        var (proc, SOAPPort, panic) = getRCCService();
        if (proc == null) return false;
        int pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.RMMScript, 60, 2, out render, jobtype: "BatchJobEx"))
        {
            Kill(proc);
            return false;
        }

        if (panic)
        {
            Kill(proc);
        }
        else
        {
            releaseRCCService(SOAPPort);
        }
        return true;
    }

    public static int StartGameserver(string jobId, int placeId, out string? render, bool teamcreate, out int fakeahport, out int pid)
    {
        render = null;
        fakeahport = GetGameServerPort();
        pid = 0;
        bool panic = false;

        Process? proc;
        int SOAPPort;

        lock (PoolLock)
        {
            int dedicatedCount = dedicated.Count;

            if (dedicatedCount >= MaxDedicated)
            {
                Logger.Warn($"{dedicatedCount} dedicated RccService processes are active, using Pooled now");

                var kv = idle.FirstOrDefault();

                if (!kv.Equals(default(KeyValuePair<int, Process>)))
                {
                    SOAPPort = kv.Key;
                    proc = kv.Value;

                    idle.Remove(SOAPPort);
                    active[SOAPPort] = proc;
                }
                else
                {
                    (proc, SOAPPort, panic) = getRCCService();
                }
            }
            else
            {
                (proc, SOAPPort) = startDedicatedRCCService();
            }
        }

        if (proc == null) return 0;

        pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.GSScript, 604800, 1, out render, teamcreate, fakeahport, jobtype: "OpenJobEx"))
        {
            Kill(proc);

            lock (PoolLock)
            {
                dedicated.Remove(SOAPPort);
            }

            if (panic)
            {
                Kill(proc);
            }
            else
            {
                releaseRCCService(SOAPPort);
            }
            return 0;
        }

        lock (JobsLock)
        {
            Jobs[jobId] = new GSMJob
            {
                JobId = jobId,
                PlaceId = placeId,
                Pid = pid,
                Port = fakeahport,
                ExpiresAt = DateTime.UtcNow.AddSeconds(604800),
                LastHeartbeat = DateTime.UtcNow,
                Alive = true
            };
        }

        return fakeahport;
    }

    private static Process? RCCService(int port)
    {
        try
        {
            string exe = Path.Combine(Config.RCCDirectory, $"{Config.name}.exe");
            bool win = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            var psi = new ProcessStartInfo
            {
                FileName = win ? exe : "wine",
                Arguments = win ? $"-console -port {port}" : $"\"{exe}\" -console -port {port}",
                WorkingDirectory = Config.RCCDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var proc = Process.Start(psi);
            if (proc == null) return null;

            proc.EnableRaisingEvents = true;
            proc.Exited += (_, __) => RCCServiceExit(proc.Id, port);

            if (Config.realtime)
            {
                if (win) proc.PriorityClass = ProcessPriorityClass.RealTime;
            } else
            {
                if (win) proc.PriorityClass = ProcessPriorityClass.High;
            }

            bool ready = false;
            for (int i = 0; i < 5; i++) {
                try
                    {
                        var resp = client.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                        ready = true;
                        break;
                    }
                    catch {
                        Thread.Sleep(500);
                    }
                }

                if (!ready)
                {
                    Logger.Warn($"Failed to connect to port {port}.");
                }

                try
                {
                    string? r;
                    //SOAP(Guid.NewGuid().ToString(), port, 0, "return true", 10, 0, out r);
                    try { string? tmp; SOAP(Guid.NewGuid().ToString(), port, 0, "return true", 5, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx"); } catch { }
                    Logger.Info($"Started RccService process. Process ID = {proc.Id}, Port = {port}");
                }
                catch { }

                return proc;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to connect to port {port} because RccService process exited with {ex}");
                return null;
            }
    }

    private static void Kill(Process proc)
    {

        try { if (!proc.HasExited) proc.Kill(true); }
        catch (Exception ex) {
            Logger.Error($"Error disposing process running on port {proc}: {ex}");
        }
    }

    public static bool KillbyID(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.ProcessName.Contains(Config.name, StringComparison.OrdinalIgnoreCase)) return false;
            proc.Kill(true);
            return true;
        }
        catch { return false; }
    }

    private static bool AwaitRCCService(int port, int timeoutMs) // no longer used, maybe find a use for this later?
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var resp = client.GetAsync($"http://127.0.0.1:{port}").Result;
                if (resp.Content.Headers.ContentType?.MediaType == "text/xml") return true;
            }
            catch { }

            Thread.Sleep(250);
        }

        Logger.Error($"Failed to connect to port {port}. This process cannot be used");
        return false;
    }

    private static bool fixitup(string input, out string output)
    {
        output = input.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace('-', '+').Replace('_', '/');
        int mod = output.Length % 4;
        if (mod == 2) output += "==";
        else if (mod == 3) output += "=";
        else if (mod == 1) return false;

        try
        {
            Convert.FromBase64String(output);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SOAP(string jobId, int port, int placeId, string type, int howlonguntilwedie, int category, out string? render, bool teamcreate = false, int fakeahport = 53640, bool headshot = false, bool isclothing = false, List<LuaValue>? arguments = null, bool enforceSigning = true, string jobtype = "OpenJobEx")
    {
        render = null;

        Config.ReloadScripts();
        if (Config.signing && enforceSigning)
        {
            string script = type.Trim();
            string[] lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (lines.Length == 0)
            {
                Logger.Error("Script is empty.");
                return false;
            }

            string signatureLine = lines[0].Trim();
            string scriptContent = string.Join("\n", lines.Skip(1)).Trim();

            bool valid = Config.Verification(scriptContent, signatureLine);

            if (!valid)
            {
                Logger.Error($"Script verification failed, please check your script signatures and try again (signature: {signatureLine})");
                return false;
            }
            else
            {
                if (Config.debug)
                    Logger.Info("Signature is valid");
            }

            type = scriptContent;
        }
        try
        {
            ServicePointManager.Expect100Continue = Config.autistic;
            ServicePointManager.UseNagleAlgorithm = false;

            type = type.Replace("{placeId}", placeId.ToString());
            type = type.Replace("{jobId}", jobId);
            type = type.Replace("{port}", fakeahport.ToString());
            type = type.Replace("{accesskey}", Config.AccessKey);
            type = type.Replace("{teamcreate}", teamcreate.ToString().ToLower());
            type = type.Replace("{isheadshot}", headshot.ToString().ToLower());
            type = type.Replace("{isclothing}", isclothing.ToString().ToLower());

            var xml = new StringBuilder();
            if (arguments != null && arguments.Count > 0)
            {
                // what the fuck was i even thinking
                xml.AppendLine("    <rob:arguments>");
                xml.AppendLine("      <rob:ArrayOfLuaValue>");
                foreach (var a in arguments)
                {
                    xml.AppendLine("        <rob:LuaValue>");
                    xml.AppendLine($"          <rob:type>{SecurityElement.Escape(a.XmlTypeName())}</rob:type>");
                    switch (a.Kind)
                    {
                        case LuaValue.ValueKind.String:
                            xml.AppendLine($"          <rob:stringValue>{SecurityElement.Escape(a.StringValue ?? "")}</rob:stringValue>");
                            break;
                        case LuaValue.ValueKind.Number:
                            xml.AppendLine($"          <rob:numberValue>{a.NumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"}</rob:numberValue>");
                            break;
                        case LuaValue.ValueKind.Boolean:
                            xml.AppendLine($"          <rob:booleanValue>{(a.BooleanValue == true ? "true" : "false")}</rob:booleanValue>");
                            break;
                    }
                    xml.AppendLine("        </rob:LuaValue>");
                }
                xml.AppendLine("      </rob:ArrayOfLuaValue>");
                xml.AppendLine("    </rob:arguments>");
            }

            var soap = $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:rob=""http://{Config.BaseURL}/"">
<soapenv:Body>
  <rob:{jobtype}>
    <rob:job>
      <rob:id>{jobId}</rob:id>
      <rob:expirationInSeconds>{howlonguntilwedie}</rob:expirationInSeconds>
      <rob:cores>{Config.cores}</rob:cores>
    </rob:job>
    <rob:script>
      <rob:name>{jobId}</rob:name>
      <rob:script><![CDATA[
{type}
      ]]></rob:script>
{xml}
    </rob:script>
  </rob:{jobtype}>
</soapenv:Body>
</soapenv:Envelope>";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/");
            req.Version = HttpVersion.Version11;
            req.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            req.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(soap));
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
            req.Headers.Add("SOAPAction", jobtype);
            req.Headers.Host = $"127.0.0.1:{port}";
            req.Headers.ConnectionClose = true;
            client.DefaultRequestHeaders.ExpectContinue = Config.autistic;

            using var resp = client.SendAsync(req, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult();
            var responseText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Error("RCCService returned error:\n" + responseText);
                return false;
            }

            if (category == 2)
            {
                var doc = XDocument.Parse(responseText);
                if (doc.Descendants().Any(e => e.Name.LocalName == "faultstring")) return false;

                var value = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "value");
                if (value != null)
                {
                    fixitup(value.Value.Trim(), out render);
                } else
                {
                    Logger.Error("Render value wasn't found! ANRSAL doesn't support ASYNC renders yet.");
                    Logger.Error("RCCService's response: " + responseText);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SOAP exception:\n" + ex);
            return false;
        }
    }

    public static void RemoveJob(string jobId)
    {
        lock (JobsLock)
        {
            Jobs.Remove(jobId);
        }
    }

    public static GSMJob? GetJobByPID(int pid)
    {
        lock (JobsLock)
        {
            return Jobs.Values.FirstOrDefault(j => j.Pid == pid);
        }
    }

    public static bool RenewLease(string jobId, int seconds)
    {
        GSMJob job;
        lock (JobsLock)
        {
            if (!Jobs.TryGetValue(jobId, out job))
                return false;
        }

        if (!SOAPRenewLease(job.Port, job.JobId, seconds))
            return false;

        lock (JobsLock)
        {
            job.ExpiresAt = DateTime.UtcNow.AddSeconds(seconds);
            job.LastHeartbeat = DateTime.UtcNow;
            job.Alive = true;
        }

        return true;
    }

    private static bool SOAPRenewLease(int port, string jobId, int seconds)
    {
        try
        {

            var soap = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""
                  xmlns:rob=""http://{Config.BaseURL}/"">
  <soapenv:Body>
    <rob:RenewLease>
      <rob:jobID>{jobId}</rob:jobID>
      <rob:expirationInSeconds>{seconds}</rob:expirationInSeconds>
    </rob:RenewLease>
  </soapenv:Body>
</soapenv:Envelope>";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}");
            req.Content = new StringContent(soap, Encoding.UTF8, "text/xml");
            req.Headers.Add("SOAPAction", "RenewLease");

            using var resp = client.Send(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Error($"SOAP RenewLease failed: {ex.Message}");
            return false;
        }
    }

    public static List<object> GetAllJobs(int? port = null, int? limit = null)
    {
        lock (JobsLock)
        {
            var query = Jobs.Values
                            .Where(j => j.Alive && (port == null || j.Port == port))
                            .Select(j => new
                            {
                                j.JobId,
                                j.PlaceId,
                                j.Port,
                                expiresAt = j.ExpiresAt
                            });

            if (limit.HasValue)
                query = query.Take(limit.Value);

            return query.Cast<object>().ToList();
        }
    }

    public static GSMJob? GetJob(string jobId)
    {
        lock (JobsLock)
        {
            if (Jobs.TryGetValue(jobId, out var job))
            {
                return job;
            }
        }
        return null;
    }

    private static void RCCServiceExit(int pid, int port)
    {
        bool booldedicated;

        lock (PoolLock)
        {
            booldedicated = dedicated.Contains(port);

            idle.Remove(port);
            active.Remove(port);
            pending.Remove(port);
            usage.Remove(port);
            dedicated.Remove(port);

            Monitor.PulseAll(PoolLock);
        }

        if (!booldedicated)
        {
            keepPoolsFull();
        }

        keepPoolsFull();
    }
}