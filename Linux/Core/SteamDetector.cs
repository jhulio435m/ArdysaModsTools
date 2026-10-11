// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ArdysaModsTools.Linux;

public sealed record DotaInstallation(string Root, string Library, string? BuildId);

public sealed class SteamDetector(string? home = null)
{
    private readonly string homePath = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public IReadOnlyList<DotaInstallation> Detect()
    {
        var libraries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in new[] { ".local/share/Steam", ".steam/steam", ".steam/root",
                     ".var/app/com.valvesoftware.Steam/.local/share/Steam" })
        {
            var steam = Path.Combine(homePath, root);
            if (!Directory.Exists(steam)) continue;
            steam = Canonical(steam);
            libraries.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"((?:\\\\.|[^\"\\\\])*)\""))
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                if (Path.IsPathFullyQualified(path) && Directory.Exists(path)) libraries.Add(Canonical(path));
            }
        }
        var result = new List<DotaInstallation>();
        foreach (var library in libraries.Order(StringComparer.Ordinal))
        {
            var manifest = Path.Combine(library, "steamapps", "appmanifest_570.acf");
            var content = File.Exists(manifest) ? File.ReadAllText(manifest) : "";
            var folder = Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"");
            var name = folder.Success ? folder.Groups[1].Value : "dota 2 beta";
            if (Path.GetFileName(name) != name || name is "." or "..") continue;
            var root = Path.Combine(library, "steamapps", "common", name);
            if (Validate(root)) result.Add(new(Canonical(root), library, BuildId(root)));
        }
        return result.DistinctBy(x => x.Root).ToArray();
    }
    public static bool Validate(string root) => Path.IsPathFullyQualified(root)
        && File.Exists(Path.Combine(root, "game/bin/linuxsteamrt64/dota2"))
        && File.Exists(Path.Combine(root, "game/dota/pak01_dir.vpk"))
        && File.Exists(Path.Combine(root, "game/dota/gameinfo.gi"));
    public static string? BuildId(string root)
    {
        var steamapps = Directory.GetParent(root)?.Parent?.FullName;
        if (steamapps is null) return null;
        var file = Path.Combine(steamapps, "appmanifest_570.acf");
        if (!File.Exists(file)) return null;
        var match = Regex.Match(File.ReadAllText(file), "\"buildid\"\\s+\"([0-9]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
    public static bool GameRunning()
    {
        foreach (var process in Process.GetProcessesByName("dota2"))
        {
            using (process) if (!process.HasExited) return true;
        }
        return false;
    }
    public static bool SteamUpdateInProgress(string root)
    {
        var steamapps = Directory.GetParent(root)?.Parent?.FullName;
        if (steamapps is null) return false;
        var file = Path.Combine(steamapps, "appmanifest_570.acf");
        if (!File.Exists(file)) return false;
        var content = File.ReadAllText(file);
        var flags = Regex.Match(content, "\"StateFlags\"\\s+\"([0-9]+)\"");
        // Only the stable FullyInstalled state is accepted for mutations.
        return flags.Success && flags.Groups[1].Value != "4";
    }
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new DirectoryInfo(full);
        return info.ResolveLinkTarget(true)?.FullName ?? full;
    }
}
