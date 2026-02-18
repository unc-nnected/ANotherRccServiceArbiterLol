using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

static class Helpers
{
    private static readonly Dictionary<string, GSMJob> Jobs = new();
    private static readonly object JobsLock = new();
    private static bool _gsmStarted;
    private static readonly Random _random = new();

    public static bool SysStats()
    {
        try
        {
            // check if the file path contains Arbiter, else return false
            string exe = Environment.ProcessPath ?? "";
            if (!exe.Contains("Arbiter", StringComparison.OrdinalIgnoreCase))
                return false;

            // check if port is in use
            if (!IsTCPPortBindable(Config.port))
                return false;

            // check if there's already an arbiter running
            var current = Process.GetCurrentProcess();
            var same = Process.GetProcessesByName(current.ProcessName);
            if (same.Length > 1)
                return false;

            return true;
        }
        catch
        {
            // idfk what happend but return false just in case
            return false;
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


    private static bool IsTCPPortBindable(int port)
    {
        try
        {
            // create a listener instantly and then stop
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAuthorized(string header)
    {
        // check if Authorization: header has Bearer and the SECRET that we have but encoded to SHA256
        if (!header.StartsWith("Bearer ")) return false;

        string token = header["Bearer ".Length..];
        using var sha = SHA256.Create();

        var expected = Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(Config.SECRET))
        );

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expected)
        );
    }

