using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
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

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}