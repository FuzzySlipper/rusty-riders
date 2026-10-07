using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Items;

/// <summary>
/// A level's things to take: closed caches at its cache points, rolled from its world's cache table when opened, and
/// items lying on the ground (an enemy's drop). Ammunition and supplies are taken by walking over them; caches and other
/// items with the use control, which costs world time. Drawn as placeholder boxes in each item's colour.
/// </summary>
internal sealed class Pickups : IDisposable
{
    private const ulong FirstObjectId = 5_000_000;

    private readonly IEngineContext engine;
    private readonly ItemCatalog items;
    private readonly LootTables loot;
    private readonly PickupTuning tuning;
    private readonly PickupMessages text;
    private readonly Dictionary<string, Appearance> looks = [];
    private readonly Appearance cache, opened;
    private readonly List<Cache> caches = [];
    private readonly List<Lying> lying = [];
    private LootTable cacheTable;
    private Random random = new(0);
    private float rewardMultiplier = 1;

    internal Pickups(IEngineContext engine, ItemCatalog items, LootTables loot, PickupTuning tuning, PickupMessages text)
    {
        this.engine = engine;
        this.items = items;
        this.loot = loot;
        this.tuning = tuning;
        this.text = text;
        foreach (ItemDefinition item in items.Items) looks[item.Id] = Primitive(item.Color);
        cache = Primitive(tuning.CacheColor);
        opened = Primitive(tuning.OpenedColor);
        cacheTable = loot.Table(LootTables.CacheTable)!;
    }

    internal PickupMessages Text => text;
    internal int ClosedCaches => caches.Count(c => !c.Opened);

    /// <summary>A level's caches close at its cache points, rolling from its world's table by the run's reward multiplier.</summary>
    internal void Enter(IEnumerable<Vector3> cachePoints, string tileset, int seed, float multiplier)
    {
        caches.Clear();
        lying.Clear();
        caches.AddRange(cachePoints.Select(p => new Cache(p)));
        cacheTable = loot.Cache(tileset);
        random = new Random(seed);
        rewardMultiplier = multiplier;
    }

    /// <summary>No level: nothing to take.</summary>
    internal void Leave()
    {
        caches.Clear();
        lying.Clear();
    }

    /// <summary>An enemy fell at <paramref name="feet"/>: by the drop chance, its drop table's items lie around it.</summary>
    internal void Drop(Vector3 feet)
    {
        if (random.NextDouble() >= tuning.DropChance) return;
        foreach ((string item, int count) in loot.Roll(loot.Table(LootTables.DropTable)!, random, rewardMultiplier))
            lying.Add(new Lying(items.Item(item)!, count, Scatter(feet)));
    }

    /// <summary>Lays <paramref name="count"/> of an item on the ground at a point (developer setups).</summary>
    internal void Place(ItemDefinition item, int count, Vector3 at) => lying.Add(new Lying(item, count, at));

    /// <summary>Takes every walk-over item the feet are on; returns the notice of what was taken, if anything.</summary>
    internal string? WalkOver(Vector3 feet, Inventory inventory)
    {
        List<string> took = [];
        foreach (Lying item in lying.Where(l => l.Item.WalkOver && Near(l.At, feet, tuning.WalkOverRadius)).ToArray())
            if (inventory.Take(item.Item, item.Count))
            {
                lying.Remove(item);
                took.Add(Named(item.Item, item.Count));
            }
        return took.Count == 0 ? null : Template.Fill(text.Took, ("item", string.Join(", ", took)));
    }

    /// <summary>What the use control would take now, for the HUD prompt: the nearest closed cache or item within reach.</summary>
    internal string Prompt(Vector3 feet) => Target(feet) switch
    {
        Cache => text.Cache,
        Lying item => Template.Fill(text.PickUp, ("item", Named(item.Item, item.Count))),
        _ => "",
    };

    /// <summary>
    /// Uses the nearest closed cache or item within reach: a cache opens and its roll goes into the inventory (what does
    /// not fit lies beside it); an item is taken. Returns the world seconds it costs and the notice, or null for nothing in reach.
    /// </summary>
    internal (float Seconds, string Notice)? Use(Vector3 feet, Inventory inventory)
    {
        switch (Target(feet))
        {
            case Cache target:
                target.Opened = true;
                List<string> found = [];
                foreach ((string id, int count) in loot.Roll(cacheTable, random, rewardMultiplier))
                {
                    ItemDefinition item = items.Item(id)!;
                    if (inventory.Take(item, count)) found.Add(Named(item, count));
                    else lying.Add(new Lying(item, count, Scatter(target.At)));
                }
                return (tuning.OpenSeconds, Template.Fill(text.Found, ("items", found.Count == 0 ? text.Nothing : string.Join(", ", found))));
            case Lying item:
                if (!inventory.Take(item.Item, item.Count)) return (0, Template.Fill(text.Full, ("item", item.Item.Name)));
                lying.Remove(item);
                return (tuning.OpenSeconds, Template.Fill(text.Took, ("item", Named(item.Item, item.Count))));
            default:
                return null;
        }
    }

