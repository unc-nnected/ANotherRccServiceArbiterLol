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
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
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
            if (Config.service)
            {
                Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "ANRSAL";
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<Worker>();
                });
            }
        }
        catch (Exception ex)
        {
            // what the fuck happend
            throw new Exception(ex.ToString()); //Environment.Exit(1);
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
            if (!Config.Ready && !context.Request.Path.StartsWithSegments("/GetStats"))
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
        app.MapPost("/StartGame", async (HttpRequest req) =>
        {
            var type = ParseJobType(req.Query["type"].FirstOrDefault());
            if (type is null)
                return Results.BadRequest(new { message = "BadRequest" });

            switch (type.Value)
            {
                case JobType.GameServer:
                    return await ExecuteAuthorizedJobAsync<GameserverRequest>(req, "gameserver", body => body.PlaceId > 0, body => {
                            var jobId = Guid.NewGuid().ToString();

                            int fakeahport = Helpers.StartGameserver(jobId, body.PlaceId, out string? render, body.TeamCreate, out int ignoredPort, out int pid);

                            if (fakeahport == 0)
                                return Task.FromResult<IResult>(Results.Problem("RCCService couldn't Open a Job"));

                            return Task.FromResult<IResult>(
                                Results.Json(new { message = "succeeded", jobId, fakeahport, pid }));
                        });

                case JobType.Avatar:
                    return await ExecuteAuthorizedJobAsync<ARenderRequest>(req, "avatar render", body => body.UserId > 0, body => {
                            var jobId = Guid.NewGuid().ToString();

                            if (!Helpers.ARender(jobId, body.UserId, out string? render, body.IsHeadshot, body.IsClothing))
                                return Task.FromResult<IResult>(Results.Problem("RCCService couldn't Open a Job"));

                            if (render is null)
                                return Task.FromResult<IResult>(Results.Problem("RCCService failed to render"));

                            return Task.FromResult<IResult>(
                                Results.Json(new { message = "succeeded", jobId, base64 = render }));
                        });

                case JobType.Place:
                    return await ExecuteAuthorizedJobAsync<RenderRequest>(req, "place render", body => body.PlaceId > 0, body => {
                            var jobId = Guid.NewGuid().ToString();

                            if (!Helpers.Render(jobId, body.PlaceId, out string? render))
                                return Task.FromResult<IResult>(Results.Problem("RCCService couldn't Open a Job"));

                            if (render is null)
                                return Task.FromResult<IResult>(Results.Problem("RCCService failed to render"));

                            return Task.FromResult<IResult>(
                                Results.Json(new { message = "succeeded", jobId, base64 = render }));
                        });

                case JobType.Model:
                    return await ExecuteAuthorizedJobAsync<MRenderRequest>(req, "model render", body => body.AssetId > 0, body => {
                            var jobId = Guid.NewGuid().ToString();

                            if (!Helpers.MRender(jobId, body.AssetId, out string? render))
                                return Task.FromResult<IResult>(Results.Problem("RCCService couldn't Open a Job"));

                            if (render is null)
                                return Task.FromResult<IResult>(Results.Problem("RCCService failed to render"));

                            return Task.FromResult<IResult>(
                                Results.Json(new { message = "succeeded", jobId, base64 = render }));
                        });

                case JobType.Mesh:
                    return await ExecuteAuthorizedJobAsync<MMRenderRequest>(req, "mesh render", body => body.MeshId > 0, body => {
                            var jobId = Guid.NewGuid().ToString();

                            if (!Helpers.MMRender(jobId, body.MeshId, out string? render))
                                return Task.FromResult<IResult>(Results.Problem("RCCService couldn't Open a Job"));

                            if (render is null)
                                return Task.FromResult<IResult>(Results.Problem("RCCService failed to render"));

                            return Task.FromResult<IResult>(
                                Results.Json(new { message = "succeeded", jobId, base64 = render }));
                        });

                default:
                    return Results.BadRequest(new { error = "badrequest" });
            }
        }).RequireRateLimiting("strict");

        app.MapPost("/StopGame", (HttpRequest http, KillRequest req) =>
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

        app.MapGet("/GetStats", () =>
        {
            var healthy = Health.IsHealthy(out var ram);

            var response = new
            {
                status = healthy ? "normal" : "stressed",
                PhysicalMemoryGigabytesUsage = MathF.Round(ram, 1),
                availablePhysicalMemoryGigabytes = MathF.Round(Health.AvailablePhysicalMemoryGigabytes, 2),
                totalPhysicalMemoryGigabytes = MathF.Round(Health.TotalPhysicalMemoryGigabytes, 2),
                cpuUsage = MathF.Round(Health.CpuUsage, 2),
                downloadSpeedKilobytesPerSecond = MathF.Round(Health.DownloadSpeedKilobytesPerSecond, 2),
                uploadSpeedKilobytesPerSecond = MathF.Round(Health.UploadSpeedKilobytesPerSecond, 2),
                logicalProcessorCount = Health.LogicalProcessorCount,
                processorCount = Health.ProcessorCount,
                rccServiceProcesses = Health.RccServiceProcesses,
                rccVersion = Health.RccVersion,
                arbiterVersion = Health.ArbiterVersion
            };

            return Results.Json(response);
        }).RequireRateLimiting("unstrict");

        app.MapPost("RenewLease", (HttpRequest req, RenewLeaseBody body) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            if (string.IsNullOrWhiteSpace(body.gameId) || body.expirationInSeconds <= 0)
                return Results.Json(new { error = "badrequest" }, statusCode: 400);

            var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            if (Config.debug)
                Logger.Info($"New client {clientIP} renew leasing {body.gameId} with {body.expirationInSeconds} seconds");

            var ok = Helpers.RenewLease(body.gameId, body.expirationInSeconds);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireRateLimiting("unstrict");

        app.MapGet("/GetAllJobs", (HttpRequest req, int? port, int? limit) =>
        {
            if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            {
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            int fakeahjobs = Math.Clamp(limit ?? 50, 1, 50);
            var jobs = Helpers.GetAllJobs(port, fakeahjobs);
            return Results.Json(jobs);
        }).RequireRateLimiting("strict");

        app.MapGet("/GetJob/{jobId}", (HttpRequest req, string jobId) =>
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

        Logger.Print("Intializing RCCService Pool");
        Helpers.runPoolManager();
        while (!Config.Ready)
            Thread.Sleep(100);
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
        app.Run($"http://0.0.0.0:{Config.port}");
    }
    enum JobType
    {
        GameServer,
        Avatar,
        Place,
        Model,
        Mesh
    }

    static JobType? ParseJobType(string? value)
    {
        return Enum.TryParse<JobType>(value, ignoreCase: true, out var type) ? type : null;
    }

    static async Task<IResult> ExecuteAuthorizedJobAsync<TBody>(HttpRequest req, string name, Func<TBody, bool> validate, Func<TBody, Task<IResult>> run){
        if (!req.Headers.TryGetValue("Authorization", out var auth) || !Helpers.IsAuthorized(auth!))
            return Results.Json(new { error = "unauthorized" }, statusCode: 401);

        var body = await JsonSerializer.DeserializeAsync<TBody>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (body is null || !validate(body))
            return Results.BadRequest(new { error = "badrequest" });

        var clientIP = req.Headers.TryGetValue("X-Forwarded-For", out var forwarded) ? forwarded.ToString().Split(',')[0].Trim() : req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        Logger.Info($"New client {clientIP} creating {name}");

        return await run(body);
    }

    delegate Task<IResult> JobHandler(HttpRequest req);
}