/*
                     d8,          
                    `8P           
                                  
 d888b8b    88bd88b  88b d888b8b  
d8P' ?88    88P'  `  88Pd8P' ?88  
88b  ,88b  d88      d88 88b  ,88b 
`?88P'`88bd88'     d88' `?88P'`88b

Well, maybe I'm the vigilant, Robloxia
I'm not a part of an AI agenda
Now everybody, do the propaganda
And sing along to the age of paranoia

*/
using System.Text.Json;
using System.Reflection;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            // parse config
            Config.Parse(args);
            Logger.Print($"Access key read: {Config.FakeSECRET}");
            Logger.Print($"Current Access key: {Config.SECRET}");
            if (Config.debug)
            {
                Logger.Info($"Loaded {Config.GSScript.Length} bytes from gameserver script");
                Logger.Info($"Loaded {Config.RScript.Length} bytes from place render script");
                Logger.Info($"Loaded {Config.RAScript.Length} bytes from avatar render script");
                Logger.Info($"Loaded {Config.RMScript.Length} bytes from model render script");
                Logger.Info($"Loaded {Config.RMMScript.Length} bytes from mesh render script");
                Logger.Info($"Loaded {Config.BaseURL.Length} bytes from BaseURL");
                Logger.Info("Config read");
            }
        }
        catch (Exception ex)
        {
            // what the fuck happend
            Logger.Error(ex.Message);
            Environment.Exit(1);
        }

        if (!Config.SkipSysStats)
        {
            // does sysstats trust this system?
            if (!Helpers.SysStats())
            {
                Logger.Error("Start is not a valid member of NetworkServer");
                return;
            }
        }
        else
        {
            // nevermind
            Logger.Warn("SysStats skipped");
        }

        Logger.Print("Service starting...");

        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapPost("/api/v1/gameserver", async (HttpRequest req) =>
        {
            // check authorization so we wont get random ass gameservers
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!)) {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            // get post data
            var body = await JsonSerializer.DeserializeAsync<GameserverRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // validate
            if (body == null || body.PlaceId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            // do a bunch of variations..
            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();
            // get client's ip for logging
            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received a gameserver request from {clientIP}, creating gameserver job={jobId} place={body.PlaceId} port={port}");

            // start the gameserver!
            if (Helpers.StartGameserver(jobId, port, body.PlaceId, out int pid, out string? render, body.TeamCreate, out int fakeahport) == 0)
                return Results.Problem("RCCService couldn't execute OpenJob");

            return Results.Json(new { status = "ready", jobId, fakeahport, pid});
        });

        app.MapPost("/api/v1/gameserver/kill", (HttpRequest http, KillRequest req) =>
        {
            if (!http.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);

            var job = Helpers.GetJobByPID(req.pid);

            if (!Helpers.KillbyID(req.pid))
                return Results.NotFound(new { error = "notfound" });

            if (job != null)
            {
                job.Alive = false;
                Helpers.RemoveJob(job.JobId);

                if (Config.debug)
                    Logger.Warn($"Killed {job.JobId} (pid={req.pid}, port={job.Port})");
            }

            return Results.Ok(new
            {
                status = "killed",
                pid = req.pid,
                jobId = job?.JobId
            });
        });



        app.MapGet("/api/v1/health", () =>
        {
            // we get ram
            if (!Health.IsHealthy(out var ram))
            {
                // uh oh, ram is overloaded
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
                return Results.BadRequest(new { error = "badrequest" });

            bool headshot = body.IsHeadshot;
            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received an avatar render request from {clientIP}, job={jobId} port={port}");

            if (!Helpers.ARender(jobId, port, body.UserId, out int pid, out string? render, headshot))
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

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received an place render request from {clientIP}, job={jobId} port={port}");

            if (!Helpers.Render(jobId, port, body.PlaceId, out int pid, out string? render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            // we kill the rccservice instantly because that makes our life easier
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

            if (body == null || body.AssetId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received a model render request from {clientIP}, job={jobId} port={port}");

            if (!Helpers.MRender(jobId, port, body.AssetId, out int pid, out string? render))
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

        app.MapPost("/api/v1/mesh-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<MMRenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.MeshId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Warn($"Received a mesh render request from {clientIP}, job={jobId} port={port}");

            if (!Helpers.MMRender(jobId, port, body.MeshId, out int pid, out string? render))
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

        app.MapGet("/", () =>
        {
            var assembly = Assembly.GetExecutingAssembly();

            var videos = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Arbiter.videos.", StringComparison.OrdinalIgnoreCase) && (n.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))).ToArray();
            // WTF IS THIS CODE
            if (videos.Length == 0)
                return Results.NotFound("No embedded videos found :(");

            var chosen = videos[Random.Shared.Next(videos.Length)];

            using var resourceStream = assembly.GetManifestResourceStream(chosen);
            if (resourceStream == null)
                // fuck you idiot
                // use Problem, not NotFound
                return Results.Problem("Couldn't load video");

            var ms = new MemoryStream();
            resourceStream.CopyTo(ms);
            ms.Position = 0;

            return Results.File(
                ms,
                contentType: chosen.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime" : "video/mp4",
                enableRangeProcessing: true
            );
        });

        app.MapPost("/api/v1/renewlease", (HttpRequest req, RenewLeaseBody body) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            if (string.IsNullOrWhiteSpace(body.jobId) || body.seconds <= 0)
                return Results.Json(new { error = "badrequest" }, statusCode: 400);

            var ok = Helpers.RenewLease(body.jobId, body.seconds);
            return ok ? Results.Ok() : Results.NotFound();
        });

        app.MapGet("/api/v1/getalljobs", (HttpRequest req, int? port) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var jobs = Helpers.GetAllJobs(port);
            return Results.Json(jobs);
        });

        app.MapPost("/api/v1/presence/join", (HttpRequest req, PresenceBody body) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var ok = Helpers.UpdatePresence(body.jobId, joining: true);
            return ok ? Results.Ok() : Results.NotFound();
        });

        app.MapPost("/api/v1/presence/leave", (HttpRequest req, PresenceBody body) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var ok = Helpers.UpdatePresence(body.jobId, joining: false);
            return ok ? Results.Ok() : Results.NotFound();
        });

        app.MapGet("/api/v1/job/{jobId}", (HttpRequest req, string jobId) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var job = Helpers.GetJob(jobId);

            if (job == null)
                return Results.NotFound();

            return Results.Json(job);
        });

        Logger.Print("Intializing ASP.NET Web Service");
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            Logger.Print($"Service Started on port {Config.port}");
        });
        lifetime.ApplicationStopping.Register(() =>
        {
            Logger.Print("Stopping ASP.NET service");
        });
        Logger.Print("Intializing Game Monitor Service");
        Helpers.StartGSM();
        app.Run($"http://0.0.0.0:{Config.port}");
    }
}

// bunch of post data shit!
public record RenderRequest(int PlaceId);
public record ARenderRequest(int UserId, bool IsHeadshot, bool IsClothing);
public record MRenderRequest(int AssetId);
public record MMRenderRequest(int MeshId);
public record GameserverRequest(int PlaceId, bool TeamCreate);
public record KillRequest(int pid);
public record GSMJob
{
    public string JobId { get; init; } = "";
    public int Port { get; init; }
    public int PlaceId { get; init; }
    public int Pid { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime LastHeartbeat { get; set; }

    public int Players { get; set; }
    public bool Alive { get; set; }
}
public record RenewLeaseBody(string jobId, int seconds);
public record PresenceBody(string jobId);
