using Canhoes.Api.Data;
using Canhoes.Api.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    options.UseSqlServer(connectionString);
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var db = scope.ServiceProvider.GetRequiredService<CanhoesDbContext>();

await DatabaseSetupRunner.InitializeAsync(db, webRootPath);

Console.WriteLine("Canhoes database setup completed.");
