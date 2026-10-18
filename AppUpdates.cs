using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RidersToolkit;

internal sealed record AppRelease(Version Version, string Tag, string FileName, Uri Download, long Size, string Sha256);

internal sealed class AppUpdates
{
    internal const string Repository = "Karthus3560/monster-hunter-stories-2-trainer";
    internal const string InstallerId = "KinshipStudio.GhaithSaadJawad";
    internal const long MaximumBytes = 250L * 1024 * 1024;
    internal static readonly Uri LatestApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    internal static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";
    internal static Version CurrentVersion => Normalize(typeof(AppUpdates).Assembly.GetName().Version!);
    internal static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
    readonly HttpClient _http;

    internal AppUpdates(HttpClient http) { _http = http; }
    internal static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"KinshipStudio/{CurrentVersion}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    internal async Task<AppRelease?> CheckAsync(Version current, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(LatestApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // Metadata is small; reject an unexpectedly large response before parsing it.
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Update information is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyLimited(stream, buffer, 1024 * 1024, null, timeout.Token).ConfigureAwait(false);
        return ParseRelease(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), current);
    }

    internal static AppRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d{1,5}\.\d{1,5}\.\d{1,5}$") || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("The release has an unsupported version number.");
        if (Normalize(version) <= Normalize(current)) return null;
        string name = $"KinshipStudio-{tag}-Setup.exe";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The new release does not yet include its Windows installer. Try again later.");
        var asset = assets[0];
        string expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        if (asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("The installer is not hosted in the official project release.");
        long size = asset.GetProperty("size").GetInt64();
        if (size < 1024 || size > MaximumBytes) throw new InvalidDataException("The installer has an unexpected size.");
        string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("The release does not have a valid SHA-256 digest. Nothing was downloaded.");
        return new(Normalize(version), tag, name, new Uri(expected), size, digest[7..].ToLowerInvariant());
    }

    internal async Task<string> DownloadAsync(AppRelease release, string cacheRoot, IProgress<double>? progress, CancellationToken cancellation)
    {
        // A separate, new directory prevents stale or partial downloads from being reused.
        string directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        string pending = Path.Combine(directory, "download.part");
        string ready = Path.Combine(directory, release.FileName);
        Directory.CreateDirectory(directory);
        try
        {
            using var response = await _http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long advertised && advertised != release.Size)
                throw new InvalidDataException("The download size does not match its release metadata.");
            using (var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                long copied = await CopyLimited(input, output, release.Size, progress, cancellation).ConfigureAwait(false);
                if (copied != release.Size) throw new InvalidDataException("The installer download was incomplete.");
            }
            VerifyFile(pending, release);
            cancellation.ThrowIfCancellationRequested();
            File.Move(pending, ready);
            return ready;
        }
        catch
        {
            // Delete only this operation's own incomplete file, never another cached update.
            if (File.Exists(pending)) File.Delete(pending);
            throw;
        }
    }

    static async Task<long> CopyLimited(Stream input, Stream output, long limit, IProgress<double>? progress, CancellationToken cancellation)
    {
        byte[] bytes = new byte[81920]; long total = 0;
        while (true)
        {
            int count = await input.ReadAsync(bytes, cancellation).ConfigureAwait(false);
            if (count == 0) return total;
            total += count;
            if (total > limit) throw new InvalidDataException("The download exceeded its expected size.");
            await output.WriteAsync(bytes.AsMemory(0, count), cancellation).ConfigureAwait(false);
            progress?.Report((double)total / limit);
        }
    }

    internal static void VerifyFile(string path, AppRelease release)
    {
        using var input = File.OpenRead(path);
        if (input.Length != release.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer failed SHA-256 verification. It will not run.");
    }

    internal static bool IsInstalledCopy(string executable)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RidersToolkit");
        if (directory.Equals(legacy, StringComparison.OrdinalIgnoreCase)) return true;
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerId}_is1");
        return key?.GetValue("InstallLocation") is string installed &&
            directory.TrimEnd(Path.DirectorySeparatorChar).Equals(installed.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    internal static ProcessStartInfo InstallCommand(string installer) => new(installer)
    {
        UseShellExecute = true,
        Arguments = "/SP- /SILENT /NORESTART /UPDATE"
    };
}
