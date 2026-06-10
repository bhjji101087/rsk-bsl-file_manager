using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AspNetCore;
using RSK.FileManager.Extensions;
using Xunit;

namespace RSK.FileManager.IntegrationTests;

public sealed class FileServerEndpointTests : IDisposable
{
    private const string Secret = "this-is-a-strong-secret-key-32+chars";
    private readonly string _root;

    public FileServerEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rskfm-ep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileManager:Provider"] = "FileSystem",
            ["FileManager:FileSystem:RootPath"] = _root,
            ["FileManager:FileSystem:ServeBaseUrl"] = "https://localhost/files",
            ["FileManager:FileSystem:TokenSecret"] = Secret,
            ["FileManager:DefaultSecureUrlExpiryHours"] = "1",
            ["FileManager:GenerateUrlOnUpload"] = "false"
        });
        builder.Services.AddFileManager(builder.Configuration);

        var app = builder.Build();
        app.MapFileManagerFileServer();
        return app;
    }

    [Fact]
    public async Task Valid_token_streams_the_file()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "hello");

        var app = BuildApp();
        await app.StartAsync();
        try
        {
            var svc = app.Services.GetRequiredService<IFileManagerService>();
            var full = (await svc.GetSecureUrlAsync("a.txt")).Url;
            var relative = full.Substring(full.IndexOf("/files", StringComparison.Ordinal));

            var client = app.GetTestClient();
            var resp = await client.GetAsync(relative);

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await resp.Content.ReadAsStringAsync()).Should().Be("hello");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Tampered_token_is_forbidden()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "hello");

        var app = BuildApp();
        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var resp = await client.GetAsync("/files/a.txt?expires=9999999999&token=not-a-valid-token");
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
