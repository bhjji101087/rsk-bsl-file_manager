using FluentAssertions;
using RSK.FileManager.Abstractions;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class FileManagerOptionsValidationTests
{
    private const string StrongSecret = "this-is-a-strong-secret-key-32+chars";

    private static FileManagerOptions ValidFileSystem(int expiryHours = 1) => new()
    {
        Provider = StorageProvider.FileSystem,
        DefaultSecureUrlExpiryHours = expiryHours,
        FileSystem = new FileSystemOptions
        {
            RootPath = @"C:\RSK\Uploads",
            ServeBaseUrl = "https://app.rsk.com/files",
            TokenSecret = StrongSecret
        }
    };

    private static FileManagerOptions ValidAzureConnString(int expiryHours = 1) => new()
    {
        Provider = StorageProvider.AzureBlob,
        RootContainer = "rsk-files",
        DefaultSecureUrlExpiryHours = expiryHours,
        AzureBlob = new AzureBlobOptions
        {
            UseManagedIdentity = false,
            ConnectionString = "UseDevelopmentStorage=true"
        }
    };

    private static FileManagerOptions ValidAzureMsi(int expiryHours = 1) => new()
    {
        Provider = StorageProvider.AzureBlob,
        RootContainer = "rsk-files",
        DefaultSecureUrlExpiryHours = expiryHours,
        AzureBlob = new AzureBlobOptions
        {
            UseManagedIdentity = true,
            AccountName = "rskstorage"
        }
    };

    // ── Valid configurations do not throw ────────────────────────────────────

    [Fact]
    public void Valid_filesystem_passes() =>
        ValidFileSystem().Invoking(o => o.Validate()).Should().NotThrow();

    [Fact]
    public void Valid_azure_connstring_passes() =>
        ValidAzureConnString().Invoking(o => o.Validate()).Should().NotThrow();

    [Fact]
    public void Valid_azure_msi_passes() =>
        ValidAzureMsi().Invoking(o => o.Validate()).Should().NotThrow();

    [Fact]
    public void Filesystem_never_expire_is_allowed() =>
        ValidFileSystem(expiryHours: 0).Invoking(o => o.Validate()).Should().NotThrow();

    // ── Root-level invalid values ────────────────────────────────────────────

    [Fact]
    public void Negative_expiry_throws()
    {
        var o = ValidFileSystem(expiryHours: -1);
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*DefaultSecureUrlExpiryHours*");
    }

    [Fact]
    public void Nonpositive_max_size_throws()
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.FileSystem,
            MaxFileSizeBytes = 0,
            FileSystem = ValidFileSystem().FileSystem
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*MaxFileSizeBytes*");
    }

    // ── Azure / Managed Identity rules ───────────────────────────────────────

    [Fact]
    public void Msi_without_account_name_throws()
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.AzureBlob,
            RootContainer = "rsk-files",
            AzureBlob = new AzureBlobOptions { UseManagedIdentity = true, AccountName = "" }
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*AccountName*");
    }

    [Fact]
    public void Msi_with_never_expire_throws()
    {
        ValidAzureMsi(expiryHours: 0).Invoking(o => o.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*Managed Identity*");
    }

    [Fact]
    public void Msi_over_seven_days_throws()
    {
        ValidAzureMsi(expiryHours: 169).Invoking(o => o.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*168*");
    }

    [Fact]
    public void Connstring_mode_without_connstring_throws()
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.AzureBlob,
            RootContainer = "rsk-files",
            AzureBlob = new AzureBlobOptions { UseManagedIdentity = false, ConnectionString = "" }
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*ConnectionString*");
    }

    [Fact]
    public void Azure_without_root_container_throws()
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.AzureBlob,
            RootContainer = "",
            AzureBlob = new AzureBlobOptions { UseManagedIdentity = false, ConnectionString = "x" }
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*RootContainer*");
    }

    // ── File System rules ────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("REPLACE_WITH_STRONG_SECRET_KEY_aaaa")]
    public void Weak_or_placeholder_secret_throws(string secret)
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.FileSystem,
            FileSystem = new FileSystemOptions
            {
                RootPath = @"C:\RSK\Uploads",
                ServeBaseUrl = "https://app.rsk.com/files",
                TokenSecret = secret
            }
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*TokenSecret*");
    }

    [Fact]
    public void Filesystem_without_rootpath_throws()
    {
        var o = new FileManagerOptions
        {
            Provider = StorageProvider.FileSystem,
            FileSystem = new FileSystemOptions
            {
                RootPath = "",
                ServeBaseUrl = "https://app.rsk.com/files",
                TokenSecret = StrongSecret
            }
        };
        o.Invoking(x => x.Validate())
            .Should().Throw<FileManagerConfigException>()
            .WithMessage("*RootPath*");
    }
}
