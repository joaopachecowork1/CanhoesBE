using Canhoes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Tests;

internal static class DbFactory
{
    public static CanhoesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CanhoesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CanhoesDbContext(options);
    }
}