    public static int GetPort()
    {
        while (true)
        {
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                if (port >= 60000 && port <= 64989)
                {
                    listener.Stop();
                    return port;
                }
            }
        }
    }

    public static int GetGameServerPort()
    {
        while (true)
        {
            using (var udp = new UdpClient(0))
            {
                int port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;

                if (port >= 40000 && port <= 59999)
                {
                    udp.Dispose();
                    return port;
                }
            }
        }
    }

    public static bool Render(string jobId, int port, int placeId, out int pid, out string? render)
    {
        pid = -1;
        render = null;
        // start rccservice
        var proc = RCCService(port);
        if (proc == null)
            return false;

        pid = proc.Id;
        //check if rccservice is online
        if (!AwaitRCCService(port, timeoutMs: 8000))
        {
            // lmfao
            Kill(proc);
            return false;
        }

        if (Config.debug)
        {
            Logger.Info($"{jobId} started (pid={pid})");
        }

        if (!SOAP(jobId, port, placeId, Config.RScript, 60, 2, out render, false))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static bool ARender(string jobId, int port, int placeId, out int pid, out string? render, bool headshot, bool isclothing)
    {
        pid = -1;
        render = null;

        var proc = RCCService(port);
        if (proc == null)
            return false;

        pid = proc.Id;

        if (!AwaitRCCService(port, timeoutMs: 8000))
        {
            Kill(proc);
            return false;
        }

        if (Config.debug)
        {
            Logger.Info($"{jobId} started (pid={pid})");
        }

        if (!SOAP(jobId, port, placeId, Config.RAScript, 60, 2, out render, false, 53640, headshot, isclothing))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static bool MRender(string jobId, int port, int placeId, out int pid, out string? render)
    {
        pid = -1;
        render = null;

        var proc = RCCService(port);
        if (proc == null)
            return false;

        pid = proc.Id;

        if (!AwaitRCCService(port, timeoutMs: 8000))
        {
            Kill(proc);
            return false;
        }

        if (Config.debug)
        {
            Logger.Info($"{jobId} started (pid={pid})");
        }

        if (!SOAP(jobId, port, placeId, Config.RMScript, 60, 2, out render, false))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static bool MMRender(string jobId, int port, int placeId, out int pid, out string? render)
    {
        pid = -1;
        render = null;

        var proc = RCCService(port);
        if (proc == null)
            return false;

        pid = proc.Id;

        if (!AwaitRCCService(port, timeoutMs: 8000))
        {
            Kill(proc);
            return false;
        }

        if (Config.debug)
        {
            Logger.Info($"{jobId} started (pid={pid})");
        }

        if (!SOAP(jobId, port, placeId, Config.RMMScript, 60, 2, out render, false))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static int StartGameserver(string jobId, int port, int placeId, out int pid, out string? render, bool teamcreate, out int fakeahport)
    {
        pid = -1;
        render = null;
        fakeahport = 0; // w fake ah port

        var proc = RCCService(port);
        if (proc == null)
            return 0;

        pid = proc.Id;

        if (!AwaitRCCService(port, timeoutMs: 8000))
        {
            Kill(proc);
            return 0;
        }

        if (Config.debug)
        {
            Logger.Info($"{jobId} started (pid={pid})");
        }

        fakeahport = GetGameServerPort();

        if (!SOAP(jobId, port, placeId, Config.GSScript, 604800, 1, out render, teamcreate, fakeahport))
        {
            Logger.Info($"{jobId} SOAP action failed");
            Kill(proc);
            return 0;
        }

        lock (JobsLock)
        {
            Jobs[jobId] = new GSMJob
            {
                JobId = jobId,
                Port = port,
                PlaceId = placeId,
                Pid = pid,
                ExpiresAt = DateTime.UtcNow.AddSeconds(604800),
                LastHeartbeat = DateTime.UtcNow,
                Players = 0,
                Alive = true
            };
        }

        return fakeahport;
    }

    private static Process? RCCService(int port)
    {
        try
        {
            if (Config.debug)
            {
                Logger.Info("Reloading Configuration");
            }
            Config.ReloadScripts();

            string RCCService = Path.Combine(Config.RCCDirectory, "RCCService.exe");
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            ProcessStartInfo psi;

            if (Config.debug)
            {
                Logger.Info($"Using {Config.RCCDirectory} for RCCService with port {port}");
            }

            if (isWindows)
            {
                psi = new ProcessStartInfo
                {
                    FileName = RCCService,
                    Arguments = $"-console -port {port}",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Config.RCCDirectory
                };

                if (Config.debug)
                {
                    Logger.Info("RCCServuce starting");
                }
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "wine",
                    Arguments = $"\"{RCCService}\" -console -port {port}",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Config.RCCDirectory
                };

                if (Config.debug)
                {
                    Logger.Info("RCCServuce starting via Wine");
                }
            }

            Process? proc = Process.Start(psi);

            if (proc != null)
            {
                if (isWindows)
                {
                    proc.PriorityClass = ProcessPriorityClass.High;
                }
                else
                {
                    try
                    {
                        using (var process = new Process())
                        {
                            process.StartInfo.FileName = "renice";
                            process.StartInfo.Arguments = $"-n -5 -p {proc.Id}";
                            process.StartInfo.UseShellExecute = false;
                            process.Start();
                            process.WaitForExit();
                        }
                    }
                    catch
                    {
                        if (Config.debug) Logger.Warn("Couldn't make RCCService higher priority. (SUDO needed?)");
                    }
                }

                if (Config.debug)
                {
                    Logger.Info("RCCService started");
                }
            }

            return proc;
        }
        catch (Exception ex)
        {
            Logger.Error("RCCService couldn't start: " + ex.Message);
            return null;
        }
    }

    private static bool AwaitRCCService(int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30) // better safe than sorry
        };

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                // GET rccservice because it behaves like a webserver anyways, it errors on GET safely so RCCService can tell us it's alive
                var resp = client.GetAsync($"http://127.0.0.1:{port}").Result;

                if (resp.Content.Headers.ContentType?.MediaType == "text/xml")
                {
                    // yup alive
                    if (Config.debug)
                    {
                        Logger.Info("RCCService alive, continuing");
                    }
                    return true;
                }
            }
            catch {}

            Thread.Sleep(250);
        }
        // RCCService fucking DEAD
        Logger.Error("Timed out waiting for RCCService");
        return false;
    }
    private static bool SOAP(string jobId, int port, int placeId, string type, int howlonguntilwedie, int category, out string? render, bool teamcreate = false, int fakeahport = 53640, bool headshot = false, bool isclothing = false)
    {
        render = null;

        try
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false
            };

            using var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            type = type.Replace("{placeId}", placeId.ToString());
            type = type.Replace("{jobId}", jobId);
            type = type.Replace("{port}", fakeahport.ToString());
            type = type.Replace("{accesskey}", Config.AccessKey);
            type = type.Replace("{teamcreate}", teamcreate.ToString().ToLower());
            type = type.Replace("{isheadshot}", headshot.ToString().ToLower());
            type = type.Replace("{isclothing}", isclothing.ToString().ToLower());

            var soap = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:rob=""http://{Config.BaseURL}/"">
