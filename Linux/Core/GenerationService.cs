// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using ArdysaModsTools.Core.Services;

namespace ArdysaModsTools.Linux;

public sealed record RecipeAsset(string Path, string Sha256, string[] Urls, bool ExtractZip = false);
public sealed record RecipePatch(string HeroId, int[] ItemIds, string IndexPath);
public sealed record GenerationRecipe(string Name, RecipeAsset[] Assets, RecipePatch[] Patches);

/// <summary>Builds a selection from integrity-pinned assets and the current native game's items_game.</summary>
public sealed class GenerationService
{
    public async Task GenerateAsync(string dotaRoot, string recipeFile, string destination, CancellationToken ct = default)
    {
        if (!SteamDetector.Validate(dotaRoot)) throw new IOException("Native Linux Dota installation required.");
        var recipe = JsonSerializer.Deserialize<GenerationRecipe>(await File.ReadAllTextAsync(recipeFile, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid generation recipe.");
        if (recipe.Assets is null || recipe.Patches is null || recipe.Assets.Length > 10000)
            throw new InvalidDataException("Recipe requires bounded assets and patches arrays.");
        if (recipe.Assets.Select(a => a.Path).Distinct(StringComparer.Ordinal).Count() != recipe.Assets.Length)
            throw new InvalidDataException("Duplicate asset destinations.");
        var workspace = Path.Combine(Path.GetTempPath(), "amt-generate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            using var downloader = new AssetDownloader();
            foreach (var asset in recipe.Assets)
            {
                var path = LinuxPaths.SafeChild(workspace, asset.Path);
                if (asset.Path == "scripts/items/items_game.txt")
                    throw new InvalidDataException("items_game must come from the installed game, not a remote asset.");
                await downloader.DownloadAsync(asset.Urls, asset.Sha256, path, ct);
                if (asset.ExtractZip)
                {
                    await ZipAssets.ExtractAsync(path, workspace, ct);
                    File.Delete(path);
                }
            }
            if (recipe.Patches.Length > 0)
            {
                var archive = new VpkArchive(Path.Combine(dotaRoot, "game/dota/pak01_dir.vpk"));
                var entry = archive.Entries.SingleOrDefault(e => e.Path == "scripts/items/items_game.txt")
                    ?? throw new IOException("Current game VPK has no items_game.txt.");
                var itemsGame = LinuxPaths.SafeChild(workspace, "scripts/items/items_game.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(itemsGame)!);
                await using (var output = new FileStream(itemsGame, FileMode.CreateNew))
                    await archive.CopyEntryAsync(entry, output, ct);
                var patcher = new HeroSetPatcherService();
                var blocks = new Dictionary<string, (string block, string heroId)>(StringComparer.Ordinal);
                foreach (var patch in recipe.Patches)
                {
                    if (patch.ItemIds is null || patch.ItemIds.Length == 0 || patch.ItemIds.Any(id => id <= 0))
                        throw new InvalidDataException("A patch requires positive item IDs.");
                    var index = LinuxPaths.SafeChild(workspace, patch.IndexPath);
                    var parsed = patcher.ParseIndexText(await File.ReadAllTextAsync(index, ct), patch.HeroId, patch.ItemIds)
                        ?? throw new InvalidDataException($"Invalid index for hero {patch.HeroId}.");
                    if (patch.ItemIds.Any(id => !parsed.ContainsKey(id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                        throw new InvalidDataException($"The index is missing requested items for hero {patch.HeroId}.");
                    foreach (var pair in parsed)
                        if (!blocks.TryAdd(pair.Key, pair.Value)) throw new InvalidDataException($"Conflicting item {pair.Key}.");
                }
                var warnings = new List<string>();
                if (!await patcher.PatchWithMergedBlocksAsync(workspace, blocks,
                    message => { if (message.StartsWith("Warning:", StringComparison.Ordinal)) warnings.Add(message); }, ct)
                    || warnings.Count > 0)
                    throw new InvalidDataException("Hero item patch failed.");
                // Index assets are metadata, not game content.
                foreach (var index in recipe.Patches.Select(p => p.IndexPath).Distinct())
                    File.Delete(LinuxPaths.SafeChild(workspace, index));
            }
            await VpkArchive.BuildAsync(workspace, destination, ct);
        }
        finally { Directory.Delete(workspace, true); }
    }
}
