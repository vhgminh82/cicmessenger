using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CICMessenger.UI.Services;

/// <summary>
/// Differential updater. The app ships as a folder of files where only a couple of MB of
/// app assemblies change between releases; the ~137MB .NET/Avalonia runtime is stable. So
/// instead of re-downloading one giant exe, this compares a per-file manifest (with SHA256
/// hashes) against the local folder and downloads only the files that actually differ.
///
/// A running exe/dll can't overwrite itself, so downloaded files are staged in a temp
/// folder and a helper .cmd copies them into place after the app exits, then relaunches.
/// </summary>
public class UpdateService
{
    public const string GitHubRepo = "vhgminh82/cicmessenger";

    static readonly HttpClient http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CICMessenger", CurrentVersion.ToString()));
        client.Timeout = TimeSpan.FromMinutes(5); // a full-zip fallback can be ~60MB
        return client;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Version as shown to users, e.g. "0.11". Each release steps Minor by one.</summary>
    public static string DisplayVersion => $"{CurrentVersion.Major}.{CurrentVersion.Minor}";

    public record UpdateInfo(Version Version, string TagName);

    sealed record ManifestEntry(string Path, string Sha256, long Size);
    sealed record ReleaseAsset(string Name, string Url);

    /// <summary>
    /// Returns info about a newer release, or null when already up to date. Only reads the
    /// release tag, so it is cheap enough to run quietly at startup for the badge.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

        var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // no releases yet — nothing to update to

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(NormalizeVersion(versionText), out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        return new UpdateInfo(latest, tag);
    }

    // "0.2" -> "0.2.0" so Version.TryParse accepts it
    static string NormalizeVersion(string text) => text.Count(c => c == '.') switch
    {
        0 => text + ".0.0",
        1 => text + ".0",
        _ => text
    };

    /// <summary>Progress callback: (message, 0-100 or -1 for indeterminate).</summary>
    public Action<string, int>? Progress { get; set; }

    /// <summary>
    /// Downloads only the files that differ from the installed version and stages a helper
    /// that swaps them in after exit. Falls back to the full zip when a runtime file changed
    /// (rare — only on a .NET SDK bump) since those aren't published as individual files.
    /// </summary>
    public async Task DownloadAndApplyAsync(UpdateInfo update)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine current executable path.");

        var assets = await GetReleaseAssetsAsync(update.TagName);

        var manifestAsset = assets.FirstOrDefault(a => a.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestAsset == null)
        {
            // Old-style release with just an exe — replace the exe wholesale
            await ApplyLegacyExeAsync(assets, currentExe);
            return;
        }

        Progress?.Invoke("Đang đọc danh sách file...", -1);
        var manifest = await GetManifestAsync(manifestAsset.Url);

        Progress?.Invoke("Đang so sánh với bản đang cài...", -1);
        var differing = FindDifferingFiles(appDir, manifest);

        if (differing.Count == 0)
            return; // nothing actually changed

        // Small update only if every changed file is a root-level file published individually
        bool canDoSmallUpdate = differing.All(e =>
            !e.Path.Contains('/') && !e.Path.Contains('\\') &&
            assets.Any(a => a.Name.Equals(e.Path, StringComparison.OrdinalIgnoreCase)));

        var staging = Path.Combine(Path.GetTempPath(), "CICMessenger-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        if (canDoSmallUpdate)
            await ApplySmallUpdateAsync(differing, assets, appDir, staging, currentExe);
        else
            await ApplyFullUpdateAsync(assets, appDir, staging, currentExe);
    }

    async Task<List<ReleaseAsset>> GetReleaseAssetsAsync(string tag)
    {
        var url = $"https://api.github.com/repos/{GitHubRepo}/releases/tags/{tag}";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var list = new List<ReleaseAsset>();
        if (doc.RootElement.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                var dl = a.GetProperty("browser_download_url").GetString();
                if (name != null && dl != null)
                    list.Add(new ReleaseAsset(name, dl));
            }
        return list;
    }

    async Task<List<ManifestEntry>> GetManifestAsync(string url)
    {
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var files = new List<ManifestEntry>();
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
            files.Add(new ManifestEntry(
                f.GetProperty("path").GetString() ?? "",
                f.GetProperty("sha256").GetString() ?? "",
                f.GetProperty("size").GetInt64()));
        return files;
    }