<soapenv:Body>
  <rob:OpenJob>
    <rob:job>
      <rob:id>{jobId}</rob:id>
      <rob:expirationInSeconds>{howlonguntilwedie}</rob:expirationInSeconds>
      <rob:cores>{Config.cores}</rob:cores>
    </rob:job>
    <rob:script>
      <rob:name>{jobId}-Script</rob:name>
      <rob:script><![CDATA[
{type}
      ]]></rob:script>
    </rob:script>
  </rob:OpenJob>
</soapenv:Body>
</soapenv:Envelope>";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/");
            req.Version = HttpVersion.Version11;
            req.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

            var bytes = Encoding.UTF8.GetBytes(soap);
            req.Content = new ByteArrayContent(bytes);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

            req.Headers.Add("SOAPAction", "OpenJob");
            req.Headers.Host = $"127.0.0.1:{port}";
            req.Headers.ConnectionClose = true;
            client.DefaultRequestHeaders.ExpectContinue = false;
            using var resp = client.SendAsync(req, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult();
            var responseText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (Config.debug)
            {
                Logger.Warn($"SOAP status: {(int)resp.StatusCode}");
                Logger.Warn("SOAP response:\n" + responseText);
            }

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Error("RCCService returned error:\n" + responseText);
                return false;
            }

            if (category == 2)
            {
                var doc = XDocument.Parse(responseText);

                if (doc.Descendants().Any(e => e.Name.LocalName == "Fault"))
                {
                    Logger.Error("SOAP Fault:\n" + responseText);
                    return false;
                }

                var value = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "value");
                if (value == null || string.IsNullOrWhiteSpace(value.Value))
                    return false;

                render = value.Value.Trim();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SOAP exception:\n" + ex);
            return false;
        }
    }

    private static void Kill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(true);
        }
        catch { }
    }

    public static bool KillbyID(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);

            if (!proc.ProcessName.Contains("RCCService", StringComparison.OrdinalIgnoreCase))
            {
                // lmfaoooooooo dumbass tried to kill non rccservice
                if (Config.debug)
                {
                    Logger.Warn($"Refusing to kill unrelated process pid={pid}");
                }
                return false;
            }
            if (Config.debug)
            {
                Logger.Warn($"Stopping RCCService pid={pid}");
            }
            proc.Kill(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SOAPRenewLease(int port, string jobId, int seconds)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

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

            if (!resp.IsSuccessStatusCode)
                return false;

            var body = resp.Content.ReadAsStringAsync().Result;

            if (string.IsNullOrWhiteSpace(body))
                return true;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"SOAP RenewLease failed: {ex.Message}");
            return false;
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


    public static bool UpdatePresence(string jobId, bool joining)
    {
        lock (JobsLock)
        {
            if (!Jobs.TryGetValue(jobId, out var job))
                return false;

            job.Players += joining ? 1 : -1;
            if (job.Players < 0) job.Players = 0;

            job.LastHeartbeat = DateTime.UtcNow;
            return true;
        }
    }

    public static List<object> GetAllJobs(int? Port = null)
    {
        lock (JobsLock)
        {
            return Jobs.Values  // this is better
                .Where(j => j.Alive && (Port == null || j.Port == Port))
                .Select(j => new
                {
                    j.JobId,
                    j.PlaceId,
                    j.Players,
                    j.Port,
                    expiresAt = j.ExpiresAt
                })
                .Cast<object>()
                .ToList();
        }
    }

    private static bool AwaitRCCServiceButUsePIDInsteadBecausePortFuckingSucksLmao(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static GSMJob? GetJob(string jobId)
    {
        lock (JobsLock)
        {
            if (!Jobs.TryGetValue(jobId, out var job))
                return null;

            if (DateTime.UtcNow > job.ExpiresAt)
            {
                job.Alive = false;
                Jobs.Remove(jobId);
                return null;
            }

            if (!AwaitRCCServiceButUsePIDInsteadBecausePortFuckingSucksLmao(job.Pid))
            {
                job.Alive = false;
                Jobs.Remove(jobId);
                return null;
            }

            job.Alive = true;
            return job;
        }
    }

    public static GSMJob? GetJobByPID(int pid)
    {
        lock (JobsLock)
        {
            return Jobs.Values.FirstOrDefault(j => j.Pid == pid);
        }
    }

    public static void RemoveJob(string jobId)
    {
        lock (JobsLock)
        {
            Jobs.Remove(jobId);
        }
    }

}