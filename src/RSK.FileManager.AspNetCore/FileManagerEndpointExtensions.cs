using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RSK.FileManager.Abstractions;
using RSK.FileManager.FileSystem;

namespace RSK.FileManager.AspNetCore;

/// <summary>
/// Maps the File System secure-URL serving endpoint, making
/// <see cref="IFileManagerService.GetSecureUrlAsync"/> turn-key for the FileSystem
/// provider without the consuming app writing a controller.
/// </summary>
public static class FileManagerEndpointExtensions
{
    /// <summary>
    /// Maps <c>GET {ServeBaseUrl-path}/{**path}?expires=&amp;token=</c>. The HMAC token
    /// is validated, the path is re-sanitized, and the file is streamed. Returns 403 for
    /// a bad/expired token, 404 for a missing file, 400 for an invalid path.
    /// No-op when the configured provider is not FileSystem.
    /// </summary>
    public static IEndpointRouteBuilder MapFileManagerFileServer(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));

        var options = endpoints.ServiceProvider.GetRequiredService<FileManagerOptions>();
        if (options.Provider != StorageProvider.FileSystem)
            return endpoints;   // Azure SAS URLs are served by Azure directly; nothing to map.

        var basePath = GetBasePath(options.FileSystem.ServeBaseUrl);
        var tokenService = new FileSystemTokenService(options.FileSystem.TokenSecret);
        var pattern = (basePath.Length > 0 ? basePath : string.Empty) + "/{**path}";

        endpoints.MapGet(pattern, async (HttpContext ctx, string path) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            long? expires = long.TryParse(ctx.Request.Query["expires"].ToString(), out var e) ? e : (long?)null;

            if (!tokenService.Validate(path, expires, token))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var fileManager = ctx.RequestServices.GetRequiredService<IFileManagerService>();
            try
            {
                var download = await fileManager.DownloadAsync(path, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Stream(download.Content, download.ContentType);
            }
            catch (FileManagerNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileManagerValidationException)
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }
        });

        return endpoints;
    }

    private static string GetBasePath(string serveBaseUrl)
    {
        if (Uri.TryCreate(serveBaseUrl, UriKind.Absolute, out var uri))
        {
            var absolute = uri.AbsolutePath.Trim('/');
            return absolute.Length > 0 ? "/" + absolute : string.Empty;
        }

        var trimmed = serveBaseUrl.Trim('/');
        return trimmed.Length > 0 ? "/" + trimmed : string.Empty;
    }
}
