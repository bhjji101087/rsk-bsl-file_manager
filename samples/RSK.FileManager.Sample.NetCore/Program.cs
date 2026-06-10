using RSK.FileManager;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// One line. Provider (FileSystem vs AzureBlob) is chosen by configuration — no code change.
builder.Services.AddFileManager(builder.Configuration);
builder.Services.AddHealthChecks().AddFileManagerHealthCheck();

var app = builder.Build();

// Serves FileSystem secure URLs. No-op when the provider is AzureBlob.
app.MapFileManagerFileServer();
app.MapHealthChecks("/health");

// POST a file (multipart/form-data, field name "file").
app.MapPost("/upload", async (IFormFile file, IFileManagerService files) =>
{
    await using var stream = file.OpenReadStream();
    var result = await files.UploadAsync(
        new FileUploadRequest
        {
            FilePath = file.FileName,
            ContentType = file.ContentType,
            Overwrite = true
        },
        stream);
    return Results.Ok(result);
}).DisableAntiforgery();

// Download a stored file, e.g. GET /download/docs/a.txt
app.MapGet("/download/{*path}", async (string path, IFileManagerService files) =>
{
    var download = await files.DownloadAsync(path);
    return Results.Stream(download.Content, download.ContentType);
});

// Get a time-limited secure URL, e.g. GET /url/docs/a.txt
app.MapGet("/url/{*path}", async (string path, IFileManagerService files) =>
    Results.Ok(await files.GetSecureUrlAsync(path)));

app.Run();
