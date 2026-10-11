// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace ArdysaModsTools.Linux;

internal static class LinuxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault() ?? "serve";
        if (command is "help" or "--help" or "-h") { Console.WriteLine(Help); return 0; }
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Use named --options.\n" + Help);
            var value = key is "--experimental" or "--no-browser" or "--local-fallback" ? "true"
                : ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {key}.");
            if (!options.TryAdd(key, value)) throw new ArgumentException($"Duplicate option {key}.");
        }
        string[] allowed = command switch
        {
            "serve" => ["--session-file", "--no-browser"],
            "detect" => Array.Empty<string>(),
            "status" or "disable" or "recover" => ["--root"],
            "install" => ["--root", "--input", "--experimental"],
            "reapply" => ["--root", "--experimental"],
            "vpk-list" or "vpk-verify" => ["--input"],
            "vpk-extract" => ["--input", "--output"],
            "vpk-extract-entry" => ["--input", "--entry", "--output"],
            "vpk-build" => ["--source", "--output"],
            "generate" => ["--root", "--recipe", "--output"],
            "hero-overlay" => ["--base", "--hero", "--asset", "--output", "--items"],
            "generate-all" => ["--root", "--output", "--report", "--base", "--base-report", "--only", "--local-fallback"],
            "download" => ["--url", "--sha256", "--output"],
            _ => throw new ArgumentException("Unknown command.\n" + Help)
        };
        if (options.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Unknown option for this command.");
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Required: {key}");
        var paths = new LinuxPaths();
        var preferences = new PreferenceStore(paths);
        string Root() => options.GetValueOrDefault("--root") ?? preferences.Load().DotaRoot
            ?? new SteamDetector().Detect().Select(i => i.Root).SingleOrDefault()
            ?? throw new IOException("Select a Dota installation with --root.");
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var ct = cancellation.Token;
        var manager = new ModManager();
        switch (command)
        {
            case "serve": await LocalHost.RunAsync(preferences, ct,
                options.GetValueOrDefault("--session-file"), options.ContainsKey("--no-browser")); break;
            case "detect": Print(new SteamDetector().Detect()); break;
            case "status": Print(manager.Status(Root())); break;
            case "vpk-list":
                Print(new VpkArchive(Required("--input")).Entries.Select(e => new { e.Path, e.Length, PreloadBytes = e.Preload.Length, e.Archive })); break;
            case "vpk-verify": await new VpkArchive(Required("--input")).VerifyAsync(ct); break;
            case "vpk-extract": await new VpkArchive(Required("--input")).ExtractAsync(Required("--output"), ct); break;
            case "vpk-extract-entry": await new VpkArchive(Required("--input")).ExtractEntryAsync(Required("--entry"), Required("--output"), ct); break;
            case "vpk-build": await VpkArchive.BuildAsync(Required("--source"), Required("--output"), ct); break;
            case "generate": await new GenerationService().GenerateAsync(Root(), Required("--recipe"), Required("--output"), ct); break;
            case "hero-overlay": await new HeroOverlay().GenerateAsync(Required("--base"), Required("--hero"), Required("--asset"), Required("--output"), ct,
                options.TryGetValue("--items", out var itemList) ? itemList.Split(',').Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : null); break;
            case "generate-all": await new AllHeroesGenerator().GenerateAsync(Root(), Required("--output"), Required("--report"), Console.WriteLine, ct,
                options.GetValueOrDefault("--base"), options.GetValueOrDefault("--base-report"), options.GetValueOrDefault("--only"), options.ContainsKey("--local-fallback")); break;
            case "download":
                using (var downloader = new AssetDownloader())
                    await downloader.DownloadAsync([Required("--url")], Required("--sha256"), Required("--output"), ct);
                break;
            case "install": await manager.InstallAsync(Root(), Required("--input"), options.ContainsKey("--experimental"), ct); break;
            case "disable": await manager.DisableAsync(Root(), ct); break;
            case "reapply": await manager.ReapplyAsync(Root(), options.ContainsKey("--experimental"), ct); break;
            case "recover": FileTransaction.Recover(Root()); break;
        }
        return 0;
    }
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));
    private const string Help = """
        ArdysaModsTools Linux — experimental source port
        serve                                   Open the local React interface (default)
          [--session-file PATH --no-browser]    Private bootstrap link for local UI testing
        detect                                  List native Linux Steam installations
        status [--root PATH]                    Inspect installed mods and patch drift
        vpk-list --input FILE                   List VPK v1/v2 directory entries
        vpk-verify --input FILE                 Verify every entry's CRC (all chunks required)
        vpk-extract --input FILE --output DIR    Extract into an empty directory
        vpk-extract-entry --input FILE --entry RELATIVE --output FILE
        vpk-build --source DIR --output FILE    Build a standalone VPK v1 (less than 4 GiB)
        download --url HTTPS --sha256 HASH --output FILE
        generate --recipe JSON --output FILE [--root PATH]
        hero-overlay --base VPK --hero ID --asset CATALOG_PATH --output VPK [--items ID,ID]
        generate-all --output FILE --report JSON [--root PATH]
        install --input VPK --experimental [--root PATH]
        disable [--root PATH]                   Restore managed gameinfo and remove Linux mod VPK
        reapply --experimental [--root PATH]    Rebase the mount onto a Steam-updated gameinfo
        recover [--root PATH]                   Recover an interrupted file transaction

        No Windows signature files are modified. Native game mounting remains unverified.
        """;
}
