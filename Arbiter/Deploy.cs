using System.Text.Json;
using System.Diagnostics;

public static class Deploy
{
    public static async Task Start()
    {
        try
        {
            await GetDeploy();
        }
        catch (Exception ex)
        {
            Logger.Error($"{ex.Message}");
        }
    }

    private static async Task GetDeploy()
    {
        string arbiterExe = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName);
        string arbiterDll = Path.GetFileNameWithoutExtension(arbiterExe) + ".dll";
        var required = new[] { arbiterExe, arbiterDll };

        if (string.IsNullOrEmpty(arbiterExe))
            throw new InvalidOperationException("An unexpected error occurred while updating Arbiter. 0x01");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 ANRSAL");

        var json = await client.GetStringAsync("https://www.couscs.com/ANRSAL/version.json");
        if (!json.TrimStart().StartsWith("{"))
            throw new InvalidOperationException("An unexpected error occurred while updating Arbiter. 0x02");

        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (manifest == null) return;

        string latestVersion = manifest["version"];
        string currentVersion = Process.GetCurrentProcess().MainModule.FileVersionInfo.FileVersion ?? "unknown";

        if (latestVersion == currentVersion) return;

        Logger.Warn("Getting the latest Arbiter...");

        var temp = new Dictionary<string, string>();
        foreach (var file in required)
        {
            string tmp = Path.Combine(Path.GetTempPath(), file + ".tmp");
            temp[file] = tmp;

            using var response = await client.GetAsync($"https://www.couscs.com/ANRSAL/{file}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            bool canReportProgress = totalBytes != -1;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            var lastReported = DateTime.MinValue;
            long bytesSinceLast = 0;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                bytesSinceLast += read;

                if ((DateTime.Now - lastReported).TotalMilliseconds > 500 || totalRead == totalBytes)
                {
                    lastReported = DateTime.Now;

                    if (canReportProgress)
                    {
                        double percent = (double)totalRead / totalBytes * 100;
                        double MBDownloaded = totalRead / 1024.0 / 1024.0;
                        double MBTotal = totalBytes / 1024.0 / 1024.0;
                        double speed = bytesSinceLast / 1024.0 / 1024.0 / 0.5; // MB
                        bytesSinceLast = 0;

                        double remainingSec = ((totalBytes - totalRead) / 1024.0 / 1024.0) / speed;

                        int barLength = 50;
                        int filled = (int)(percent / 2);
                        string bar = new string('█', filled) + new string('░', barLength - filled);

                        Console.Write($"\r[{bar}] {percent:0.0}% {MBDownloaded:0.00}/{MBTotal:0.00}MB | {speed:0.00} MB/s | ETA: {TimeSpan.FromSeconds(remainingSec):mm\\:ss} | {file}");
                    }
                    else
                    {
                        Console.Write($"\rDownloaded {totalRead / 1024.0 / 1024.0:0.00} MB | {file}");
                    }
                }
            }

            Console.WriteLine();
        }

        foreach (var kvp in temp)
        {
            string target = Path.Combine(AppContext.BaseDirectory, kvp.Key);
            string tempnumbatwo = Path.Combine(Path.GetTempPath(), kvp.Key + ".tmp");

            string wait = kvp.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? kvp.Key : required[0];

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C " +
                    $":loop & " +
                    $"tasklist | find \"{wait}\" >nul && timeout /t 1 >nul && goto loop & " +
                    // im killing myself
                    $"move /Y \"{tempnumbatwo}\" \"{target}\"" + (kvp.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? $" & start \"\" \"{target}\"" : ""),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        Logger.Warn("Restarting ANRSAL...");

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, required[0]),
            UseShellExecute = true
        });

        Environment.Exit(0);
    }
}