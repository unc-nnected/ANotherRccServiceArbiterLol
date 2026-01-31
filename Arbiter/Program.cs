using System.Text.Json;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            Config.Parse(args);
            Logger.Info($"loaded {Config.GSScript.Length} bytes from gameserver script");
            Logger.Info($"loaded {Config.RScript.Length} bytes from place/model render script");
            Logger.Info($"loaded {Config.RAScript.Length} bytes from avatar render script");
            Logger.Info("Config read");
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
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!)) {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<GameserverRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.PlaceId <= 0)
                return Results.BadRequest(new { error = "invalid_request" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();
            var clientIp = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received a gameserver request from {clientIp}, creating gameserver job={jobId} place={body.PlaceId} port={port}");

            if (!Helpers.StartGameserver(jobId, port, body.PlaceId, out int pid, out string? render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            return Results.Json(new { status = "ready", jobId, port, pid});
        });

        app.MapPost("/api/v1/gameserver/kill", (KillRequest req) =>
        {
            if (!Helpers.KillbyID(req.pid))
                return Results.NotFound(new { error = "notfound" });

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

        app.MapPost("/api/v1/avatar-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!)) {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<ARenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.UserId <= 0)
                return Results.BadRequest(new { error = "bad_request" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIp = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received an avatar render request from {clientIp}, job={jobId} port={port}");

            if (!Helpers.ARender(jobId, port, body.UserId, out int pid, out string? render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            if (!Helpers.KillbyID(pid))
                return Results.NotFound(new { error = "notfound" });

            return Results.Json(new
            {
                jobId,
                pid,
                base64 = render
            });
        });

        app.MapPost("/api/v1/place-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!)) {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<RenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.PlaceId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIp = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received an place render request from {clientIp}, job={jobId} port={port}");

            if (!Helpers.Render(jobId, port, body.PlaceId, out int pid, out string? render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            if (!Helpers.KillbyID(pid))
                return Results.NotFound(new { error = "notfound" });

            return Results.Json(new
            {
                jobId,
                pid,
                base64 = render
            });
        });

        app.MapPost("/api/v1/model-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<MRenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.AssetID <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIp = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received an model render request from {clientIp}, job={jobId} port={port}");

            if (!Helpers.MRender(jobId, port, body.AssetID, out int pid, out string? render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            if (!Helpers.KillbyID(pid))
                return Results.NotFound(new { error = "notfound" });

            return Results.Json(new
            {
                jobId,
                pid,
                base64 = render
            });
        });
        app.Run("http://0.0.0.0:7000");
    }
}

public record RenderRequest(int PlaceId);
public record ARenderRequest(int UserId);
public record MRenderRequest(int AssetID);
public record GameserverRequest(int PlaceId);
public record KillRequest(int pid);