using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace CICMessenger.UI.Services;

/// <summary>
/// Checks GitHub Releases of the CICMessenger repo for a newer version and,
/// when found, downloads the portable exe and swaps it in via a helper script
/// (the running exe can't overwrite itself, so a cmd script does the copy
/// after this process exits, then relaunches the app).
/// </summary>
public class UpdateService
{
    // owner/repo on GitHub that hosts CICMessenger releases
    public const string GitHubRepo = "vhgminh82/cicmessenger";

    static readonly HttpClient http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CICMessenger", CurrentVersion.ToString()));
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public record UpdateInfo(Version Version, string TagName, string DownloadUrl);

    /// <summary>
    /// Returns info about a newer release, or null when already up to date.
    /// Throws on network/API failure so the caller can show an error message.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

        var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Repo exists but has no releases yet, or the repo isn't published — either way
            // there is nothing to update to, which is not a failure worth alarming about.
            return null;
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(NormalizeVersion(versionText), out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        string? downloadUrl = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets))
        {
            downloadUrl = assets.EnumerateArray()
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault(u => u != null && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }

        if (downloadUrl == null)
            return null;

        return new UpdateInfo(latest, tag, downloadUrl);
    }

    // "0.2" -> "0.2.0" so Version.TryParse accepts it
    static string NormalizeVersion(string text)
    {
        return text.Count(c => c == '.') switch
        {
            0 => text + ".0.0",
            1 => text + ".0",
            _ => text
        };
    }

    /// <summary>
    /// Downloads the new exe and hands off to a cmd script that replaces the
    /// current exe once this process exits, then restarts the app.
    /// Returns the path of the downloaded file.
    /// </summary>
    public async Task DownloadAndApplyAsync(UpdateInfo update)
    {
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine current executable path.");

        var tempDir = Path.Combine(Path.GetTempPath(), "CICMessenger-update");
        Directory.CreateDirectory(tempDir);
        var newExePath = Path.Combine(tempDir, "CICMessenger-new.exe");

        var bytes = await http.GetByteArrayAsync(update.DownloadUrl);
        await File.WriteAllBytesAsync(newExePath, bytes);

        var scriptPath = Path.Combine(tempDir, "apply-update.cmd");
        var script = $"""
            @echo off
            rem Wait for the app to exit, then swap in the new exe and restart
            timeout /t 2 /nobreak >nul
            :retry
            copy /y "{newExePath}" "{currentExe}" >nul 2>&1
            if errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto retry
            )
            start "" "{currentExe}"
            del "{newExePath}" >nul 2>&1
            (goto) 2>nul & del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
