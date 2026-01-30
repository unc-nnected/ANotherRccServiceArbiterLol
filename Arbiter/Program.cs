using System.Text.Json;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            Config.Parse(args);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
            Environment.Exit(1);
        }

        if (!Config.SkipSysStats)
        {
            if (!Helpers.SysStats())
            {
                Logger.Error("Start is not a valid member of NetworkServer");
                return;
            }
        }
        else
        {
            Logger.Warn("SysStats skipped");
        }

        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapPost("/api/v1/gameserver", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) ||
                !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<GameserverRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (body == null || body.PlaceId <= 0)
                return Results.BadRequest(new { error = "invalid_request" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();
            var clientIp = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received a gameserver request from {clientIp}, creating gameserver job={jobId} place={body.PlaceId} port={port}");

            if (!Helpers.StartGameserver(jobId, port, body.PlaceId, out int pid))
                return Results.Problem("RCCService OpenJob failed");

            return Results.Json(new { status = "ready", jobId, port, pid});
        });

        app.MapPost("/api/v1/gameserver/kill", (KillRequest req) =>
        {
            if (!Helpers.KillbyID(req.pid))
                return Results.NotFound(new { error = "process_not_found" });

            return Results.Ok(new { status = "killed", pid = req.pid });
        });

        app.MapGet("/api/v1/health", () =>
        {
            if (!Health.IsHealthy(out var ram))
            {
                Logger.Warn($"Health is degrading, ram={ram:F1}%");

                return Results.Json(new
                {
                    status = "stressed",
                    ram = MathF.Round(ram, 1)
                }, statusCode: 503);
            }

            return Results.Ok(new
            {
                status = "alive",
                ram = MathF.Round(ram, 1)
            });
        });
        app.Run("http://0.0.0.0:7000");
    }
}

public record GameserverRequest(int PlaceId);
public record KillRequest(int pid);