// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace ArdysaModsTools.Linux;

public sealed record CatalogSet(string Name, string AssetPath, string Sha256);
public sealed record CatalogHero(string Id, string Name, int[] ItemIds, CatalogSet[] Sets);

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
            foreach (var set in hero.GetProperty("sets").EnumerateObject())
            {
                if (set.Value.ValueKind != JsonValueKind.Array) continue;
                var url = set.Value.EnumerateArray().Select(v => v.GetString())
                    .FirstOrDefault(v => v is not null && v.StartsWith(Base, StringComparison.Ordinal)
                        && v.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                if (url is null) continue; // Default skins and split archives need separate handling.
                var path = url[Base.Length..];
                _ = LinuxPaths.SafeChild(Path.GetTempPath(), path);
                if (!hashes.TryGetProperty(path, out var entry)) continue;
                var hash = entry.GetProperty("sha256").GetString() ?? "";
                if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) continue;
                sets.Add(new CatalogSet(set.Name + " · " + Path.GetFileNameWithoutExtension(path), path, hash));
            }
            result.Add(new CatalogHero(hero.GetProperty("used_by_heroes").GetString()!,
                hero.GetProperty("name").GetString()!,
                hero.GetProperty("id").EnumerateArray().Select(v => v.GetInt32()).ToArray(), sets.ToArray()));
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
