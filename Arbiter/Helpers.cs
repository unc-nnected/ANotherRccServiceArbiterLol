/*
i haven't even bothered to comment in this code because if you understand it enough to read it, you understand it enough to not need comments. also if you don't understand it, comments won't help you.
*/
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public sealed class ReverseProxy
{
    private readonly UdpClient _listener;
    private readonly IPEndPoint _target;
    private readonly Dictionary<IPEndPoint, UdpClient> _clients = new();

    public ReverseProxy(int listen, int target)
    {
        _listener = new UdpClient(listen);
        _target = new IPEndPoint(IPAddress.Parse("127.0.0.1"), target);
    }

    public void Start()
    {
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (true)
        {
            UdpReceiveResult result;

            try
            {
                result = await _listener.ReceiveAsync();
            }
            catch
            {
                continue;
            }

            var client = result.RemoteEndPoint;

            if (!_clients.TryGetValue(client, out var server))
            {
                server = new UdpClient(0);
                _clients[client] = server;

                _ = Task.Run(() => HandleServerTraffic(client, server));
            }

            try
            {
                await server.SendAsync(result.Buffer, result.Buffer.Length, _target);
            }
            catch {}
        }
    }

    private async Task HandleServerTraffic(IPEndPoint client, UdpClient serverSocket) {
        while (true)
        {
            try
            {
                var result = await serverSocket.ReceiveAsync();
                await _listener.SendAsync(result.Buffer, result.Buffer.Length, client);
            }
            catch
            {
                break;
            }
        }

        serverSocket.Dispose();
        _clients.Remove(client);
    }
}

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

        if (Config.debug)
            Logger.NetworkAudit($"READY={Config.Ready} active={active.Count} idle={idle.Count} pending={pending.Count}");

        lock (PoolLock)
        {
            if (_isFilling || _isRefreshingIdle)
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
                    if (Config.RefreshIDLERCCServices && DateTime.UtcNow - _lastIdleRefresh >= TimeSpan.FromMinutes(5))
                    {
                        _isRefreshingIdle = true;

                        try
                        {
                            List<(int Port, Process Proc)> toRefresh;

                            lock (PoolLock)
                            {
                                toRefresh = idle
                                    .Select(x => (x.Key, x.Value))
                                    .ToList();

                                _lastIdleRefresh = DateTime.UtcNow;
                            }

                            foreach (var (Port, oldProc) in toRefresh)
                            {
                                var newProc = startPending(Port);

                                if (newProc == null)
                                    continue;

                                if (!AwaitRCCService(Port, 5000))
                                {
                                    KillbyID(newProc.Id);
                                    continue;
                                }

                                lock (PoolLock)
                                {
                                    if (idle.TryGetValue(Port, out var currentProc) &&
                                        currentProc.Id == oldProc.Id)
                                    {
                                        idle[Port] = newProc;
                                    }
                                }

                                KillbyID(oldProc.Id);
                            }
                        }
                        finally
                        {
                            _isRefreshingIdle = false;
                        }
                    }

                    int port;

                    lock (PoolLock)
                    {
                        if (howmuchRCCService() >= TargetPool)
                            break;

                        port = GetPort(27280, 30919);
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
            if (AwaitRCCService(port, 5000)) {
                alive = true;
                break;
            }
        }

        if (!alive)
        {
            if (Config.debug) Logger.Error($"Failed to connect to port {port}. This process cannot be used");
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return null;
        }

        try
        {
            string? tmp; string script;

            if (!Config.json)
            {
                script = "Instance.new('Part', workspace) game:GetService('RunService'):Run()";
                SOAP(Guid.NewGuid().ToString(), port, 0, script, 10, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx");
            }
            else
            {
                // again, we dont have a way to know if rccservices alive, so pretend that it is
            }
        }
        catch {}

        return proc;
    }

    private static (Process? proc, int port) startDedicatedRCCService()
    {
        int port = GetPort(27280, 30919);
        var proc = RCCService(port);
        if (proc == null) return (null, 0);

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, __) => RCCServiceExit(proc.Id, port);

        const int attempts = 20;
        bool alive = false;
        for (int i = 0; i < attempts; i++)
        {
            if (AwaitRCCService(port, 5000)) {
                alive = true;
                break;
            }
        }
        if (!alive) { Kill(proc); return (null, 0); }

        string script;

        if (!Config.json)
        {
            script = "Instance.new('Part', workspace) game:GetService('RunService'):Run()";
            try { string? tmp; SOAP(Guid.NewGuid().ToString(), port, 0, script, 2, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx"); } catch { } // we probably dont need to render if were just starting a gameserver.. just run physics
        } else
        {
            /*var payload = new
            {
                Mode = "Thumbnail",
                Settings = new
                {
                    Type = "Model",
                    PlaceId = 67,
                    UserId = 67,
                    BaseUrl = Config.BaseURL,
                    MatchmakingContextId = 1,
                    Arguments = new object[] { $"http://www.{Config.BaseURL}/asset/?id=67", "PNG", 420, 420, "http://www.{Config.BaseURL}" } // idk how to make it not guess that its www
                },
                Arguments = new
                {
                    MachineAddress = "127.0.0.1"
                }
            };

            script = JsonSerializer.Serialize(payload);*/
            script = ""; // we dont know how to warm up so
        }

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

                    if (!AwaitRCCService(port, 5000))
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
                    int port = GetPort(27280, 30919);
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

                Logger.RCCServiceInit("All jobs are busy. Spawning new RccService..");

                Monitor.Exit(PoolLock);

                int pport = GetPort(27280, 30919);
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
                    if (AwaitRCCService(pport, 5000)) {
                        alive = true;
                        break;
                    }
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
            foreach (var kv in idle)
            {
                Logger.Info($"Disposing idle process {kv.Value.Id} with port {kv.Key}");
                Kill(kv.Value);
            }
            foreach (var kv in pending)
            {
                Logger.Info($"Disposing pending process {kv.Value.Id} with port {kv.Key}");
                Kill(kv.Value);
            }
            foreach (var kv in active)
            {
                Logger.Info($"Disposing active process {kv.Value.Id} with port {kv.Key}");
                Kill(kv.Value);
            }

            idle.Clear();
            pending.Clear();
            active.Clear();
        }
    }

    private static DateTime _lastIdleRefresh = DateTime.UtcNow;
    private static bool _isRefreshingIdle;

    public static void runPoolManager()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {

                keepPoolsFull();

                bool ready;
                List<int> ports;

                lock (PoolLock)
                {
                    int usable = active.Count + idle.Count;

                    if (usable < TargetPool)
                    {
                        if (!Config.ForceReady)
                            Config.Ready = false;

                        goto Sleep;
                    }

                    ports = active.Keys.Concat(idle.Keys).ToList();
                }

                ready = ports.All(port => AwaitRCCService(port, 5000));

                lock (PoolLock)
                {
                    Config.Ready = ready;
                }

            Sleep:
                await Task.Delay(2000);
            }
        });
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

    public static bool IsTCPPortBindable(int port)
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

    public static int GetPort(int first = 50000, int second = 52999)
    {
        while (true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                Logger.RCCServiceInit($"Chosen random port {((IPEndPoint)listener.LocalEndpoint).Port} is already in use");
            }
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (port >= first && port <= second)
            {
                listener.Stop();
                if (Config.debug)
                    Logger.RCCServiceInit($"Port {port} is chosen for the next RccServiceProcess.");
                return port;
            }
        }
    }

    public static int GetGameServerPort(int first = 40000, int second = 59999)
    {
        while (true)
        {
            using var udp = new UdpClient(0);
            int port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            if (port >= first && port <= second)
            {
                udp.Dispose();
                if (Config.debug)
                    Logger.RCCServiceInit($"Port {port} is chosen for the next GameServer or Proxy.");
                return port;
            }
        }
    }

    public static bool Render(string jobId, long placeId, out string? render)
    {
        render = null;
        var (proc, SOAPPort, panic) = getRCCService();
        if (proc == null) return false;
        int pid = proc.Id;

        if (!SOAP(jobId, SOAPPort, placeId, Config.RScript, 120, 2, out render, jobtype: "BatchJobEx")) 
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

    public static bool ARender(string jobId, long placeId, out string? render, bool headshot, bool isclothing)
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

    public static bool MRender(string jobId, long placeId, out string? render)
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

    public static bool MMRender(string jobId, long placeId, out string? render)
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

    public static int StartGameserver(string jobId, long placeId, out string? render, bool teamcreate, out int fakeahport, out int pid)
    {
        render = null;
        int GameServerPort = GetGameServerPort(23640, 27279);
        int PublicPort = GameServerPort;

        ReverseProxy? proxy = null;

        if (Config.fakeahReverseProxy)
        {
            PublicPort = GetGameServerPort(20000, 23639);

            while (PublicPort == GameServerPort)
                PublicPort = GetGameServerPort(20000, 23639);

            proxy = new ReverseProxy(PublicPort, GameServerPort);
            proxy.Start();

            if (Config.debug)
                Logger.Info($"Started a reverse proxy for gameserver: {PublicPort} to {GameServerPort}");
        }

        fakeahport = PublicPort;
        pid = 0;
        bool panic = false;

        Process? proc;
        int SOAPPort;

        lock (PoolLock)
        {
            int dedicatedCount = dedicated.Count;

            if (dedicatedCount >= MaxDedicated)
            {
                Logger.RCCServiceInit($"{dedicatedCount} dedicated RccService processes are active, using Pooled now");

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
                if (!Config.poolgs) // last one fucking second in my private lobby pal..
                {
                    (proc, SOAPPort) = startDedicatedRCCService();
                }
                else
                {
                    (proc, SOAPPort, panic) = getRCCService();
                }
            }
        }

        if (proc == null) return 0;

        pid = proc.Id;
        int fakeahtimeout = Config.legacy ? 604800 : 30;
        if (!SOAP(jobId, SOAPPort, placeId, Config.GSScript, fakeahtimeout, 1, out render, teamcreate, fakeahport: GameServerPort, jobtype: "OpenJobEx"))
        {
            lock (PoolLock)
            {
                dedicated.Remove(SOAPPort);
                active.Remove(SOAPPort);
                idle.Remove(SOAPPort);
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
                Port = fakeahport, // oh my god bruh
                SOAP = SOAPPort,
                ExpiresAt = DateTime.UtcNow.AddSeconds(fakeahtimeout),
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
                Arguments = // god help me
                    Config.json
                        ? (Config.debug
                            ? (win
                                ? $"-verbose -settingsfile \"DevSettingsFile.json\" -Console -port {port}"
                                : $"\"{exe}\" -verbose -settingsfile \"DevSettingsFile.json\" -Console -port {port}")
                            : (win
                                ? $"-Console -port {port}"
                                : $"\"{exe}\" -Console -port {port}"))
                        : (win
                            ? $"/Console /content:content\\\\ {port}"
                            : $"\"{exe}\" /Console /content:content\\\\ {port}"),
                WorkingDirectory = Config.RCCDirectory,
                UseShellExecute = (Config.legacy) ? false : true,
                CreateNoWindow = (Config.legacy) ? false : true,
                WindowStyle = ProcessWindowStyle.Minimized
            };

            var proc = Process.Start(psi);
            if (proc == null) return null;

            proc.EnableRaisingEvents = true;
            proc.Exited += (_, __) => RCCServiceExit(proc.Id, port);

            if (win)
                proc.PriorityClass = Config.realtime ? ProcessPriorityClass.RealTime : ProcessPriorityClass.High;

            bool ready = false;

            for (int i = 0; i < 5; i++)
            {
                if (AwaitRCCService(port, 5000))
                {
                    ready = true;
                    break;
                }

                Thread.Sleep(200);
            }

            if (!ready)
            {
                Logger.Error($"Failed to connect to port {port}.");
            }

            try
            {
                try
                {
                    string? tmp;
                    string script;

                    if (!Config.json)
                    {
                        script = "return true";
                        SOAP(Guid.NewGuid().ToString(), port, 0, script, 5, 0, out tmp, enforceSigning: false, jobtype: "BatchJobEx");
                    }
                    else
                    {
                        // we dont have a way to know if rccservice's alive other than HelloWorld, which im not making a different function for it, so, for now, just pretend that RCCService is alive.
                    }
                }
                catch { }

                Logger.RCCServiceInit($"Started RccService process. Process ID = {proc.Id}, Port = {port}");
            }
            catch { }

            return proc;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to connect to port {port}. This process cannot be used: {ex}");
            return null;
        }
    }

    private static bool ControlC(Process proc, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (proc.HasExited)
            return true;

        if (!AttachConsole((uint)proc.Id))
            return false;

        try
        {
            SetConsoleCtrlHandler(null, true);

            if (!GenerateConsoleCtrlEvent(CtrlCEvent, 0))
                return false;

            return proc.WaitForExit((int)timeout.TotalMilliseconds);
        }
        finally
        {
            SetConsoleCtrlHandler(null, false);
            FreeConsole();
        }
    }

    private static void Kill(Process proc)
    {

        try
        {
            if (proc == null || proc.HasExited)
                return;

            if (ControlC(proc, TimeSpan.FromSeconds(5)))
                return;

            proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error disposing process {proc.Id}: {ex}");
        }
    }

    public static bool KillbyID(long pid)
    {
        if (pid < int.MinValue || pid > int.MaxValue)
            return false;

        try
        {
            var proc = Process.GetProcessById((int)pid);

            if (!proc.ProcessName.Contains(Config.name, StringComparison.OrdinalIgnoreCase))
                return false;

            Kill(proc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool AwaitRCCService(int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var resp = client.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                if (resp.Content.Headers.ContentType?.MediaType == "text/xml")
                    return true;
            }
            catch
            {
            }

            Thread.Sleep(200);
        }

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

    private static string ProcessConditionals(string template, Dictionary<string, object> variables)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        var stack = new Stack<ConditionalFrame>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (TryParseConditionalStart(line, "if", out var ifCondition))
            {
                bool parentActive = stack.Count == 0 || stack.All(f => f.CurrentActive);
                bool conditionResult = parentActive && EvaluateCondition(ifCondition, variables);

                stack.Push(new ConditionalFrame
                {
                    ParentActive = parentActive,
                    BranchMatched = conditionResult,
                    CurrentActive = conditionResult
                });

                continue;
            }

            if (TryParseConditionalStart(line, "elseif", out var elseifCondition))
            {
                if (stack.Count == 0)
                    throw new Exception("elseif without if");

                var frame = stack.Peek();

                if (frame.SeenElse)
                    throw new Exception("elseif after else");

                if (frame.BranchMatched)
                {
                    frame.CurrentActive = false;
                }
                else
                {
                    bool conditionResult = frame.ParentActive && EvaluateCondition(elseifCondition, variables);
                    frame.CurrentActive = conditionResult;
                    frame.BranchMatched |= conditionResult;
                }

                continue;
            }

            if (line == "else")
            {
                if (stack.Count == 0)
                    throw new Exception("else without if");

                var frame = stack.Peek();

                if (frame.SeenElse)
                    throw new Exception("multiple else blocks");

                frame.SeenElse = true;
                frame.CurrentActive = frame.ParentActive && !frame.BranchMatched;
                frame.BranchMatched = true;

                continue;
            }

            if (line == "end")
            {
                if (stack.Count == 0)
                    throw new Exception("end without if");

                stack.Pop();
                continue;
            }

            bool shouldEmit = stack.Count == 0 || stack.All(f => f.CurrentActive);
            if (shouldEmit)
                output.AppendLine(rawLine);
        }

        if (stack.Count != 0)
            throw new Exception("Unclosed if block(s)");

        return output.ToString();
    }

    private static bool TryParseConditionalStart(string line, string keyword, out string condition)
    {
        condition = null!;

        if (!line.StartsWith(keyword + " ", StringComparison.Ordinal) || !line.EndsWith(" then", StringComparison.Ordinal))
            return false;

        condition = line.Substring(keyword.Length + 1, line.Length - (keyword.Length + 1) - 5).Trim();
        return true;
    }

    private static bool EvaluateCondition(string condition, IReadOnlyDictionary<string, object?> variables) {
        var match = Regex.Match(condition, @"^\{\%\{(.+?)\}\}\s*==\s*(.+)$");

        if (!match.Success)
            throw new Exception($"Bad condition: {condition}");

        string variableName = match.Groups[1].Value;
        string expectedRaw = match.Groups[2].Value.Trim();

        if (!variables.TryGetValue(variableName, out var actual))
            return false;

        if (actual is bool b &&
            bool.TryParse(expectedRaw, out var expectedBool))
        {
            return b == expectedBool;
        }

        return string.Equals(Convert.ToString(actual), expectedRaw, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SOAP(string jobId, int port, long placeId, string type, int howlonguntilwedie, int category, out string? render, bool teamcreate = false, int fakeahport = 53640, bool headshot = false, bool isclothing = false, List<LuaValue>? arguments = null, bool enforceSigning = true, string jobtype = "OpenJobEx")
    {
        render = null;

        Config.ReloadScripts();
        if (Config.signing && enforceSigning)
        {
            string script = type.Trim();
            string[] lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (lines.Length == 0)
            {
                Logger.RCCServiceJobs("Script is empty.");
                return false;
            }

            string signatureLine = lines[0].Trim();
            string scriptContent = string.Join("\n", lines.Skip(1)).Trim();

            bool valid = Config.Verification(scriptContent, signatureLine);

            if (!valid)
            {
                Logger.RCCServiceJobs($"Script verification failed, please check your script signatures and try again (signature: {signatureLine})");
                return false;
            }
            else
            {
                if (Config.debug)
                    Logger.RCCServiceJobs("Signature is valid");
            }

            type = scriptContent;
        }
        try
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["placeId"] = placeId,
                ["jobId"] = jobId,
                ["port"] = fakeahport,
                ["accesskey"] = Config.AccessKey,
                ["teamcreate"] = teamcreate,
                ["isheadshot"] = headshot,
                ["isclothing"] = isclothing
            };

            type = ProcessConditionals(type, variables);
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
            client.DefaultRequestHeaders.ExpectContinue = false;

            using var resp = client.SendAsync(req, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult();
            var responseText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"An unexpected error was occurred in RccService:\n" + responseText);
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
                    Logger.RCCServiceJobs("Render value wasn't found! ANRSAL doesn't support ASYNC renders yet.");
                    Logger.RCCServiceJobs("RccService's response: " + responseText);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception($"An unexpected error was occurred:\n" + ex);
        }
    }

    public static void RemoveJob(string jobId)
    {
        lock (JobsLock)
        {
            Jobs.Remove(jobId);
        }
    }

    public static GSMJob? GetJobByPID(long pid)
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

        if (!SOAPRenewLease(job.SOAP, job.JobId, seconds))
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
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            var soap = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:rob=""http://{Config.BaseURL}/"">
  <soapenv:Body>
    <rob:RenewLease>
      <rob:jobID>{jobId}</rob:jobID>
      <rob:expirationInSeconds>{seconds}</rob:expirationInSeconds>
    </rob:RenewLease>
  </soapenv:Body>
</soapenv:Envelope>";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/");
            req.Version = HttpVersion.Version11;
            req.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            req.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(soap));
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
            req.Headers.Add("SOAPAction", "RenewLease");
            req.Headers.Host = $"127.0.0.1:{port}";
            req.Headers.ConnectionClose = true;
            client.DefaultRequestHeaders.ExpectContinue = false;

            using var resp = client.Send(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            throw new Exception($"An unexpected error was occurred:\n" + ex);
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

    private const uint CtrlCEvent = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);
}