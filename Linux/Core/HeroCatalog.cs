// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace ArdysaModsTools.Linux;

public sealed record CatalogSet(string Name, string AssetPath, string Sha256, string? DisplayName = null, string Category = "legacyset", string? Tag = null, string? ThumbnailUrl = null, string? StyleGroup = null, string? StyleLabel = null, string? Key = null, string[]? Locks = null);
public sealed record CatalogHero(string Id, string Name, int[] ItemIds, CatalogSet[] Sets, string Attribute = "universal", int? Method = null, Dictionary<string, int>? SetMethods = null, string[][]? Incompatible = null);

/// <summary>Reads the original public catalog; downloads stay pinned to its SHA-256 manifest.</summary>
public sealed class HeroCatalog
{
    private const string Base = "https://cdn.ardysamods.my.id/";
    private CatalogHero[]? heroes;
    public async Task<CatalogHero[]> LoadAsync(CancellationToken ct = default)
    {
        if (heroes is not null) return heroes;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(45) };
        async Task<JsonDocument> Read(string path)
        {
            using var response = await client.GetAsync(Base + path, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(bytes, ct)) > 0)
            {
                if (buffer.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Catalog too large.");
                buffer.Write(bytes, 0, count);
            }
            return JsonDocument.Parse(buffer.ToArray());
        }
        using var catalog = await Read("Assets/heroes.json");
        using var manifest = await Read("Assets/asset_hashes.json");
        var hashes = manifest.RootElement.GetProperty("assets");
        var result = new List<CatalogHero>();
        foreach (var hero in catalog.RootElement.EnumerateArray())
        {
            var sets = new List<CatalogSet>();
            var candidates = new List<(string Name, JsonElement Value)>();
            foreach (var set in hero.GetProperty("sets").EnumerateObject())
            {
                if (set.Value.ValueKind == JsonValueKind.Array) candidates.Add((set.Name, set.Value));
                else if (set.Value.ValueKind == JsonValueKind.Object && set.Value.TryGetProperty("styles", out var styles))
                    foreach (var style in styles.EnumerateObject())
                        if (style.Value.ValueKind == JsonValueKind.Array) candidates.Add((set.Name + " / " + style.Name, style.Value));
            }
            foreach (var set in candidates)
            {
                var url = set.Value.EnumerateArray().Select(v => v.GetString())
                    .FirstOrDefault(v => v is not null && v.StartsWith(Base, StringComparison.Ordinal)
                        && v.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                if (url is null) continue; // Default skins and split archives need separate handling.
                var path = url[Base.Length..];
                _ = LinuxPaths.SafeChild(Path.GetTempPath(), path);
                if (!hashes.TryGetProperty(path, out var entry)) continue;
                var hash = entry.GetProperty("sha256").GetString() ?? "";
                if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) continue;
                var stem = Path.GetFileNameWithoutExtension(path);
                var category = stem.StartsWith("effect_") ? "abilityeffect" : stem.StartsWith("base_") ? "basehero"
                    : stem.StartsWith("item_") ? "item" : stem.StartsWith("prismatic_") ? "prismatic"
                    : stem.StartsWith("persona") || path.Contains("/persona/") ? "persona" : stem.StartsWith("mix_") ? "customset" : "legacyset";
                var display = hero.TryGetProperty("set_names", out var names) && names.TryGetProperty(stem, out var label) ? label.GetString() : stem;
                var thumb = set.Value.EnumerateArray().Select(v => v.GetString()).FirstOrDefault(v => v is not null && (v.EndsWith(".png") || v.EndsWith(".jpg") || v.EndsWith(".webp")));
                var tag = category is "item" or "abilityeffect" ? string.Join('_', stem.Split('_').Skip(1).Take(1)) : null;
                var key = set.Name.Split(" / ")[0];
                string[]? locks = null;
                if (hero.TryGetProperty("base_locks", out var lockMap) && lockMap.TryGetProperty(stem, out var locked)) locks = locked.EnumerateArray().Select(v => v.GetString()!).ToArray();
                var style = stem.EndsWith("_1") ? "Estilo 1" : stem.EndsWith("_2") ? "Estilo 2" : null;
                if (hero.GetProperty("name").GetString() == "Drow Ranger" && stem == "base_arcana_2") style = "Master of Madness";
                if (hero.GetProperty("name").GetString() == "Drow Ranger" && stem == "base_arcana_1") style = "Cold of Heart";
                var groupKind = category switch { "basehero" => "Base", "abilityeffect" => "Efecto " + tag, "item" => "Pieza " + tag, "prismatic" => "Prismático", _ => "Set" };
                sets.Add(new CatalogSet(set.Name + " · " + stem, path, hash, display, category, tag, thumb,
                    style is null ? (set.Name.Contains(" / ") ? display + " · " + groupKind + " · " + key : null) : display + " · " + groupKind, style ?? (set.Name.Contains(" / ") ? stem : null), key, locks));
            }
            result.Add(new CatalogHero(hero.GetProperty("used_by_heroes").GetString()!,
                hero.GetProperty("name").GetString()!,
                hero.GetProperty("id").EnumerateArray().Select(v => v.GetInt32()).ToArray(), sets.ToArray(),
                hero.GetProperty("primary_attr").GetString() ?? "universal",
                hero.TryGetProperty("method", out var method) ? (method.ValueKind == JsonValueKind.Number ? method.GetInt32() : method.GetProperty("default").GetInt32()) : null,
                hero.TryGetProperty("method", out var policy) && policy.ValueKind == JsonValueKind.Object && policy.TryGetProperty("sets", out var overrides)
                    ? overrides.EnumerateObject().ToDictionary(v => v.Name, v => v.Value.GetInt32()) : null,
                hero.TryGetProperty("incompatible", out var incompatible) ? incompatible.EnumerateArray().SelectMany(group => group.GetProperty("sets").EnumerateArray().SelectMany(a => group.GetProperty("with").EnumerateArray().Select(b => new[] { a.GetString()!, b.GetString()! }))).ToArray() : null));
        }
        return heroes = result.OrderBy(h => h.Name, StringComparer.Ordinal).ToArray();
    }

    public async Task GenerateAsync(string root, string heroId, string assetPath, string output, CancellationToken ct = default)
    {
        var hero = (await LoadAsync(ct)).SingleOrDefault(h => h.Id == heroId)
            ?? throw new ArgumentException("Unknown catalog hero.");
        var set = hero.Sets.SingleOrDefault(s => s.AssetPath == assetPath)
            ?? throw new ArgumentException("Unknown catalog set.");
        var recipe = new GenerationRecipe(hero.Name + " / " + set.Name,
            [new RecipeAsset("selection.zip", set.Sha256,
                [Base + set.AssetPath, "https://cdn2.ardysamods.my.id/" + set.AssetPath], true)],
            [new RecipePatch(hero.Id, hero.ItemIds, "index.txt", AllowSubset: true)]);
        var file = Path.Combine(Path.GetTempPath(), "amt-recipe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(recipe), ct);
            await new GenerationService().GenerateAsync(root, file, output, ct);
        }
        finally { File.Delete(file); }
    }
}