    internal IEnumerable<AppearanceFact> Facts()
    {
        ulong id = FirstObjectId;
        Vector3 cacheSize = Authored.Vector(tuning.CacheSize);
        foreach (Cache c in caches)
            yield return new AppearanceFact(id++, false, 0, new Transform(c.At + Vector3.UnitY * cacheSize.Y / 2, Quaternion.Identity,
                c.Opened ? cacheSize with { Y = cacheSize.Y * .4f } : cacheSize), c.Opened ? opened : cache, true, RenderLayer.Scene);
        foreach (Lying item in lying)
            yield return new AppearanceFact(id++, false, 0, new Transform(item.At + Vector3.UnitY * tuning.ItemSize, Quaternion.Identity,
                new Vector3(tuning.ItemSize)), looks[item.Item.Id], true, RenderLayer.Scene);
    }

    /// <summary>Every lying item and cache, for inspection.</summary>
    internal IEnumerable<string> Describe() =>
        caches.Select(c => FormattableString.Invariant($"cache at {c.At.X:0.0},{c.At.Z:0.0}{(c.Opened ? " (opened)" : "")}"))
            .Concat(lying.Select(l => FormattableString.Invariant($"{Named(l.Item, l.Count)} at {l.At.X:0.0},{l.At.Z:0.0}")));

    public void Dispose()
    {
        foreach (Appearance look in looks.Values) look.Dispose();
        cache.Dispose();
        opened.Dispose();
    }

    private object? Target(Vector3 feet) =>
        caches.Where(c => !c.Opened && Near(c.At, feet, tuning.Reach)).Cast<object>()
            .Concat(lying.Where(l => !l.Item.WalkOver && Near(l.At, feet, tuning.Reach)))
            .MinBy(t => Vector3.DistanceSquared(t is Cache c ? c.At : ((Lying)t).At, feet));

    private Vector3 Scatter(Vector3 around)
    {
        float angle = (float)(random.NextDouble() * Math.Tau), reach = (float)random.NextDouble() * tuning.DropSpread;
        return around + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * reach;
    }

    private static bool Near(Vector3 a, Vector3 b, float reach) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) <= reach && MathF.Abs(a.Y - b.Y) <= 2;

    private static string Named(ItemDefinition item, int count) => count > 1 ? $"{item.Name} ×{count}" : item.Name;

    private Appearance Primitive(float[] rgb) =>
        engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Authored.Color(rgb)));

    private sealed class Cache(Vector3 at)
    {
        internal Vector3 At { get; } = at;
        internal bool Opened { get; set; }
    }

    private sealed record Lying(ItemDefinition Item, int Count, Vector3 At);
}

/// <summary>
/// How things are taken (content/items/pickups.json): the use control's reach, the walk-over radius, the world seconds
/// opening or taking costs, the sizes and colours drawn, and an enemy's drop chance and scatter.
/// </summary>
internal sealed record PickupTuning(float Reach, float WalkOverRadius, float OpenSeconds, float ItemSize, float[] CacheSize,
    float[] CacheColor, float[] OpenedColor, float DropChance, float DropSpread)
{
    internal const string Path = "items/pickups.json";

    internal void Validate()
    {
        Authored.Positive(Path, "reach", Reach);
        Authored.Positive(Path, "walkOverRadius", WalkOverRadius);
        Authored.AtLeast(Path, "openSeconds", OpenSeconds, 0);
        Authored.Positive(Path, "itemSize", ItemSize);
        Authored.Point(Path, "cacheSize", CacheSize);
        Authored.Colour(Path, "cacheColor", CacheColor);
        Authored.Colour(Path, "openedColor", OpenedColor);
        Authored.Within(Path, "dropChance", DropChance, 0, 1);
        Authored.AtLeast(Path, "dropSpread", DropSpread, 0);
    }
}

/// <summary>The item HUD's text (content/items/messages.json), as templates.</summary>
internal sealed record PickupMessages(string Found, string Took, string Full, string Used, string Cache, string PickUp, string Haul, string Consumable,
    string Nothing)
{
    internal const string Path = "items/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "found", Found, "items");
        Template.Check(Path, "took", Took, "item");
        Template.Check(Path, "full", Full, "item");
        Template.Check(Path, "used", Used, "item");
        Template.Check(Path, "cache", Cache);
        Template.Check(Path, "pickUp", PickUp, "item");
        Template.Check(Path, "haul", Haul, "value");
        Template.Check(Path, "consumable", Consumable, "item", "count");
        Template.Check(Path, "nothing", Nothing);
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PickupTuning))]
[JsonSerializable(typeof(PickupMessages))]
internal sealed partial class PickupJson : JsonSerializerContext;
