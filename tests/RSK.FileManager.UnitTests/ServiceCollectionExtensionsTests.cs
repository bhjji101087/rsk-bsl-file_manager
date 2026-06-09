using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RSK.FileManager;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AzureBlob;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class ServiceCollectionExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddFileManager_resolves_filesystem_provider()
    {
        var cfg = Config(new Dictionary<string, string?>
        {
            ["FileManager:Provider"] = "FileSystem",
            ["FileManager:FileSystem:RootPath"] = Path.GetTempPath(),
            ["FileManager:FileSystem:ServeBaseUrl"] = "https://app.rsk.com/files",
            ["FileManager:FileSystem:TokenSecret"] = "this-is-a-strong-secret-key-32+chars"
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileManager(cfg);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IFileManagerService>().Should().BeOfType<FileSystemProvider>();
    }

    [Fact]
    public void AddFileManager_resolves_azure_provider_without_logging_registered()
    {
        var cfg = Config(new Dictionary<string, string?>
        {
            ["FileManager:Provider"] = "AzureBlob",
            ["FileManager:RootContainer"] = "rsk-files",
            ["FileManager:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true"
        });

        var services = new ServiceCollection();   // intentionally no AddLogging
        services.AddFileManager(cfg);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IFileManagerService>().Should().BeOfType<AzureBlobProvider>();
    }

    [Fact]
    public void AddFileManager_missing_section_throws()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddFileManager(Config(new Dictionary<string, string?>()));
        act.Should().Throw<FileManagerConfigException>().WithMessage("*missing*");
    }

    [Fact]
    public void AddFileManager_invalid_config_throws_at_registration()
    {
        var cfg = Config(new Dictionary<string, string?>
        {
            ["FileManager:Provider"] = "AzureBlob",
            ["FileManager:RootContainer"] = "rsk-files",
            ["FileManager:AzureBlob:UseManagedIdentity"] = "true",
            ["FileManager:AzureBlob:AccountName"] = "rskstorage",
            ["FileManager:DefaultSecureUrlExpiryHours"] = "0"   // never-expire is invalid with MSI
        });

        var services = new ServiceCollection();
        Action act = () => services.AddFileManager(cfg);
        act.Should().Throw<FileManagerConfigException>().WithMessage("*Managed Identity*");
    }
}
