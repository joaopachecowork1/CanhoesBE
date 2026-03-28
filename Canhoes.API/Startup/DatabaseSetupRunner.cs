using Canhoes.Api.Data;

namespace Canhoes.Api.Startup;

public static class DatabaseSetupRunner
{
    public static async Task InitializeAsync(CanhoesDbContext db, string? webRootPath = null, CancellationToken ct = default)
    {
        db.Database.EnsureCreated();
        await DbSchema.EnsureAsync(db);

        ct.ThrowIfCancellationRequested();
        DbSeeder.Seed(db, webRootPath);
    }
}
