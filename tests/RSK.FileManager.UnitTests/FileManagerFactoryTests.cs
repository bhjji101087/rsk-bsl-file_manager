using System.Collections.Specialized;
using System.IO;
using FluentAssertions;
using RSK.FileManager;
using RSK.FileManager.Abstractions;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class FileManagerFactoryTests
{
    [Fact]
    public void Initialize_then_GetService_returns_cached_singleton_provider()
    {
        var settings = new NameValueCollection
        {
            ["FileManager:Provider"] = "FileSystem",
            ["FileManager:FileSystem:RootPath"] = Path.GetTempPath(),
            ["FileManager:FileSystem:ServeBaseUrl"] = "https://app.rsk.com/files",
            ["FileManager:FileSystem:TokenSecret"] = "this-is-a-strong-secret-key-32+chars"
        };

        FileManagerFactory.Initialize(settings);

        var first = FileManagerFactory.GetService();
        var second = FileManagerFactory.GetService();

        first.Should().BeOfType<FileSystemProvider>();
        first.Should().BeSameAs(second);   // cached singleton
    }

    [Fact]
    public void Initialize_with_invalid_config_throws()
    {
        var settings = new NameValueCollection
        {
            ["FileManager:Provider"] = "FileSystem",
            ["FileManager:FileSystem:RootPath"] = Path.GetTempPath(),
            ["FileManager:FileSystem:ServeBaseUrl"] = "https://app.rsk.com/files",
            ["FileManager:FileSystem:TokenSecret"] = "too-short"   // < 32 chars
        };

        Action act = () => FileManagerFactory.Initialize(settings);
        act.Should().Throw<FileManagerConfigException>().WithMessage("*TokenSecret*");
    }
}
