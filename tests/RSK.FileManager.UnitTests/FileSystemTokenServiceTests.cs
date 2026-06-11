using FluentAssertions;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class FileSystemTokenServiceTests
{
    private const string Secret = "this-is-a-strong-secret-key-32+chars";
    private static readonly FileSystemTokenService Service = new(Secret);

    [Fact]
    public void Valid_token_round_trips()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = Service.Create("invoices/inv001.pdf", expires);

        Service.Validate("invoices/inv001.pdf", expires.ToUnixTimeSeconds(), token)
            .Should().BeTrue();
    }

    [Fact]
    public void Token_for_one_file_does_not_validate_for_another()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = Service.Create("public/brochure.pdf", expires);

        // Same expiry, different path — must be rejected.
        Service.Validate("private/salaries.xlsx", expires.ToUnixTimeSeconds(), token)
            .Should().BeFalse();
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(-1);
        var token = Service.Create("invoices/inv001.pdf", expires);

        Service.Validate("invoices/inv001.pdf", expires.ToUnixTimeSeconds(), token)
            .Should().BeFalse();
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = Service.Create("invoices/inv001.pdf", expires);
        // Flip the FIRST character (its bits are always significant, unlike the last
        // Base64 char whose low bits can fall in the discarded remainder).
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token.Substring(1);

        Service.Validate("invoices/inv001.pdf", expires.ToUnixTimeSeconds(), tampered)
            .Should().BeFalse();
    }

    [Fact]
    public void Never_expiring_token_validates_with_no_expiry()
    {
        var token = Service.Create("invoices/inv001.pdf", expiresOn: null);

        Service.Validate("invoices/inv001.pdf", expiresUnix: null, token)
            .Should().BeTrue();
    }

    [Fact]
    public void Token_is_url_safe()
    {
        var token = Service.Create("invoices/inv001.pdf", DateTimeOffset.UtcNow.AddHours(1));

        token.Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public void A_different_secret_produces_a_different_signature()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = new FileSystemTokenService("a-completely-different-secret-32-chars").Create("invoices/inv001.pdf", expires);

        Service.Validate("invoices/inv001.pdf", expires.ToUnixTimeSeconds(), token)
            .Should().BeFalse();
    }
}
