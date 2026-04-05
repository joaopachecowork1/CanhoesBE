using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Canhoes.Api;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Middleware;
using Canhoes.Api.Startup;
using Canhoes.BL.Interfaces;
using Canhoes.BL.Services;

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
        ?? builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=localhost;Initial Catalog=Canhoes;Integrated Security=True;TrustServerCertificate=True;";
    opt.UseSqlServer(cs, sql =>
    {
        // Azure SQL can reject connections briefly during failover or warm-up.
        // Let EF retry those transient faults before the app gives up on startup.
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(15),
            errorNumbersToAdd: null);
    });
});

builder.Services.AddScoped<ITokenService, TokenService>();

// --- AUTH (Google id_token) ---
var clientId = builder.Configuration["Auth:Google:ClientId"];
// Frontend sends: Authorization: Bearer <Google ID token>
// We validate token and map (sub/email/name) to a local UserEntity in the DB.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        // Google OIDC
        opt.Authority = "https://accounts.google.com";
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = !string.IsNullOrWhiteSpace(clientId),
            ValidAudience = builder.Configuration["Auth:Google:ClientId"],
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

builder.Services.AddFrontendCors(builder.Configuration);

var app = builder.Build();

var webRootPath = ResolveWebRootPath(app.Environment);

app.UseFrontendCors();

// Global error handling â€“ must be first in the middleware pipeline.
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

// Serve uploaded images (for CanhÃµes do Ano)
app.UseStaticFiles();

app.UseAuthentication();
// Injetamos o nosso Mock SE a flag estiver ativa
var useMockAuth = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Auth:UseMockAuth");
if (useMockAuth)
{
    app.UseMiddleware<MockAuthMiddleware>();
}
app.UseAuthorization();

// Map authenticated Google user to local DB user (first user becomes admin)
app.UseMiddleware<UserContextMiddleware>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

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


