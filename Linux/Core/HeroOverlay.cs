// SPDX-License-Identifier: GPL-3.0-or-later
using ArdysaModsTools.Core.Services;

namespace ArdysaModsTools.Linux;

public sealed class HeroOverlay
{
    public async Task GenerateAsync(string baseVpk, string heroId, string assetPath, string output,
        CancellationToken ct = default, int[]? itemIds = null)
    {
        var hero = (await new HeroCatalog().LoadAsync(ct)).SingleOrDefault(h => h.Id == heroId)
            ?? throw new ArgumentException("Unknown catalog hero.");
        var set = hero.Sets.SingleOrDefault(s => s.AssetPath == assetPath)
            ?? throw new ArgumentException("Unknown catalog asset.");
        if (itemIds is not null && (itemIds.Length == 0 || itemIds.Any(id => !hero.ItemIds.Contains(id) || id == 8366)))
            throw new ArgumentException("Select item IDs belonging to this hero, excluding shared items.");
        if (File.Exists(output)) throw new IOException("Use a new output path.");
        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "amt-overlay-" + Guid.NewGuid().ToString("N"));
        LinuxPaths.EnsureNoLinks(work);
        Directory.CreateDirectory(work);
        try
        {
            var content = Path.Combine(work, "content");
            await new VpkArchive(baseVpk).ExtractAsync(content, ct);
            var zip = Path.Combine(work, "set.zip");
            using var downloader = new AssetDownloader();
            await downloader.DownloadAsync(["https://cdn.ardysamods.my.id/" + assetPath,
                "https://cdn2.ardysamods.my.id/" + assetPath], set.Sha256, zip, ct);
            var selection = Path.Combine(work, "selection");
            await ZipAssets.ExtractAsync(zip, selection, ct);
            var patcher = new HeroSetPatcherService();
            var blocks = patcher.ParseIndexText(await File.ReadAllTextAsync(Path.Combine(selection, "index.txt"), ct),
                hero.Id, itemIds ?? hero.ItemIds.Where(id => id != 8366).ToArray())
                ?? throw new InvalidDataException("No matching hero items in selected set.");
            var warnings = new List<string>();
            if (!File.Exists(Path.Combine(content, "scripts/items/items_game.txt")))
                throw new InvalidDataException("Base pack requires an item table.");
            if (!await patcher.PatchWithMergedBlocksAsync(content, blocks,
                message => { if (message.StartsWith("Warning:", StringComparison.Ordinal)) warnings.Add(message); }, ct)
                || warnings.Count > 0) throw new InvalidDataException("Overlay patch failed: " + string.Join("; ", warnings));
            foreach (var file in Directory.GetFiles(selection, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(selection, file);
                // Preserve the existing pack's shared interface, and never ship recipe metadata.
                if (relative == "index.txt" || relative.StartsWith("panorama/", StringComparison.Ordinal)) continue;
                var target = LinuxPaths.SafeChild(content, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
            await VpkArchive.BuildAsync(content, output, ct);
        }
        finally { Directory.Delete(work, true); }
    }
}
