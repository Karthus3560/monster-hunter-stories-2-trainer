using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RidersToolkit;

internal static class AppUpdateTests
{
    static readonly byte[] Payload = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
    static string Digest => "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    static string Metadata(string tag = "v3.10.0", string? url = null, string? digest = null, long size = 4096, bool draft = false, bool prerelease = false) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft, prerelease,
        assets = new[] { new { name = $"KinshipStudio-{tag}-Setup.exe", browser_download_url = url ?? $"https://github.com/{AppUpdates.Repository}/releases/download/{tag}/KinshipStudio-{tag}-Setup.exe", digest = digest ?? Digest, size } }
    });
    static void Require(bool condition) { if (!condition) throw new Exception("Update assertion failed"); }
    static void Reject(Action run) { try { run(); } catch (InvalidDataException) { return; } throw new Exception("Unsafe update was accepted"); }
    static HttpClient Client(Func<HttpResponseMessage> response) => new(new Handler(response));
    static void InCache(Action<string> run)
    {
        string root = Path.Combine(Path.GetTempPath(), "KinshipUpdateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { run(root); }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
    internal static void Run(Action<string, Action> check)
    {
        check("Updater compares numeric stable versions and never offers equal or older releases", () =>
        {
            Require(AppUpdates.ParseRelease(Metadata(), new Version(3, 9, 0))?.Version == new Version(3, 10, 0));
            Require(AppUpdates.ParseRelease(Metadata(), new Version(3, 10, 0, 2)) is null);
            Require(AppUpdates.ParseRelease(Metadata(), new Version(4, 0, 0)) is null);
            Require(AppUpdates.ParseRelease(Metadata(draft: true), new Version(1, 0, 0)) is null);
            Require(AppUpdates.ParseRelease(Metadata(prerelease: true), new Version(1, 0, 0)) is null);
            Reject(() => AppUpdates.ParseRelease(Metadata("v3.10.0-beta"), new Version(1, 0, 0)));
        });
        check("Updater refuses installers outside the exact project release and duplicate assets", () =>
        {
            foreach (string url in new[] { "http://github.com/installer.exe", "https://example.com/installer.exe", $"https://github.com/{AppUpdates.Repository}/releases/download/v3.10.0/other.exe" })
                Reject(() => AppUpdates.ParseRelease(Metadata(url: url), new Version(1, 0, 0)));
            using var parsed = JsonDocument.Parse(Metadata());
            var duplicated = JsonSerializer.Serialize(new { tag_name = "v3.10.0", draft = false, prerelease = false, assets = new[] { parsed.RootElement.GetProperty("assets")[0], parsed.RootElement.GetProperty("assets")[0] } });
            Reject(() => AppUpdates.ParseRelease(duplicated, new Version(1, 0, 0)));
        });
        check("Updater requires a SHA-256 digest and a bounded installer size", () =>
        {
            foreach (string hash in new[] { "", "sha1:1234", "sha256:" + new string('x', 64) })
                Reject(() => AppUpdates.ParseRelease(Metadata(digest: hash), new Version(1, 0, 0)));
            foreach (long size in new[] { 0L, AppUpdates.MaximumBytes + 1 })
                Reject(() => AppUpdates.ParseRelease(Metadata(size: size), new Version(1, 0, 0)));
        });
        check("Updater checks only the fixed GitHub metadata endpoint", () =>
        {
            using var client = Client(() => new(HttpStatusCode.OK) { Content = new StringContent(Metadata()) });
            var result = new AppUpdates(client).CheckAsync(new Version(1, 0, 0), default).GetAwaiter().GetResult();
            Require(result?.Version == new Version(3, 10, 0));
        });
        check("Updater stages and verifies a complete download before marking it executable", () => InCache(root =>
        {
            using var client = Client(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
            var release = AppUpdates.ParseRelease(Metadata(), new Version(1, 0, 0))!;
            var file = new AppUpdates(client).DownloadAsync(release, root, null, default).GetAwaiter().GetResult();
            Require(File.ReadAllBytes(file).SequenceEqual(Payload) && !Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Any());
            AppUpdates.VerifyFile(file, release);
            File.WriteAllBytes(file, new byte[Payload.Length]);
            Reject(() => AppUpdates.VerifyFile(file, release));
        }));
        check("Updater refuses corrupt and incomplete downloads without leaving a runnable file", () => InCache(root =>
        {
            foreach (var bytes in new[] { new byte[Payload.Length], Payload[..1000] })
            {
                using var client = Client(() => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(bytes)) });
                var release = AppUpdates.ParseRelease(Metadata(), new Version(1, 0, 0))!;
                Reject(() => new AppUpdates(client).DownloadAsync(release, root, null, default).GetAwaiter().GetResult());
            }
            Require(!Directory.GetFiles(root, "*", SearchOption.AllDirectories).Any());
        }));
        check("Updater bounds streamed responses even when Content-Length is absent", () => InCache(root =>
        {
            using var client = Client(() => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(new byte[8192])) });
            var release = AppUpdates.ParseRelease(Metadata(), new Version(1, 0, 0))!;
            Reject(() => new AppUpdates(client).DownloadAsync(release, root, null, default).GetAwaiter().GetResult());
            Require(!Directory.GetFiles(root, "*", SearchOption.AllDirectories).Any());
        }));
        check("Updater cancellation never launches or retains a partial installer", () => InCache(root =>
        {
            using var client = Client(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            bool cancelled = false;
            try { new AppUpdates(client).DownloadAsync(AppUpdates.ParseRelease(Metadata(), new Version(1, 0, 0))!, root, null, cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && !Directory.GetFiles(root, "*", SearchOption.AllDirectories).Any());
        }));
        check("Updater rejects metadata failures and oversized responses", () =>
        {
            using var error = Client(() => new(HttpStatusCode.ServiceUnavailable));
            bool failed = false;
            try { new AppUpdates(error).CheckAsync(new Version(1, 0, 0), default).GetAwaiter().GetResult(); } catch (HttpRequestException) { failed = true; }
            Require(failed);
            using var huge = Client(() => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(new byte[1024 * 1024 + 1])) });
            Reject(() => new AppUpdates(huge).CheckAsync(new Version(1, 0, 0), default).GetAwaiter().GetResult());
        });
        check("Updater uses a stable installed location and an explicit non-elevating restart command", () =>
        {
            string existing = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RidersToolkit", "RidersToolkit.exe");
            Require(AppUpdates.IsInstalledCopy(existing));
            var command = AppUpdates.InstallCommand("C:\\update cache\\Setup.exe");
            Require(command.FileName == "C:\\update cache\\Setup.exe" && command.UseShellExecute && command.Verb == "" && command.Arguments.Contains("/UPDATE") && !command.Arguments.Contains("runas"));
        });
    }
    sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Require(request.RequestUri?.Scheme == "https");
            Require(request.RequestUri == AppUpdates.LatestApi || request.RequestUri!.AbsoluteUri.StartsWith($"https://github.com/{AppUpdates.Repository}/releases/download/", StringComparison.Ordinal));
            return Task.FromResult(response());
        }
    }
    sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
