using System.Security.Cryptography;
using System.Text;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Core;

namespace RSK.FileManager.FileSystem;

/// <summary>
/// Creates and validates HMAC-SHA256 signed tokens for File System secure URLs.
/// The signed payload covers both the (sanitized) path and the expiry, so a token
/// cannot be reused for a different file. Validation is constant-time and tokens
/// are URL-safe Base64.
/// </summary>
public sealed class FileSystemTokenService
{
    private const char Separator = '\n';
    private readonly byte[] _key;

    /// <summary>Creates the service with the configured signing secret.</summary>
    /// <exception cref="FileManagerConfigException">If the secret is null or empty.</exception>
    public FileSystemTokenService(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new FileManagerConfigException("File System token secret must not be empty.");

        _key = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// Creates a URL-safe token for the given path and optional expiry.
    /// When <paramref name="expiresOn"/> is null the token never expires
    /// (the expiry is omitted from the signed payload).
    /// </summary>
    public string Create(string path, DateTimeOffset? expiresOn)
    {
        var payload = BuildPayload(path, expiresOn is null ? (long?)null : expiresOn.Value.ToUnixTimeSeconds());
        return Sign(payload);
    }

    /// <summary>
    /// Validates a token for the given path and optional expiry. Returns false if the
    /// token is expired, malformed, or does not match. Comparison is constant-time.
    /// </summary>
    public bool Validate(string path, long? expiresUnix, string providedToken)
    {
        if (string.IsNullOrEmpty(providedToken))
            return false;

        if (expiresUnix is not null &&
            DateTimeOffset.FromUnixTimeSeconds(expiresUnix.Value) < DateTimeOffset.UtcNow)
        {
            return false;
        }

        var expected = Sign(BuildPayload(path, expiresUnix));

        byte[] expectedBytes;
        byte[] providedBytes;
        try
        {
            expectedBytes = FromUrlSafeBase64(expected);
            providedBytes = FromUrlSafeBase64(providedToken);
        }
        catch (FormatException)
        {
            return false;
        }

        return FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string BuildPayload(string path, long? expiresUnix)
    {
        var normalized = PathSanitizer.Sanitize(path);
        return expiresUnix is null
            ? normalized
            : string.Concat(normalized, Separator.ToString(), expiresUnix.Value.ToString());
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return ToUrlSafeBase64(hash);
    }

    private static string ToUrlSafeBase64(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromUrlSafeBase64(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        return CryptographicOperations.FixedTimeEquals(a, b);
#else
        // Constant-time comparison polyfill for net462 / netstandard2.0.
        // HMAC-SHA256 outputs are always 32 bytes, so the length check leaks nothing useful.
        if (a.Length != b.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];

        return diff == 0;
#endif
    }
}
