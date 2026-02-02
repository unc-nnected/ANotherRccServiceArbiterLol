using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

static class Helpers
{

    public static bool SysStats()
    {
        try
        {
            // check if the file path contains Arbiter, else return false
            string exe = Environment.ProcessPath ?? "";
            if (!exe.Contains("Arbiter", StringComparison.OrdinalIgnoreCase))
                return false;

            // check if port is in use
            if (!IsPortBindable(Config.port))
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


    private static bool IsPortBindable(int port)
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
        var r = new Random();
        int port = r.Next(64000, 64989);
        return port;
    }

    public static int GetGameServerPort()
    {
        var r = new Random();
        int port = r.Next(60000, 64000);
        return port;
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

        if (!SOAP(jobId, port, placeId, Config.RScript, 60, 2, out render))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static bool ARender(string jobId, int port, int placeId, out int pid, out string? render)
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

        if (!SOAP(jobId, port, placeId, Config.RAScript, 60, 2, out render))
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

        if (!SOAP(jobId, port, placeId, Config.RMScript, 60, 2, out render))
        {
            Kill(proc);
            return false;
        }
        return true;
    }

    public static bool StartGameserver(string jobId, int port, int placeId, out int pid, out string? render)
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

        if (!SOAP(jobId, port, placeId, Config.GSScript, 604800, 1, out render))
        {
            Logger.Info($"{jobId} SOAP action failed");
            Kill(proc);
            return false;
        }

        return true;
    }

    private static Process? RCCService(int port)
    {
        try
        {
            // we get RCCService.exe from the custom path
            string RCCService = Path.Combine(Config.RCCDirectory, "RCCService.exe");

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            ProcessStartInfo psi;

            if (Config.debug)
            {
                Logger.Info($"Using {Config.RCCDirectory} for RCCService with port {port}");
            }

            if (isWindows)
            {
                // windows (:
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
                    Logger.Info($"RCCServuce starting");
                }
            }
            else
            {
                // linux ):
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
                    Logger.Info($"RCCServuce starting via wine");
                }
            }

            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            // What
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


    private static bool SOAP(string jobId, int port, int placeId, string type, int howlonguntilwedie, int category, out string? render) {
        render = null;
        Thread.Sleep(250);
        try
        {
            using var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            if (Config.debug)
            {
                Logger.Warn($"Using {placeId} ID for SOAP");
            }

            client.DefaultRequestHeaders.Host = $"127.0.0.1:{port}";
            type = type.Replace("{placeId}", placeId.ToString());
            var soap = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""
                  xmlns:rob=""http://{Config.BaseURL}/"">
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
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}");
            req.Content = new StringContent(soap, Encoding.UTF8, "text/xml"); // this as well
            req.Headers.Add("SOAPAction", "OpenJob"); // important because rccservice wouldnt recongize this

            if (Config.debug)
            {
                Logger.Warn(type);
            }

            using var resp = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult(); // POST! and then wait for result

            using var stream = resp.Content.ReadAsStream(); // start reading
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var responseText = reader.ReadToEnd(); // done (:

            if (string.IsNullOrWhiteSpace(responseText))
                return resp.IsSuccessStatusCode;

            if (category == 2)
            {
                try
                {
                    var doc = XDocument.Parse(responseText);

                    // boo
                    if (doc.Descendants().Any(e => e.Name.LocalName == "faultsring"))
                    {
                        Logger.Error("RCCService errored:\n" + responseText);
                        return false;
                    }

                    var value = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "value");

                    if (value == null || string.IsNullOrWhiteSpace(value.Value))
                    {
                        Logger.Error(responseText);
                        return false;
                    }

                    render = value.Value.Trim();
                }
                catch (Exception ex)
                {
                    //C# ohio
                    Logger.Error("Couldn't parse XML: " + ex);
                    return false;
                }
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // WHAT JUST HAPPEND BRO
            Logger.Error("SOAP error: " + ex);
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

}