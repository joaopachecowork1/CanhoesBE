using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Mvc;
using Canhoes.Api;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Middleware;
using Canhoes.Api.Services;
using Canhoes.Api.Startup;
using Canhoes.Api.Caching;

var builder = WebApplication.CreateBuilder(args);

var publicPort = builder.Configuration["PORT"]
    ?? builder.Configuration["WEBSITES_PORT"];
if (int.TryParse(publicPort, out var parsedPort) && parsedPort > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

// Do not override the web root through builder.WebHost.
// Azure App Service already boots the app with a resolved web root
// (for example C:\home\site\wwwroot\). Forcing "wwwroot" here changes the
// host configuration to C:\home\site\wwwroot\wwwroot\ and crashes startup.
// We normalize the path only after Build(), and only when the host left it empty.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Legacy controllers still expose nested DTOs with the same short names as
    // newer event-scoped models. Use stable fully-qualified schema ids so
    // OpenAPI generation stays deterministic without changing JSON contracts.
    options.CustomSchemaIds(type =>
        (type.FullName ?? type.Name)
            .Replace("+", ".")
            .Replace("[", "_")
            .Replace("]", "_"));
});

builder.Services.AddDbContext<CanhoesDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration.GetConnectionString("Default");

    if (string.IsNullOrWhiteSpace(cs))
    {
        throw new InvalidOperationException(
            "Connection string not configured. Set ConnectionStrings__Default via environment variable or appsettings.json.");
    }

    opt.UseSqlServer(cs, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(15),
            errorNumbersToAdd: null);
    });

    // Enable sensitive data logging in development for query debugging
    if (builder.Environment.IsDevelopment())
    {
        opt.EnableSensitiveDataLogging();
    }

    // Default to NoTracking for read-heavy API workloads
    opt.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<SecretSantaService>();

// --- AUTH (Google id_token) ---
var useMockAuth = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Auth:UseMockAuth");

// Safety: never allow mock auth in production, regardless of environment name.
if (useMockAuth && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Auth:UseMockAuth cannot be enabled in non-Development environments.");
}

var clientId = builder.Configuration["Auth:Google:ClientId"]?.Trim();

if (!useMockAuth && string.IsNullOrWhiteSpace(clientId))
{
    throw new InvalidOperationException(
        "Auth:Google:ClientId is missing. Configure Google auth or enable mock auth in Development.");
}

// Frontend sends: Authorization: Bearer <Google ID token>
// We validate token and map (sub/email/name) to a local UserEntity in the DB.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        // Google OIDC
        opt.Authority = "https://accounts.google.com";
        opt.RequireHttpsMetadata = true;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = !useMockAuth,
            ValidAudience = clientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        opt.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleJwtAuth");
                var subject = context.Principal?.FindFirst("sub")?.Value ?? "unknown";

                logger.LogDebug(
                    "Google token validated for subject {Subject}.",
                    subject);
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleJwtAuth");

                logger.LogWarning(
                    context.Exception,
                    "Google token validation failed.");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Error)
                    && string.IsNullOrWhiteSpace(context.ErrorDescription))
                {
                    return Task.CompletedTask;
                }

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleJwtAuth");

                logger.LogWarning(
                    "Google auth challenge issued. Error={Error}; Description={Description}",
                    context.Error,
                    context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// In-memory cache for frequently-read, rarely-changed data
builder.Services.AddMemoryCache();

// Performance metrics collector (singleton for tracking request durations)
builder.Services.AddSingleton<RequestMetricsCollector>();

// Health checks with SQL Server
var healthCheckCs = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "";

builder.Services.AddHealthChecks()
    .AddSqlServer(
        healthCheckCs,
        name: "sql-server",
        failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);

builder.Services.AddFrontendCors(builder.Configuration);

// --- Response Compression (gzip + brotli) ---
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    // Prefer brotli (better compression) falling back to gzip (wider support)
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});

// --- Output Caching for stable GET endpoints ---
builder.Services.AddOutputCache(options =>
{
    // Default: 30 seconds for all cached endpoints
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromSeconds(30)));
    // Categories: cache for 2 minutes (changes rarely)
    options.AddPolicy("Categories", builder => builder.Expire(TimeSpan.FromMinutes(2)));
    // Event state: cache for 15 seconds (changes during phase transitions)
    options.AddPolicy("EventState", builder => builder.Expire(TimeSpan.FromSeconds(15)));
});

var app = builder.Build();

// Response compression MUST be before UseRouting to compress all responses
app.UseResponseCompression();

var webRootPath = ResolveWebRootPath(app.Environment);
app.Logger.LogInformation(
    "Authentication configured. MockAuthEnabled={MockAuthEnabled}; GoogleClientIdConfigured={GoogleClientIdConfigured}",
    useMockAuth,
    !string.IsNullOrWhiteSpace(clientId));

app.UseFrontendCors();

// Global error handling – must be first in the middleware pipeline.
app.UseMiddleware<ErrorHandlingMiddleware>();

// Performance logging – logs every request with duration.
app.UseMiddleware<PerformanceLoggingMiddleware>();

// Performance metrics – tracks request durations in memory for /admin/perf endpoint.
app.UseMiddleware<PerformanceMetricsMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

// Serve uploaded images (for CanhÃµes do Ano)
app.UseStaticFiles();

app.UseAuthentication();
if (useMockAuth)
{
    app.UseMiddleware<MockAuthMiddleware>();
}
app.UseAuthorization();

// Map authenticated Google user to local DB user (first user becomes admin)
app.UseMiddleware<UserContextMiddleware>();

// Output caching only after auth/user context is established
app.UseOutputCache();

app.MapGet("/health", async ([FromServices] Canhoes.Api.Data.CanhoesDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(healthCheckCs))
    {
        return Results.Ok(new
        {
            status = "degraded",
            database = "connection-string-not-configured",
            timestampUtc = DateTime.UtcNow
        });
    }

    var dbWorking = await db.Database.CanConnectAsync(ct);
    return Results.Ok(new
    {
        status = dbWorking ? "healthy" : "degraded",
        database = dbWorking ? "connected" : "disconnected",
        timestampUtc = DateTime.UtcNow
    });
});

// Admin performance metrics (requires authentication)
app.MapPerformanceMetrics();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CanhoesDbContext>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseSetup");

    DatabaseSetupRunner.InitializeAsync(db, logger, webRootPath).GetAwaiter().GetResult();
}

await app.RunAsync();

static string ResolveWebRootPath(IWebHostEnvironment environment)
{
    // Local dev can leave WebRootPath empty in some launch profiles. In that case
    // we fall back to a conventional ./wwwroot path without rewriting host config.
    if (!string.IsNullOrWhiteSpace(environment.WebRootPath))
    {
        return environment.WebRootPath;
    }

    var fallbackWebRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
    environment.WebRootPath = fallbackWebRoot;
    return fallbackWebRoot;
}


