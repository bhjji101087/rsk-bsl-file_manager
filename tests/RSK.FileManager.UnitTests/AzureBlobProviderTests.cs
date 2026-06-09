using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AzureBlob;
using Xunit;

namespace RSK.FileManager.UnitTests;

// These tests run fully offline. Service SAS generation uses the well-known Azurite
// development account key, so no network or emulator is required. (Blob upload/download
// against a real backend is covered by the Azurite integration suite.)
public class AzureBlobProviderTests
{
    private static FileManagerOptions ConnStringOptions(int expiryHours = 1) => new()
    {
        Provider = StorageProvider.AzureBlob,
        RootContainer = "rsk-files",
        DefaultSecureUrlExpiryHours = expiryHours,
        GenerateUrlOnUpload = false,
        AzureBlob = new AzureBlobOptions
        {
            UseManagedIdentity = false,
            ConnectionString = "UseDevelopmentStorage=true"
        }
    };

    private static AzureBlobProvider NewProvider(FileManagerOptions options)
        => new(options, NullLogger<AzureBlobProvider>.Instance);

    [Fact]
    public void Construct_with_connection_string_succeeds()
    {
        var act = () => NewProvider(ConnStringOptions());
        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_with_managed_identity_succeeds_without_network()
    {
        // DefaultAzureCredential is lazy; constructing the client performs no I/O.
        var options = new FileManagerOptions
        {
            Provider = StorageProvider.AzureBlob,
            RootContainer = "rsk-files",
            DefaultSecureUrlExpiryHours = 1,
            AzureBlob = new AzureBlobOptions { UseManagedIdentity = true, AccountName = "rskstorage" }
        };

        var act = () => NewProvider(options);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Service_sas_is_generated_offline_with_read_permission_and_expiry()
    {
        var p = NewProvider(ConnStringOptions(expiryHours: 1));

        var result = await p.GetSecureUrlAsync("invoices/inv001.pdf");

        result.ExpiresAt.Should().NotBeNull();
        result.FilePath.Should().Be("invoices/inv001.pdf");
        result.Url.Should().Contain("sig=");   // signature present
        result.Url.Should().Contain("sp=r");   // read-only permission
        result.Url.Should().Contain("se=");    // expiry present
    }

    [Fact]
    public async Task Never_expire_service_sas_has_no_bounded_expiry()
    {
        var p = NewProvider(ConnStringOptions(expiryHours: 0));   // 0 = never (connection-string mode)

        var result = await p.GetSecureUrlAsync("invoices/inv001.pdf");

        result.ExpiresAt.Should().BeNull();
        result.Url.Should().Contain("sig=");
    }
}
