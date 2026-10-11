// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using ArdysaModsTools.Core.Services;

namespace ArdysaModsTools.Linux;

public sealed record HeroPackSelection(string Hero, string? Set, string? Error);

/// <summary>One verified compatible set per catalog hero; never installs a partial build silently.</summary>
public sealed class AllHeroesGenerator
{
    public async Task GenerateAsync(string root, string output, string reportFile, Action<string> log,
        CancellationToken ct = default, string? baseVpk = null, string? baseReport = null, string? onlyHero = null, bool localFallback = false)
    {
        if (!SteamDetector.Validate(root)) throw new IOException("Native Dota installation required.");
        if (File.Exists(output) || File.Exists(reportFile)) throw new IOException("Use new output and report paths.");
        var heroes = await new HeroCatalog().LoadAsync(ct);
        var workspace = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "amt-all-" + Guid.NewGuid().ToString("N"));
        LinuxPaths.EnsureNoLinks(workspace);
        Directory.CreateDirectory(workspace);
        var content = Path.Combine(workspace, "content");
        Directory.CreateDirectory(content);
        var selections = new List<HeroPackSelection>();
        var previous = baseVpk is null ? Array.Empty<HeroPackSelection>() :
            JsonSerializer.Deserialize<HeroPackSelection[]>(await File.ReadAllTextAsync(baseReport
                ?? throw new ArgumentException("Base VPK requires a base report."), ct))
                ?? throw new InvalidDataException("Invalid base report.");
        if (baseVpk is not null) await new VpkArchive(baseVpk).ExtractAsync(content, ct);
        var blocks = new Dictionary<string, (string block, string heroId)>(StringComparer.Ordinal);
        var patcher = new HeroSetPatcherService();
        using var downloader = new AssetDownloader();
        try
        {
            foreach (var hero in heroes)
            {
                ct.ThrowIfCancellationRequested();
                var retained = previous.SingleOrDefault(s => s.Hero == hero.Name && s.Set is not null);
                if (retained is not null) { selections.Add(retained); continue; }
                if (localFallback && baseVpk is not null)
                { selections.Add(previous.SingleOrDefault(s => s.Hero == hero.Name) ?? new(hero.Name, null, "No prior selection.")); continue; }
                if (onlyHero is not null && hero.Id != onlyHero)
                { selections.Add(previous.SingleOrDefault(s => s.Hero == hero.Name) ?? new(hero.Name, null, "Not selected for this retry.")); continue; }
                var success = false;
                var errors = new List<string>();
                foreach (var set in hero.Sets)
                {
                    var zip = Path.Combine(workspace, Guid.NewGuid().ToString("N") + ".zip");
                    var extracted = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
                    try
                    {
                        log($"Preparing {hero.Name}: {set.Name}");
                        await downloader.DownloadAsync(["https://cdn.ardysamods.my.id/" + set.AssetPath,
                            "https://cdn2.ardysamods.my.id/" + set.AssetPath], set.Sha256, zip, ct);
                        await ZipAssets.ExtractAsync(zip, extracted, ct);
                        var index = Path.Combine(extracted, "index.txt");
                        if (!File.Exists(index)) throw new InvalidDataException("No root index.txt.");
                        var parsed = patcher.ParseIndexText(await File.ReadAllTextAsync(index, ct), hero.Id, hero.ItemIds)
                            ?? throw new InvalidDataException("No matching default hero items.");
                        if (parsed.Keys.Any(blocks.ContainsKey)) throw new InvalidDataException("Conflicting item IDs.");
                        File.Delete(index);
                        var files = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
                        var omittedFiles = new HashSet<string>();
                        var replacedFiles = new HashSet<string>();
                        foreach (var file in files)
                        {
                            var relative = Path.GetRelativePath(extracted, file);
                            var target = LinuxPaths.SafeChild(content, relative);
                            if (File.Exists(target) && FileTransaction.Hash(file) != FileTransaction.Hash(target))
                            {
                                if (relative.StartsWith("kisilev_ind/particles/logo/", StringComparison.Ordinal))
                                { omittedFiles.Add(file); log("Retaining existing author logo: " + relative); continue; }
                                var ownModel = "models/heroes/" + hero.Id.Replace("npc_dota_hero_", "", StringComparison.Ordinal) + "/";
                                var ownSound = "sounds/weapons/hero/" + hero.Id.Replace("npc_dota_hero_", "", StringComparison.Ordinal) + "/";
                                if (relative.StartsWith(ownSound, StringComparison.Ordinal))
                                { replacedFiles.Add(file); log("Using this hero's sound variant: " + relative); continue; }
                                var reference = relative.EndsWith("_c", StringComparison.Ordinal) ? relative[..^2] : relative;
                                if (relative.StartsWith("models/heroes/", StringComparison.Ordinal)
                                    && !relative.StartsWith(ownModel, StringComparison.Ordinal)
                                    && !parsed.Values.Any(v => v.block.Contains(reference, StringComparison.Ordinal))
                                    && !files.Where(f => f != file).Any(f => System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains(reference, StringComparison.Ordinal)))
                                { omittedFiles.Add(file); log("Skipping unreferenced foreign model: " + relative); continue; }
                                throw new InvalidDataException($"Conflicting asset: {relative}");
                            }
                        }
                        foreach (var file in files)
                        {
                            if (omittedFiles.Contains(file)) continue;
                            var target = LinuxPaths.SafeChild(content, Path.GetRelativePath(extracted, file));
                            if (File.Exists(target) && !replacedFiles.Contains(file)) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            File.Copy(file, target, replacedFiles.Contains(file));
                        }
                        foreach (var pair in parsed) blocks.Add(pair.Key, pair.Value);
                        selections.Add(new(hero.Name, set.Name, null));
                        success = true;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException)
                    { errors.Add(set.Name + ": " + ex.Message); log("Skipping candidate: " + ex.Message); }
                    finally
                    {
                        File.Delete(zip);
                        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
                    }
                }
                if (!success) selections.Add(new(hero.Name, null, errors.Count == 0 ? "No supported ZIP in original catalog." : string.Join("; ", errors)));
            }
            var archive = new VpkArchive(Path.Combine(root, "game/dota/pak01_dir.vpk"));
            var entry = archive.Entries.Single(e => e.Path == "scripts/items/items_game.txt");
            var items = LinuxPaths.SafeChild(content, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(items)!);
            if (baseVpk is null)
                await using (var stream = new FileStream(items, FileMode.CreateNew)) await archive.CopyEntryAsync(entry, stream, ct);
            if (localFallback)
            {
                using var native = new MemoryStream();
                await archive.CopyEntryAsync(entry, native, ct);
                var text = System.Text.Encoding.UTF8.GetString(native.ToArray());
                if (!ArdysaModsTools.Core.Helpers.KeyValuesBlockHelper.FindItemsSectionRange(text, out var start, out var end))
                    throw new InvalidDataException("Cannot locate current native item table.");
                var itemBlocks = ArdysaModsTools.Core.Helpers.KeyValuesBlockHelper.ParseKvBlocks(text[start..end]);
                var models = archive.Entries.Where(e => e.Path.EndsWith(".vmdl_c", StringComparison.Ordinal)).Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
                for (var i = 0; i < selections.Count; i++)
                {
                    if (selections[i].Set is not null) continue;
                    var hero = heroes.Single(h => h.Name == selections[i].Hero);
                    var local = LocalCosmeticsFallback.Select(hero, itemBlocks, models);
                    if (local.Count == 0) continue;
                    foreach (var pair in local) blocks.Add(pair.Key, pair.Value);
                    selections[i] = new(hero.Name, $"Installed game cosmetics ({local.Count} slots)", null);
                    log($"Using installed game cosmetics for {hero.Name}: {local.Count} slots");
                }
            }
            var warnings = new List<string>();
            if (!await patcher.PatchWithMergedBlocksAsync(content, blocks,
                message => { if (message.StartsWith("Warning:", StringComparison.Ordinal)) warnings.Add(message); }, ct)
                || warnings.Count > 0) throw new InvalidDataException("Combined patch failed: " + string.Join("; ", warnings));
            await VpkArchive.BuildAsync(content, output, ct);
            await File.WriteAllTextAsync(reportFile, JsonSerializer.Serialize(selections, new JsonSerializerOptions { WriteIndented = true }), ct);
            log($"Generated {selections.Count(s => s.Set is not null)}/{heroes.Length} heroes; inspect {reportFile} before installation.");
        }
        finally { Directory.Delete(workspace, true); }
    }
}
