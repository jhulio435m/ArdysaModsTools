// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Security.Cryptography;

namespace ArdysaModsTools.Linux;

public sealed record GallerySelection(string? Set = null, string[]? Items = null, string? Base = null,
    string? Prismatic = null, string[]? Effects = null)
{
    public IEnumerable<string> Assets => new[] { Set, Base, Prismatic }.Where(v => v is not null).Cast<string>()
        .Concat(Items ?? []).Concat(Effects ?? []).Distinct(StringComparer.Ordinal);
}
public sealed record GalleryState(Dictionary<string, GallerySelection> Baseline, Dictionary<string, GallerySelection> Draft,
    Dictionary<string, GallerySelection> Applied, string[] Favorites, string? InstalledHash, string? BaselineHash = null, string? BuildId = null);

public sealed class GalleryStore
{
    public string DirectoryPath { get; }
    public string BaselineVpk => Path.Combine(DirectoryPath, "baseline_dir.vpk");
    private string StateFile => Path.Combine(DirectoryPath, "selection.json");
    public GalleryStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(new LinuxPaths().Data, "editor");
    public GalleryState Load() => File.Exists(StateFile)
        ? JsonSerializer.Deserialize<GalleryState>(File.ReadAllText(StateFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid editor selection.")
        : new([], [], [], [], null);
    public void Save(GalleryState state)
    {
        LinuxPaths.EnsureNoLinks(StateFile);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = StateFile + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, StateFile, true);
    }
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static bool Same(GallerySelection? a, GallerySelection? b) =>
        (a?.Set == b?.Set && a?.Base == b?.Base && a?.Prismatic == b?.Prismatic
        && (a?.Items ?? []).Order().SequenceEqual((b?.Items ?? []).Order())
        && (a?.Effects ?? []).Order().SequenceEqual((b?.Effects ?? []).Order()));
    public static void Validate(Dictionary<string, GallerySelection> selections, CatalogHero[] catalog)
    {
        if (selections is null || selections.Values.Any(value => value is null)) throw new ArgumentException("Selección inválida.");
        if (selections.Count > catalog.Length) throw new ArgumentException("Too many hero selections.");
        foreach (var (id, selection) in selections)
        {
            var hero = catalog.SingleOrDefault(h => h.Id == id) ?? throw new ArgumentException("Unknown hero: " + id);
            CatalogSet? Find(string? path, params string[] categories)
            {
                if (path is null) return null;
                var set = hero.Sets.SingleOrDefault(s => s.AssetPath == path) ?? throw new ArgumentException("Unknown asset for " + hero.Name);
                if (!categories.Contains(set.Category)) throw new ArgumentException("Invalid selection category for " + hero.Name);
                return set;
            }
            var chosenSet = Find(selection.Set, "legacyset", "customset", "persona");
            var body = Find(selection.Base, "basehero");
            _ = Find(selection.Prismatic, "prismatic");
            foreach (var path in selection.Items ?? []) _ = Find(path, "item");
            foreach (var path in selection.Effects ?? []) _ = Find(path, "abilityeffect");
            if (selection.Assets.Count() > 32) throw new ArgumentException("Too many layers for " + hero.Name);
            if (chosenSet?.Category == "persona" && selection.Assets.Count() != 1)
                throw new ArgumentException("A persona must be selected alone: " + hero.Name);
            if (selection.Prismatic is not null && body is null) throw new ArgumentException("Choose a base before prismatic.");
            var items = (selection.Items ?? []).Select(p => hero.Sets.Single(s => s.AssetPath == p));
            if (items.GroupBy(s => s.Tag).Any(g => g.Count() > 1)) throw new ArgumentException("Choose one item per slot.");
            var stems = selection.Assets.Select(Path.GetFileNameWithoutExtension).ToHashSet();
            if ((hero.Incompatible ?? []).Any(pair => pair.All(stems.Contains))) throw new ArgumentException("Incompatible layers for " + hero.Name);
        }
    }
}
