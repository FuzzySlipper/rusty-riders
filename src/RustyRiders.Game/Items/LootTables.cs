using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Items;

/// <summary>
/// Weighted loot tables (content/loot/tables.json). A table rolls its entries <see cref="LootTable.Rolls"/> times
/// (scaled by the run's reward multiplier), each roll one weighted entry and a count between its bounds. A table may
/// name a <see cref="LootTable.Base"/> whose entries it adds to: a world's cache table biases the shared one.
/// </summary>
internal sealed record LootTables(LootTable[] Tables)
{
    internal const string Path = "loot/tables.json";
    internal const string CacheTable = "cache", DropTable = "drop";

    internal static LootTables Load(IEngineContext engine, ItemCatalog items)
    {
        LootTables tables = Authored.Read(engine, Path, LootJson.Default.LootTables);
        string? repeated = tables.Tables.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "tables", $"id '{repeated}' appears more than once.");
        Authored.Require(tables.Table(CacheTable) is not null && tables.Table(DropTable) is not null, Path, "tables",
            $"needs the '{CacheTable}' and '{DropTable}' tables.");
        for (int t = 0; t < tables.Tables.Length; t++)
        {
            LootTable table = tables.Tables[t];
            string at = $"tables[{t}]";
            Authored.Require(table.Rolls >= 1, Path, $"{at}.rolls", "must be at least 1.");
            Authored.Require(table.Base is null || (tables.Table(table.Base) is { Base: null }), Path, $"{at}.base", "names a table without a base of its own.");
            for (int e = 0; e < table.Entries.Length; e++)
            {
                LootEntry entry = table.Entries[e];
                items.Require(Path, $"{at}.entries", entry.Item);
                Authored.Require(entry.Weight >= 0, Path, $"{at}.entries[{e}].weight", "must not be negative.");
                Authored.Require(entry.Minimum >= 1 && entry.Maximum >= entry.Minimum, Path, $"{at}.entries[{e}].maximum", "counts run from 1 up.");
            }
            Authored.Require(tables.Entries(table).Sum(e => e.Weight) > 0, Path, $"{at}.entries", "gives no entry a positive weight.");
        }
        return tables;
    }

    internal LootTable? Table(string id) => Tables.FirstOrDefault(t => t.Id == id);

    /// <summary>The cache table of a world, or the shared one.</summary>
    internal LootTable Cache(string tileset) => Table($"{CacheTable}.{tileset}") ?? Table(CacheTable)!;

    /// <summary>Rolls a table: item ids with counts, merged.</summary>
    internal IReadOnlyList<(string Item, int Count)> Roll(LootTable table, Random random, float rewardMultiplier = 1)
    {
        LootEntry[] entries = Entries(table);
        int total = entries.Sum(e => e.Weight);
        int rolls = Math.Max(1, (int)MathF.Round(table.Rolls * rewardMultiplier));
        Dictionary<string, int> found = [];
        for (int r = 0; r < rolls; r++)
        {
            int pick = random.Next(total);
            LootEntry entry = entries.First(e => (pick -= e.Weight) < 0);
            found[entry.Item] = found.GetValueOrDefault(entry.Item) + random.Next(entry.Minimum, entry.Maximum + 1);
        }
        return found.Select(f => (f.Key, f.Value)).ToArray();
    }

    private LootEntry[] Entries(LootTable table) => table.Base is { } baseId ? [.. Table(baseId)!.Entries, .. table.Entries] : table.Entries;
}

internal sealed record LootTable(string Id, int Rolls, LootEntry[] Entries, string? Base = null);
internal sealed record LootEntry(string Item, int Weight, int Minimum, int Maximum);

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(LootTables))]
internal sealed partial class LootJson : JsonSerializerContext;
