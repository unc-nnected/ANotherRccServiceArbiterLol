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
        string arbiter = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName);
        string arbiterDLL = Path.GetFileNameWithoutExtension(arbiter) + ".dll";
        var required = new[] { arbiter, arbiterDLL };
        if (string.IsNullOrEmpty(arbiter))
        {
            throw new InvalidOperationException("An unexpected error occurred while updating Arbiter. 0x01");
        }

        string name = Path.GetFileName(arbiter);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 ANRSAL");
        var json = await client.GetStringAsync("https://www.couscs.com/ANRSAL/version.json");
        if (!json.TrimStart().StartsWith("{"))
        {
            throw new InvalidOperationException("An unexpected error occurred while updating Arbiter. 0x02");
        }
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        if (manifest == null) return;

        string latestVersion = manifest["version"];
        string currentVersion = Process.GetCurrentProcess().MainModule.FileVersionInfo.FileVersion ?? "unknown";

        if (latestVersion == currentVersion) return;

        Logger.Warn("Getting the latest Arbiter...");

        string temp = Path.Combine(Path.GetTempPath(), name + ".tmp");
        foreach (var file in required)
        {
            using var response = await client.GetAsync($"https://www.couscs.com/ANRSAL/{file}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            var lastReported = DateTime.MinValue;
            var sw = Stopwatch.StartNew();
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
                        double speed = bytesSinceLast / 1024.0 / 1024.0 / 0.5;
                        bytesSinceLast = 0;

                        double remainingSec = ((totalBytes - totalRead) / 1024.0 / 1024.0) / speed;

                        int barLength = 50;
                        int filled = (int)(percent / 2);
                        string bar = new string('█', filled) + new string('░', barLength - filled);

                        Console.Write($"\r[{bar}] {percent:0.0}% {MBDownloaded:0.00}/{MBTotal:0.00}MB | {speed:0.00} MB/s | ETA: {TimeSpan.FromSeconds(remainingSec):mm\\:ss} | {file}");
                    }
                    else
                    {
                        Console.Write($"\rDownloaded {totalRead / 1024.0 / 1024.0:0.00} MB");
                    }
                }
            }
        }

        foreach (var file in required)
        {
            string tempnumbatwo = Path.Combine(Path.GetTempPath(), file + ".tmp");
            string target = Path.Combine(AppContext.BaseDirectory, file);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout 1 & move /Y \"{tempnumbatwo}\" \"{target}\" & start \"\" \"{Path.Combine(AppContext.BaseDirectory, required[0])}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        Environment.Exit(0);
    }
}