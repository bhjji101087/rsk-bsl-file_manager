using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using RSK.FileManager;
using RSK.FileManager.Abstractions;

namespace RSK.FileManager.Sample.NetFramework
{
    internal static class Program
    {
        // Async entry point is supported on .NET Framework 4.6.2 with the modern SDK.
        private static async Task Main()
        {
            // No DI: initialize once from web.config / app.config <appSettings>.
            FileManagerFactory.Initialize(ConfigurationManager.AppSettings);
            var files = FileManagerFactory.GetService();

            var bytes = Encoding.UTF8.GetBytes("hello from .NET Framework 4.6.2");
            var uploaded = await files.UploadAsync(
                new FileUploadRequest { FilePath = "demo/hello.txt", Overwrite = true }, bytes);
            Console.WriteLine($"Uploaded {uploaded.StoredPath} ({uploaded.FileSizeBytes} bytes) via {uploaded.Provider}");

            // FileDownloadResult is IDisposable on .NET Framework (no IAsyncDisposable).
            using (var download = await files.DownloadAsync("demo/hello.txt"))
            using (var reader = new StreamReader(download.Content))
            {
                Console.WriteLine("Downloaded: " + await reader.ReadToEndAsync());
            }

            var url = await files.GetSecureUrlAsync("demo/hello.txt");
            Console.WriteLine("Secure URL: " + url.Url);

            await files.DeleteAsync("demo/hello.txt");
            Console.WriteLine("Deleted. Done.");
        }
    }
}
