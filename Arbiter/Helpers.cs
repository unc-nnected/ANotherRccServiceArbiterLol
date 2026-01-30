using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

static class Helpers
{
    private const string SECRET = "my-mother-ate-fries-lol";

    public static bool SysStats()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (!exe.Contains("Arbiter", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!IsPortBindable(7000))
                return false;

            var current = Process.GetCurrentProcess();
            var same = Process.GetProcessesByName(current.ProcessName);
            if (same.Length > 1)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    static string? ExtractXML(string soapResponse)
    {
        var doc = XDocument.Parse(soapResponse);

        var valueElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "value");

        return valueElement?.Value.Trim();
    }


    private static bool IsPortBindable(int port)
    {
        try
        {
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
        if (!header.StartsWith("Bearer ")) return false;

        string token = header["Bearer ".Length..];
        using var sha = SHA256.Create();

        var expected = Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(SECRET))
        );

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expected)
        );
    }

    public static int GetPort()
    {
        var r = new Random();
        int port;
        do port = r.Next(20000, 60000);
        while (port == 3306);
        return port;
    }

    public static bool Render(string jobId, int port, int placeId, out int pid, out string? render)
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

        if (!SOAP(jobId, port, placeId, Config.RScript, 1, 2, out render))
        {
            Kill(proc);
            return false;
        }

        Logger.Info($"{jobId} started (pid={pid})");
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

        if (!SOAP(jobId, port, placeId, Config.RAScript, 1, 2, out render))
        {
            Kill(proc);
            return false;
        }

        Logger.Info($"{jobId} started (pid={pid})");
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

        if (!SOAP(jobId, port, placeId, Config.GSScript, 604800, 1, out render))
        {
            Logger.Info($"{jobId} SOAP action failed");
            Kill(proc);
            return false;
        }

        Logger.Info($"{jobId} started (pid={pid})");
        return true;
    }

    private static Process? RCCService(int port)
    {
        try
        {
            string RCCService = Path.Combine(Config.RCCDirectory, "RCCService.exe");

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            ProcessStartInfo psi;

            Logger.Info($"Using {Config.RCCDirectory} for RCCService with port {port}");

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

                Logger.Info($"RCCServuce starting");
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

                Logger.Info($"RCCServuce starting via wine");
            }

            return Process.Start(psi);
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
            Timeout = TimeSpan.FromSeconds(5)
        };

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var resp = client.GetAsync($"http://127.0.0.1:{port}").Result;

                if (resp.Content.Headers.ContentType?.MediaType == "text/xml")
                {
                    Logger.Info("RCCService alive, continuing");
                    return true;
                }
            }
            catch {}

            Thread.Sleep(250);
        }

        Logger.Error("Timed out waiting for RCCService");
        return false;
    }


    private static bool SOAP(string jobId, int port, int placeId, string type, int howlonguntilwedie, int category, out string? render)
    {
        render = null;
        try
        {
            using var soapClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            soapClient.DefaultRequestHeaders.Host = $"127.0.0.1:{port}";

            string soap = $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:rob=""http://{Config.BaseURL}/"">
	<soapenv:Body>
        <rob:OpenJob>
            <rob:job>
                <rob:id>{jobId}</rob:id>
                <rob:expirationInSeconds>999999999</rob:expirationInSeconds>
                <rob:cores>{Config.cores}</rob:cores>
            </rob:job>
            <rob:script>
                <rob:name>GameScript</rob:name>
                <rob:script><![CDATA[
                    {type}
                ]]></rob:script>
            </rob:script>
        </rob:OpenJob>
    </soapenv:Body>
</soapenv:Envelope>
";

            /*var content = new StringContent(soap, Encoding.UTF8, "text/xml");
            content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            content.Headers.Add("SOAPAction", "OpenJob");
            var resp = soapClient.PostAsync($"http://127.0.0.1:{port}", content).GetAwaiter().GetResult();*/
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}");
            req.Content = new StringContent(soap, Encoding.UTF8, "text/xml");
            req.Headers.Accept.ParseAdd("text/xml");
            req.Headers.Add("SOAPAction", "OpenJob");
            var resp = soapClient.SendAsync(req).GetAwaiter().GetResult();

            if (category == 2) // category 2 is for renders
            {
                string response = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                Thread.Sleep(2000); // delay because roblox rccservice is a fucking chud

                render = ExtractXML(response);

                if (render == null)
                    Logger.Error("RCCService failed to render (missing Value)");
            }

            string responseText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Error($"{jobId} errored ({(int)resp.StatusCode} {resp.StatusCode})\n" + responseText);
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Error($"SOAP error: {ex.Message}");
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
                Logger.Warn($"Refusing to kill unrelated process pid={pid}");
                return false;
            }

            Logger.Warn($"Stopping RCCService pid={pid}");
            proc.Kill(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

}