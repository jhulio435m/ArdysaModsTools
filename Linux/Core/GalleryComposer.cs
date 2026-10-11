// SPDX-License-Identifier: GPL-3.0-or-later
using ArdysaModsTools.Core.Helpers;
using ArdysaModsTools.Core.Services;
using System.Text.RegularExpressions;

namespace ArdysaModsTools.Linux;

public sealed class GalleryComposer
{
    private readonly string cache;
    public GalleryComposer(string? cache = null) => this.cache = cache ?? Path.Combine(new LinuxPaths().Cache, "gallery");
    private async Task<string> Layer(CatalogSet set, CancellationToken ct)
    {
        var folder = Path.Combine(cache, set.Sha256.ToLowerInvariant());
        LinuxPaths.EnsureNoLinks(folder);
        if (File.Exists(Path.Combine(folder, ".complete"))) return folder;
        Directory.CreateDirectory(cache);
        var zip = folder + ".zip";
        if (!File.Exists(zip))
        {
            using var downloader = new AssetDownloader();
            await downloader.DownloadAsync(["https://cdn.ardysamods.my.id/" + set.AssetPath,
                "https://cdn2.ardysamods.my.id/" + set.AssetPath], set.Sha256, zip, ct);
        }
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        try { await ZipAssets.ExtractAsync(zip, folder, ct); await File.WriteAllTextAsync(Path.Combine(folder, ".complete"), set.AssetPath, ct); }
        catch { if (Directory.Exists(folder)) Directory.Delete(folder, true); throw; }
        return folder;
    }
    public async Task GenerateAsync(string dotaRoot, GalleryStore store, GalleryState state,
        Dictionary<string, GallerySelection> selections, CatalogHero[] catalog, string output,
        Action<string> progress, CancellationToken ct = default)
    {
        GalleryStore.Validate(selections, catalog);
        if (File.Exists(output)) throw new IOException("Output already exists.");
        var work = Path.Combine(store.DirectoryPath, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var content = Path.Combine(work, "content");
            if (File.Exists(store.BaselineVpk)) await new VpkArchive(store.BaselineVpk).ExtractAsync(content, ct);
            else Directory.CreateDirectory(content);
            var native = new VpkArchive(Path.Combine(dotaRoot, "game/dota/pak01_dir.vpk"));
            var nativeEntries = native.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
            var nativeTextFile = Path.Combine(work, "native-items.txt");
            await native.ExtractEntryAsync("scripts/items/items_game.txt", nativeTextFile, ct);
            var nativeText = await File.ReadAllTextAsync(nativeTextFile, ct);
            var nativeItems = ItemsGameBlockIndex.IndexSpans(nativeText).ToDictionary(kv => kv.Key, kv => nativeText.Substring(kv.Value.Start, kv.Value.Length));
            var itemPath = Path.Combine(content, "scripts/items/items_game.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(itemPath)!);
            if (!File.Exists(itemPath)) await File.WriteAllTextAsync(itemPath, nativeText, ct);
            var baselineText = await File.ReadAllTextAsync(itemPath, ct);
            var baselineSpans = ItemsGameBlockIndex.IndexSpans(baselineText);
            var resourceOwners = new Dictionary<string, HashSet<string>>();
            foreach (var h in catalog)
                foreach (var itemId in h.ItemIds.Where(n => n != 8366))
                    if (baselineSpans.TryGetValue(itemId.ToString(), out var span))
                        foreach (Match reference in Regex.Matches(baselineText.Substring(span.Start, span.Length), @"""([^""]+\.(?:vmdl|vpcf|vmat|vtex|vsndevts|vsnd))"""))
                        {
                            var resource = reference.Groups[1].Value + "_c";
                            if (!resourceOwners.TryGetValue(resource, out var owners)) resourceOwners[resource] = owners = [];
                            owners.Add(h.Id);
                        }
            var allBlocks = new Dictionary<string, (string block, string heroId)>();
            var fileOwners = new Dictionary<string, string>();
            var changed = state.Baseline.Keys.Union(selections.Keys).Where(id =>
                !GalleryStore.Same(state.Baseline.GetValueOrDefault(id), selections.GetValueOrDefault(id))).ToArray();
            foreach (var id in changed)
            {
                ct.ThrowIfCancellationRequested();
                var hero = catalog.Single(h => h.Id == id);
                progress("Preparando " + hero.Name);
                var selected = selections.GetValueOrDefault(id) ?? new GallerySelection();
                var previous = state.Baseline.GetValueOrDefault(id) ?? new GallerySelection();
                var previousLayers = new List<string>();
                foreach (var asset in previous.Assets)
                    previousLayers.Add(await Layer(hero.Sets.Single(s => s.AssetPath == asset), ct));
                // Restore baseline resources of this hero before composing its new layers.
                var previousBaseStem = previous.Base is null ? null : Path.GetFileNameWithoutExtension(previous.Base);
                var previousFiles = previousLayers.SelectMany(folder => Files(folder).Concat(previousBaseStem is not null
                    && Directory.Exists(Path.Combine(folder, "variants", previousBaseStem)) ? Files(Path.Combine(folder, "variants", previousBaseStem)) : []));
                foreach (var relative in previousFiles.Distinct())
                {
                    var target = LinuxPaths.SafeChild(content, relative);
                    if (resourceOwners.TryGetValue(relative, out var sharedOwners) && sharedOwners.Any(owner => owner != id)) continue;
                    if (File.Exists(target)) File.Delete(target);
                    if (nativeEntries.ContainsKey(relative))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await native.ExtractEntryAsync(relative, target, ct);
                    }
                }
                var defaults = hero.ItemIds.Where(n => n != 8366).Select(n => n.ToString()).ToArray();
                foreach (var key in defaults)
                    if (nativeItems.TryGetValue(key, out var block)) allBlocks[key] = (block, id);
                var layers = new List<(CatalogSet Set, string Folder)>();
                foreach (var asset in selected.Assets)
                {
                    var set = hero.Sets.Single(s => s.AssetPath == asset);
                    progress(hero.Name + " · " + (set.DisplayName ?? set.Name));
                    layers.Add((set, await Layer(set, ct)));
                }
                var composed = Compose(hero, layers, nativeItems);
                foreach (var (key, value) in composed.Blocks) allBlocks[key] = (value, id);
                foreach (var block in composed.Blocks.Values)
                {
                    var model = Value(block, "model_player");
                    if (model.Length > 0 && !nativeEntries.ContainsKey(model + "_c") && !File.Exists(LinuxPaths.SafeChild(content, model + "_c")) && !composed.Files.ContainsKey(model + "_c"))
                        throw new InvalidDataException(hero.Name + ": falta el modelo " + model);
                }
                foreach (var (relative, source) in composed.Files)
                {
                    if (fileOwners.TryGetValue(relative, out var owner) && owner != id)
                        throw new InvalidDataException("Conflicto entre héroes: " + relative);
                    if (resourceOwners.TryGetValue(relative, out var sharedOwners) && sharedOwners.Any(owner => owner != id)
                        && File.Exists(LinuxPaths.SafeChild(content, relative))
                        && !File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(LinuxPaths.SafeChild(content, relative))))
                        throw new InvalidDataException("El recurso también se usa en otro héroe: " + relative);
                    fileOwners[relative] = id;
                    var target = LinuxPaths.SafeChild(content, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, true);
                }
            }
            var warnings = new List<string>();
            if (allBlocks.Count > 0 && (!await new HeroSetPatcherService().PatchWithMergedBlocksAsync(content, allBlocks,
                message => { if (message.StartsWith("Warning:")) warnings.Add(message); }, ct) || warnings.Count > 0))
                throw new InvalidDataException("No se pudo combinar la tabla de objetos: " + string.Join("; ", warnings));
            progress("Creando y verificando paquete");
            await VpkArchive.BuildAsync(content, output, ct);
            await new VpkArchive(output).VerifyAsync(ct);
        }
        finally { Directory.Delete(work, true); }
    }
    public static IEnumerable<string> Files(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(folder, file)).Where(path => path != "index.txt" && path != ".complete"
            && !path.StartsWith("panorama/") && !path.StartsWith("variants/"));

    public sealed record Composition(Dictionary<string, string> Blocks, Dictionary<string, string> Files);
    public static Composition Compose(CatalogHero hero, IReadOnlyList<(CatalogSet Set, string Folder)> layers,
        IReadOnlyDictionary<string, string> nativeItems)
    {
        var blocks = new Dictionary<string, string>();
        var files = new Dictionary<string, string>();
        var owners = new Dictionary<string, CatalogSet>();
        var baseLayer = layers.SingleOrDefault(l => l.Set.Category == "basehero");
        var hasBase = baseLayer != default;
        var detectedBase = hasBase && File.Exists(Path.Combine(baseLayer.Folder, "index.txt"))
            && File.ReadAllText(Path.Combine(baseLayer.Folder, "index.txt")).Contains("\"hero_base\"");
        int Weight(CatalogSet set)
        {
            var method = hero.SetMethods?.GetValueOrDefault(set.Key ?? "", hero.Method ?? (detectedBase ? 1 : 2)) ?? hero.Method ?? (detectedBase ? 1 : 2);
            var baseWins = method == 1 || (method is not (1 or 2) && detectedBase);
            return set.Category switch { "basehero" => 100, "item" => baseWins ? 80 : 110,
                "prismatic" => 0, "abilityeffect" => -10, _ => baseWins ? 90 : 120 };
        }
        foreach (var (set, folder) in layers.OrderByDescending(l => Weight(l.Set)))
        {
            var index = Path.Combine(folder, "index.txt");
            if (set.Category is not ("prismatic" or "abilityeffect"))
            {
                if (!File.Exists(index)) throw new InvalidDataException("El paquete no contiene index.txt: " + set.Name);
                var parsed = new HeroSetPatcherService().ParseIndexText(File.ReadAllText(index), hero.Id, hero.ItemIds.Where(n => n != 8366).ToArray());
                if (parsed is null || parsed.Count == 0) throw new InvalidDataException("No hay objetos compatibles: " + set.Name);
                foreach (var (id, value) in parsed) { blocks[id] = value.block; owners[id] = set; }
            }
            foreach (var path in Files(folder))
            {
                if (set.Category == "abilityeffect" && !(path.EndsWith(".vpcf_c") || path.EndsWith(".vmat_c") || path.EndsWith(".vtex_c") || path.EndsWith(".vsnd_c") || path.EndsWith(".vsndevts_c")))
                    throw new InvalidDataException("El efecto contiene recursos de otra categoría: " + path);
                files[path] = LinuxPaths.SafeChild(folder, path);
            }
        }
        // Apply base-specific compiled wearable variants, as in the original body ownership pass.
        if (hasBase)
        {
            var stem = Path.GetFileNameWithoutExtension(baseLayer.Set.AssetPath);
            // The body must belong to the selected base; worn models belong to their selected slots.
            var worn = blocks.Values.Select(b => Value(b, "model_player") + "_c").ToHashSet();
            foreach (var path in Files(baseLayer.Folder).Where(p => p.EndsWith(".vmdl_c") && !worn.Contains(p)))
                if (path.StartsWith("models/heroes/")) files[path] = LinuxPaths.SafeChild(baseLayer.Folder, path);
            foreach (var layer in layers)
            {
                var variant = Path.Combine(layer.Folder, "variants", stem);
                if (!Directory.Exists(variant)) continue;
                foreach (var path in Files(variant))
                {
                    var selectedItemOwnsFile = blocks.Any(kv => Value(kv.Value, "model_player") + "_c" == path
                        && owners.TryGetValue(kv.Key, out var owner) && owner.Category == "item" && owner != layer.Set);
                    if (!selectedItemOwnsFile) files[path] = LinuxPaths.SafeChild(variant, path);
                }
            }
            foreach (var lockedSlot in baseLayer.Set.Locks ?? [])
            {
                var parsed = new HeroSetPatcherService().ParseIndexText(File.ReadAllText(Path.Combine(baseLayer.Folder, "index.txt")), hero.Id, hero.ItemIds);
                foreach (var (id, original) in nativeItems.Where(kv => hero.ItemIds.Contains(int.Parse(kv.Key)) && lockedSlot.Split('_').Contains(Value(kv.Value, "item_slot"))))
                    if (parsed is not null && parsed.TryGetValue(id, out var locked))
                    {
                        blocks[id] = locked.block;
                        foreach (var model in new[] { Value(original, "model_player"), Value(locked.block, "model_player") }.Where(v => v.Length > 0))
                            if (File.Exists(Path.Combine(baseLayer.Folder, model + "_c"))) files[model + "_c"] = LinuxPaths.SafeChild(baseLayer.Folder, model + "_c");
                    }
            }
            // Planetfall's post-refit head is a separate native wearable, absent from old indices.
            if (hero.Id == "npc_dota_hero_earthshaker" && stem.StartsWith("base_arcana")
                && layers.Any(l => Path.GetFileName(l.Set.AssetPath) == "arcana.zip") && nativeItems.TryGetValue("34382", out var head))
            {
                head = Regex.Replace(head, "\"34382\"", "\"8655\"", RegexOptions.None, TimeSpan.FromSeconds(1));
                head = Regex.Replace(head, "\"prefab\"\\s*\"wearable\"", "\"prefab\"\t\t\"default_item\"");
                blocks["8655"] = head;
            }
        }
        return new(blocks, files);
    }
    private static string Value(string block, string key) => KeyValuesBlockHelper.TryGetTopLevelValue(block, key, out var value) ? value ?? "" : "";
}
