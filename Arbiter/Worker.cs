using Microsoft.Extensions.Hosting;
using System.Text.Json;

public class Worker : BackgroundService
{
    private readonly HttpClient _http = new HttpClient();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Config.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());

        if (Config.service)
        {
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
            }
            catch { }
        }

        Config.ReloadScripts();
    }
}