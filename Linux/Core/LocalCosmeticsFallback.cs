// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using ArdysaModsTools.Core.Helpers;

namespace ArdysaModsTools.Linux;

public static class LocalCosmeticsFallback
{
    public static Dictionary<string, (string block, string heroId)> Select(CatalogHero hero,
        IReadOnlyDictionary<string, string> items, IReadOnlySet<string> availableModels)
    {
        string Value(string block, string key) => KeyValuesBlockHelper.TryGetTopLevelValue(block, key, out var value) ? value ?? "" : "";
        var candidates = items.Values.Where(b => b.Contains("\"" + hero.Id + "\"", StringComparison.Ordinal)
            && Value(b, "prefab") == "wearable" && availableModels.Contains(Value(b, "model_player") + "_c")).ToArray();
        var result = new Dictionary<string, (string block, string heroId)>();
        foreach (var id in hero.ItemIds)
        {
            var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!items.TryGetValue(key, out var original) || Value(original, "prefab") != "default_item"
                || !original.Contains("\"" + hero.Id + "\"", StringComparison.Ordinal)) continue;
            var slot = Value(original, "item_slot");
            // Personas, body swaps and ambient-effect slots need a separate conversion pipeline.
            if (slot.Contains("persona", StringComparison.Ordinal) || slot is "ambient_effects" or "ability_effects" or "costume" or "taunt") continue;
            var candidate = candidates.FirstOrDefault(b => Value(b, "item_slot") == slot);
            if (candidate is null) continue;
            var model = Value(candidate, "model_player");
            if (model == Value(original, "model_player")) continue;
            var property = new Regex(@"(?m)^([ \t]*)""model_player""[ \t]+""[^""]*""");
            var replacement = property.IsMatch(original)
                ? property.Replace(original, m => m.Groups[1].Value + "\"model_player\"\t\t\"" + model + "\"", 1)
                : original.Insert(original.IndexOf('{') + 1, "\n\t\t\t\"model_player\"\t\t\"" + model + "\"");
            result.Add(key, (replacement, hero.Id));
        }
        return result;
    }
}