    static List<ManifestEntry> FindDifferingFiles(string appDir, List<ManifestEntry> manifest)
    {
        var result = new List<ManifestEntry>();
        foreach (var entry in manifest)
        {
            var local = Path.Combine(appDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local) || !HashFile(local).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                result.Add(entry);
        }
        return result;
    }

    static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    async Task ApplySmallUpdateAsync(List<ManifestEntry> differing, List<ReleaseAsset> assets,
                                     string appDir, string staging, string currentExe)
    {
        var copyPairs = new List<(string from, string to)>();
        int done = 0;

        foreach (var entry in differing)
        {
            var asset = assets.First(a => a.Name.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
            Progress?.Invoke($"Đang tải {entry.Path} ({++done}/{differing.Count})...",
                (int)(done / (double)differing.Count * 100));

            var stagedFile = Path.Combine(staging, entry.Path);
            var bytes = await http.GetByteArrayAsync(asset.Url);
            await File.WriteAllBytesAsync(stagedFile, bytes);

            copyPairs.Add((stagedFile, Path.Combine(appDir, entry.Path)));
        }

        LaunchSwapScript(copyPairs, staging, currentExe);
    }

    async Task ApplyFullUpdateAsync(List<ReleaseAsset> assets, string appDir, string staging, string currentExe)
    {
        var zip = assets.FirstOrDefault(a => a.Name.EndsWith("-full.zip", StringComparison.OrdinalIgnoreCase))
                  ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException("No full package found in the release.");

        Progress?.Invoke("Đang tải bản đầy đủ...", -1);
        var zipPath = Path.Combine(staging, "full.zip");
        var bytes = await http.GetByteArrayAsync(zip.Url);
        await File.WriteAllBytesAsync(zipPath, bytes);

        Progress?.Invoke("Đang giải nén...", -1);
        var extractDir = Path.Combine(staging, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // The zip may wrap everything in a single top folder — unwrap it
        var roots = Directory.GetFileSystemEntries(extractDir);
        var sourceDir = roots.Length == 1 && Directory.Exists(roots[0]) ? roots[0] : extractDir;

        LaunchFolderSyncScript(sourceDir, appDir, staging, currentExe);
    }

    async Task ApplyLegacyExeAsync(List<ReleaseAsset> assets, string currentExe)
    {
        var exe = assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException("No exe in the release.");
        var staging = Path.Combine(Path.GetTempPath(), "CICMessenger-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var staged = Path.Combine(staging, Path.GetFileName(currentExe));
        var bytes = await http.GetByteArrayAsync(exe.Url);
        await File.WriteAllBytesAsync(staged, bytes);
        LaunchSwapScript(new() { (staged, currentExe) }, staging, currentExe);
    }

    /// <summary>Writes and launches a .cmd that copies each staged file into place, then restarts.</summary>
    void LaunchSwapScript(List<(string from, string to)> copyPairs, string staging, string currentExe)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        foreach (var (from, to) in copyPairs)
        {
            // Retry each copy — the file may still be locked for a moment after exit
            sb.AppendLine($":retry_{Math.Abs(to.GetHashCode())}");
            sb.AppendLine($"copy /y \"{from}\" \"{to}\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  timeout /t 1 /nobreak >nul");
            sb.AppendLine($"  goto retry_{Math.Abs(to.GetHashCode())}");
            sb.AppendLine(")");
        }
        sb.AppendLine($"start \"\" \"{currentExe}\"");
        sb.AppendLine($"rmdir /s /q \"{staging}\" >nul 2>&1");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");

        RunScript(sb.ToString(), staging);
    }

    /// <summary>Writes and launches a .cmd that mirrors the extracted folder over the app folder.</summary>
    void LaunchFolderSyncScript(string sourceDir, string appDir, string staging, string currentExe)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        sb.AppendLine(":retry");
        // /E all subdirs, /R retries, /W wait — robocopy is built into Windows
        sb.AppendLine($"robocopy \"{sourceDir}\" \"{appDir}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP >nul");
        // robocopy exit codes < 8 mean success
        sb.AppendLine("if errorlevel 8 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto retry");
        sb.AppendLine(")");
        sb.AppendLine($"start \"\" \"{currentExe}\"");
        sb.AppendLine($"rmdir /s /q \"{staging}\" >nul 2>&1");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");

        RunScript(sb.ToString(), staging);
    }

    void RunScript(string script, string staging)
    {
        var scriptPath = Path.Combine(staging, "apply-update.cmd");
        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
