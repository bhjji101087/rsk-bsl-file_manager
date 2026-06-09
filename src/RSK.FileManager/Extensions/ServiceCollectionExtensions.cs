using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AzureBlob;
using RSK.FileManager.FileSystem;

namespace RSK.FileManager;

/// <summary>
/// DI registration for RSK.FileManager (.NET Core / .NET 5+).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileManagerService"/> as a singleton, selecting the provider
    /// from configuration. Configuration is validated immediately (fail fast); a bad
    /// configuration throws <see cref="FileManagerConfigException"/> here, not at first use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="sectionName">Configuration section name. Defaults to "FileManager".</param>
    public static IServiceCollection AddFileManager(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "FileManager")
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
            throw new FileManagerConfigException($"Configuration section '{sectionName}' is missing.");

        var options = section.Get<FileManagerOptions>()
            ?? throw new FileManagerConfigException($"Configuration section '{sectionName}' is empty or invalid.");

        options.Validate();   // fail fast at startup

        services.AddSingleton(options);
        services.AddSingleton<IFileManagerService>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            return CreateProvider(options, loggerFactory);
        });

        return services;
    }

    /// <summary>Creates the configured provider with provider-specific loggers.</summary>
    internal static IFileManagerService CreateProvider(FileManagerOptions options, ILoggerFactory loggerFactory)
        => options.Provider switch
        {
            StorageProvider.AzureBlob => new AzureBlobProvider(options, loggerFactory.CreateLogger<AzureBlobProvider>()),
            StorageProvider.FileSystem => new FileSystemProvider(options, loggerFactory.CreateLogger<FileSystemProvider>()),
            _ => throw new FileManagerConfigException($"Unknown provider: {options.Provider}")
        };
}
