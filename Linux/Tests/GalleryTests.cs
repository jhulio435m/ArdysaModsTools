// SPDX-License-Identifier: GPL-3.0-or-later
using NUnit.Framework;
using ArdysaModsTools.Core.Helpers;

namespace ArdysaModsTools.Linux.Tests;

[TestFixture]
public sealed class GalleryTests
{
    private string root = null!;
    [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "amt-gallery-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TearDown] public void Cleanup() => Directory.Delete(root, true);
    private string Write(string relative, string text)
    { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path; }
    private static string Block(int id, string hero, string slot, string model = "") =>
        $"\"{id}\"\n{{\n\"name\" \"test\"\n\"prefab\" \"default_item\"\n\"item_slot\" \"{slot}\"\n\"model_player\" \"{model}\"\n\"used_by_heroes\" {{ \"{hero}\" \"1\" }}\n}}";
    private static CatalogSet Set(string name, string category, string tag = "") => new(name, name + ".zip", new string('A', 64), name, category, tag);

    [Test]
    public void RejectsWrongHeroDuplicateSlotsAndPersonaCombinations()
    {
        var hero = new CatalogHero("npc_dota_hero_test", "Test", [1], [Set("a", "item", "head"), Set("b", "item", "head"), Set("persona", "persona")]);
        Assert.Throws<ArgumentException>(() => GalleryStore.Validate(new() { [hero.Id] = new(Items: ["a.zip", "b.zip"]) }, [hero]));
        Assert.Throws<ArgumentException>(() => GalleryStore.Validate(new() { [hero.Id] = new("persona.zip", ["a.zip"]) }, [hero]));
        Assert.Throws<ArgumentException>(() => GalleryStore.Validate(new() { [hero.Id] = new("other.zip") }, [hero]));
    }
    [Test]
    public void AssetPathsPersistIndependentlyOfCatalogOrdering()
    {
        var store = new GalleryStore(Path.Combine(root, "state"));
        var selection = new GallerySelection("arcana.zip", ["head.zip"], "base_arcana_2.zip");
        store.Save(new(new() { ["hero"] = selection }, new() { ["hero"] = selection }, [], ["hero"], null));
        Assert.That(GalleryStore.Same(store.Load().Draft["hero"], selection), Is.True);
        Assert.That(GalleryStore.Same(selection, new("arcana.zip", ["head.zip"], "base_arcana_1.zip")), Is.False);
    }
    [Test]
    public void BaseKeepsBodyWhileBundleUsesItsStyleSpecificWearablesAndItemWinsItsSlot()
    {
        var hero = new CatalogHero("npc_dota_hero_test", "Test", [1, 2], [], Method: 1);
        Write("base/index.txt", Block(1, hero.Id, "hero_base") + "\n" + Block(2, hero.Id, "weapon", "models/heroes/test/weapon.vmdl"));
        Write("base/models/heroes/test/body.vmdl_c", "base body");
        Write("base/models/heroes/test/weapon.vmdl_c", "empty weapon");
        Write("set/index.txt", Block(2, hero.Id, "weapon", "models/heroes/test/weapon.vmdl"));
        Write("set/models/heroes/test/body.vmdl_c", "wrong body");
        Write("set/models/heroes/test/weapon.vmdl_c", "style 1");
        Write("set/variants/base_arcana_2/models/heroes/test/weapon.vmdl_c", "style 2");
        var layers = new[] { (Set("bundle", "legacyset"), Path.Combine(root, "set")), (Set("base_arcana_2", "basehero"), Path.Combine(root, "base")) };
        var result = GalleryComposer.Compose(hero, layers, new Dictionary<string, string>());
        Assert.That(File.ReadAllText(result.Files["models/heroes/test/body.vmdl_c"]), Is.EqualTo("base body"));
        Assert.That(File.ReadAllText(result.Files["models/heroes/test/weapon.vmdl_c"]), Is.EqualTo("style 2"));
        Write("item/index.txt", Block(2, hero.Id, "weapon", "models/heroes/test/weapon.vmdl"));
        Write("item/models/heroes/test/weapon.vmdl_c", "chosen item");
        result = GalleryComposer.Compose(hero, layers.Append((Set("item_weapon", "item", "weapon"), Path.Combine(root, "item"))).ToArray(), new Dictionary<string, string>());
        Assert.That(File.ReadAllText(result.Files["models/heroes/test/weapon.vmdl_c"]), Is.EqualTo("chosen item"));
    }
    [Test]
    public void LockedSlotRestoresTheBaseBlockAndItsCompiledModel()
    {
        var hero = new CatalogHero("npc_dota_hero_test", "Test", [1], [], Method: 1);
        var block = Block(1, hero.Id, "weapon", "models/heroes/test/weapon.vmdl");
        Write("base/index.txt", block); Write("base/models/heroes/test/weapon.vmdl_c", "locked base weapon");
        Write("set/index.txt", block); Write("set/models/heroes/test/weapon.vmdl_c", "set weapon");
        var body = Set("base_arcana", "basehero") with { Locks = ["weapon"] };
        var result = GalleryComposer.Compose(hero, [(body, Path.Combine(root, "base")), (Set("set", "legacyset"), Path.Combine(root, "set"))],
            new Dictionary<string, string> { ["1"] = block });
        Assert.That(File.ReadAllText(result.Files["models/heroes/test/weapon.vmdl_c"]), Is.EqualTo("locked base weapon"));
    }

    [Test]
    public void RejectsEffectArchiveContainingHeroModels()
    {
        Write("effect/models/heroes/test/body.vmdl_c", "bad");
        Assert.Throws<InvalidDataException>(() => GalleryComposer.Compose(new("hero", "Test", [], []),
            [(Set("effect", "abilityeffect"), Path.Combine(root, "effect"))], new Dictionary<string, string>()));
    }
    [Test]
    public void PlanetfallAssignsTheSeparateRefitHeadInsteadOfLeavingItModelLess()
    {
        var hero = new CatalogHero("npc_dota_hero_earthshaker", "Earthshaker", [8655, 855], [], Method: 1);
        Write("base/index.txt", Block(855, hero.Id, "hero_base"));
        Write("arcana/index.txt", Block(8655, hero.Id, "head"));
        var head = Block(34382, hero.Id, "head", "models/items/earthshaker/earthshaker_arcana/head_refit.vmdl");
        var result = GalleryComposer.Compose(hero,
            [(Set("arcana", "legacyset"), Path.Combine(root, "arcana")), (Set("base_arcana_1", "basehero"), Path.Combine(root, "base"))],
            new Dictionary<string, string> { ["34382"] = head });
        Assert.That(result.Blocks["8655"], Does.Contain("head_refit.vmdl"));
        Assert.That(result.Blocks["8655"], Does.StartWith("\"8655\""));
    }
    [Test]
    public async Task ClearingOneHeroRestoresNativeFilesAndPreservesOtherHeroAndSharedInterface()
    {
        var hero = new CatalogHero("npc_dota_hero_test", "Test", [1], [Set("old", "legacyset")]);
        var original = Block(1, hero.Id, "weapon", "models/heroes/test/weapon.vmdl");
        Write("native/scripts/items/items_game.txt", "\"items_game\"\n{\n\"items\"\n{\n" + original + "\n}\n}");
        Write("native/models/heroes/test/weapon.vmdl_c", "native weapon");
        var dota = Path.Combine(root, "dota"); Directory.CreateDirectory(Path.Combine(dota, "game/dota"));
        await VpkArchive.BuildAsync(Path.Combine(root, "native"), Path.Combine(dota, "game/dota/pak01_dir.vpk"));
        Write("baseline/scripts/items/items_game.txt", "\"items_game\"\n{\n\"items\"\n{\n" + original + "\n}\n}");
        Write("baseline/models/heroes/test/weapon.vmdl_c", "old mod");
        Write("baseline/models/heroes/other/weapon.vmdl_c", "keep other hero");
        Write("baseline/panorama/logo.txt", "keep interface");
        var store = new GalleryStore(Path.Combine(root, "state")); Directory.CreateDirectory(store.DirectoryPath);
        await VpkArchive.BuildAsync(Path.Combine(root, "baseline"), store.BaselineVpk);
        var cache = Path.Combine(root, "cache"); var folder = Path.Combine(cache, new string('a', 64));
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, ".complete"), "cached fixture");
        Directory.CreateDirectory(Path.Combine(folder, "models/heroes/test")); File.WriteAllText(Path.Combine(folder, "models/heroes/test/weapon.vmdl_c"), "old mod");
        var state = new GalleryState(new() { [hero.Id] = new("old.zip") }, [], [], [], null);
        var output = Path.Combine(root, "new.vpk");
        await new GalleryComposer(cache).GenerateAsync(dota, store, state, [], [hero], output, _ => { });
        var extraction = Path.Combine(root, "result"); await new VpkArchive(output).ExtractAsync(extraction);
        Assert.That(File.ReadAllText(Path.Combine(extraction, "models/heroes/test/weapon.vmdl_c")), Is.EqualTo("native weapon"));
        Assert.That(File.ReadAllText(Path.Combine(extraction, "models/heroes/other/weapon.vmdl_c")), Is.EqualTo("keep other hero"));
        Assert.That(File.ReadAllText(Path.Combine(extraction, "panorama/logo.txt")), Is.EqualTo("keep interface"));
        Assert.That(ItemsGameBlockIndex.Build(File.ReadAllText(Path.Combine(extraction, "scripts/items/items_game.txt"))).Count, Is.EqualTo(1));
    }
}
