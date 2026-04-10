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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.Title = "ANotherRccServiceArbiterLol";
        try
        {
            // parse config
            Config.Parse(args);
            Logger.Print($"Access key read: {Config.FakeSECRET}");
            using var sha = SHA256.Create();
            var SECREThash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(Config.SECRET)));
            Logger.Print($"Current Access key: {SECREThash}");
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
            if (!Helpers.IsTCPPortBindable(Config.port))
            {
                throw new Exception($"{Config.port} is not bindable, it must be TCP!");
            }
        }
        catch (Exception ex)
        {
            // what the fuck happend
            Logger.Error(ex.Message);
            Environment.Exit(1);
        }
        
        Logger.Print("Service starting...");

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("strict", context =>
            {
                var ip = context.Request.Headers.TryGetValue("X-Forwarded-For", out var f) ? f.ToString().Split(',')[0] : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(
                    ip,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 10,
                        TokensPerPeriod = 10,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });

            options.AddPolicy("unstrict", context =>
            {
                var ip = context.Request.Headers.TryGetValue("X-Forwarded-For", out var f) ? f.ToString().Split(',')[0] : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetConcurrencyLimiter(
                    ip,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 20
                    });
            });
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ip = context.Request.Headers.TryGetValue("X-Forwarded-For", out var f) ? f.ToString().Split(',')[0] : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(
                    ip,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 20,
                        TokensPerPeriod = 20,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 2
                    });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (!Config.Ready && !context.Request.Path.StartsWithSegments("/api/v1/health"))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers["Retry-After"] = "30";

                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Service Unavailable"
                });

                return;
            }

            await next();
        });

        app.UseRateLimiter();
        app.MapPost("/api/v1/gameserver", async (HttpRequest req) =>
        {
            // check authorization so we wont get random ass gameservers
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
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
            int pid;
            string? render;
            // get client's ip for logging
            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Info($"New client {clientIP} creating gameserver with place {body.PlaceId} with port {port}");

            // start the gameserver!
            int fakeahport = Helpers.StartGameserver(jobId, body.PlaceId, out render, body.TeamCreate, out int _, out pid);

            if (fakeahport == 0)
                return Results.Problem("RCCService couldn't execute OpenJob");

            return Results.Json(new { status = "ready", jobId, fakeahport, pid });
        }).RequireRateLimiting("strict");

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
                    Logger.Info($"Killed {job.JobId} (pid={req.pid}, port={job.Port})");
            }

            return Results.Ok(new
            {
                status = "killed",
                pid = req.pid,
                jobId = job?.JobId
            });
        }).RequireRateLimiting("unstrict");

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
                });
            }

            return Results.Ok(new
            {
                status = "alive",
                ram = MathF.Round(ram, 1)
            });
        }).RequireRateLimiting("unstrict");

        app.MapPost("/api/v1/avatar-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<ARenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.UserId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();
            int pid;
            string? render;

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Info($"New client {clientIP} creating avatar render with user {body.UserId} with port {port}");

            if (!Helpers.ARender(jobId, body.UserId, out render, body.IsHeadshot, body.IsClothing))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            return Results.Json(new
            {
                jobId,
                base64 = render
            });
        }).RequireRateLimiting("strict");

        app.MapPost("/api/v1/place-render", async (HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            var body = await JsonSerializer.DeserializeAsync<RenderRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null || body.PlaceId <= 0)
                return Results.BadRequest(new { error = "badrequest" });

            string jobId = Guid.NewGuid().ToString();
            int port = Helpers.GetPort();
            string? render;

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Info($"New client {clientIP} creating place render with place {body.PlaceId} with port {port}");

            if (!Helpers.Render(jobId, body.PlaceId, out render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            return Results.Json(new
            {
                jobId,
                base64 = render
            });
        }).RequireRateLimiting("strict");

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
            string? render;

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Info($"New client {clientIP} creating model render with place {body.AssetId} with port {port}");

            if (!Helpers.MRender(jobId, body.AssetId, out render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            return Results.Json(new
            {
                jobId,
                base64 = render
            });
        }).RequireRateLimiting("strict");

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
            string? render;

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            Logger.Info($"New client {clientIP} creating model render with place {body.MeshId} with port {port}");

            if (!Helpers.MMRender(jobId, body.MeshId, out render))
                return Results.Problem("RCCService couldn't execute OpenJob");

            if (render == null)
                return Results.Problem("RCCService failed to render");

            return Results.Json(new
            {
                jobId,
                base64 = render
            });
        }).RequireRateLimiting("strict");

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
        }).RequireRateLimiting("unstrict");

        app.MapGet("/api/v1/getalljobs", (HttpRequest req, int? port, int? limit) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            int fakeahjobs = Math.Clamp(limit ?? 50, 1, 50);
            var jobs = Helpers.GetAllJobs(port, fakeahjobs);
            return Results.Json(jobs);
        }).RequireRateLimiting("strict");

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
        }).RequireRateLimiting("strict"); // dont care honestly

        Logger.Print("Intializing ASP.NET Web Service");
        Helpers.StartGSM();
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            Logger.Print($"Service Started on port {Config.port}");
        });
        lifetime.ApplicationStopping.Register(() =>
        {
            Logger.Print("Service shutting down...");
            Helpers.killallthefags();
        });

        Logger.Print("Intializing RCCService Pool");
        Helpers.runPoolManager();
        Thread.Sleep(3000);
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
    public bool Alive { get; set; }
}
public record RenewLeaseBody(string jobId, int seconds);
public record PresenceBody(string jobId);