using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Xunit;

namespace Canhoes.Tests;

public class UploadsTests
{
    private sealed class TestEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Canhoes.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = default!;
        public IFileProvider ContentRootFileProvider { get; set; } = default!;
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
    }

    [Fact]
    public async Task GetUpload_ShouldReturnNotFoundForMissingPath()
    {
        var controller = new UploadsController(new CanhoesDbContext(new DbContextOptionsBuilder<CanhoesDbContext>().Options), new TestEnv());
        var result = await controller.GetUpload(null, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
