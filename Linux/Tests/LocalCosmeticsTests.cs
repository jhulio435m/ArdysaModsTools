// SPDX-License-Identifier: GPL-3.0-or-later
using NUnit.Framework;

namespace ArdysaModsTools.Linux.Tests;

[TestFixture]
public sealed class LocalCosmeticsTests
{
    [Test]
    public void OnlyUsesExistingModelsForTheSameHeroAndSlotAndPreservesDefaultIdentity()
    {
        const string original = "\"1\" { \"prefab\" \"default_item\" \"item_slot\" \"head\" \"used_by_heroes\" { \"npc_dota_hero_test\" \"1\" } }";
        var items = new Dictionary<string, string>
        {
            ["1"] = original,
            ["2"] = "\"2\" { \"prefab\" \"wearable\" \"item_slot\" \"head\" \"model_player\" \"models/missing.vmdl\" \"used_by_heroes\" { \"npc_dota_hero_test\" \"1\" } }",
            ["3"] = "\"3\" { \"prefab\" \"wearable\" \"item_slot\" \"head\" \"model_player\" \"models/existing.vmdl\" \"used_by_heroes\" { \"npc_dota_hero_other\" \"1\" } }",
            ["4"] = "\"4\" { \"prefab\" \"wearable\" \"item_slot\" \"head\" \"model_player\" \"models/existing.vmdl\" \"used_by_heroes\" { \"npc_dota_hero_test\" \"1\" } }"
        };
        var hero = new CatalogHero("npc_dota_hero_test", "Test", [1], []);
        var selected = LocalCosmeticsFallback.Select(hero, items, new HashSet<string> { "models/existing.vmdl_c" });
        Assert.That(selected.Keys, Is.EquivalentTo(new[] { "1" }));
        Assert.That(selected["1"].block, Does.Contain("models/existing.vmdl"));
        Assert.That(selected["1"].block, Does.Contain("\"default_item\""));
        Assert.That(LocalCosmeticsFallback.Select(hero, items, new HashSet<string>()), Is.Empty);
    }
}
