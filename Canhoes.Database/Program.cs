using Canhoes.Api.Data;
using Canhoes.Api.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("database.settings.json", optional: true, reloadOnChange: false);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["Database:ConnectionString"]
    ?? "Data Source=localhost;Initial Catalog=Canhoes;Integrated Security=True;TrustServerCertificate=True;";

var webRootPath = builder.Configuration["Database:WebRootPath"];
if (string.IsNullOrWhiteSpace(webRootPath))
{
    webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
}

builder.Services.AddDbContext<CanhoesDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
    });
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var db = scope.ServiceProvider.GetRequiredService<CanhoesDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Canhoes.Database");

await DatabaseSetupRunner.InitializeAsync(db, logger, webRootPath);

Console.WriteLine("Canhoes database setup completed.");
