using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyRiders.Game.Content;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Run;

/// <summary>
/// The rift run: how deep this run has pushed, the reward that depth earns, where each rift leads, and how a run ends.
/// A rift to another world takes the player one level deeper (rewards up, the chase sooner and larger); the return rift
/// ends the run and banks its haul; a fall ends it and loses the haul. The bank and the number of runs are the only saved
/// values, stored through the Engine at those two boundaries. Between runs a summary shows until the player moves.
/// </summary>
internal sealed class Expedition : IDisposable
{
    private const string Scope = "riders.run", Key = "bank";

    private readonly Scenes scenes;
    private readonly Play play;
    private readonly Walker walker;
    private readonly RunTuning tuning;
    private readonly RunMessages text;
    private readonly ProductStateStore<BankState> store;
    private readonly string startTileset, startLayout;
    private readonly int startSeed;

    internal Expedition(IEngineContext engine, Scenes scenes, Play play, Walker walker)
    {
        this.scenes = scenes;
        this.play = play;
        this.walker = walker;
        tuning = Authored.Read(engine, RunTuning.Path, RunJson.Default.RunTuning);
        tuning.Validate();
        text = Authored.Read(engine, RunMessages.Path, RunJson.Default.RunMessages);
        text.Validate();
        (startTileset, startLayout, startSeed) = (scenes.Settings.Tileset, scenes.Settings.Layout, scenes.Seed);
        store = new ProductStateStore<BankState>(engine, Scope, new JsonProductStateCodec<BankState>(RunJson.Default.BankState));
        ProductStateLoad<BankState> loaded = store.Load(Key);
        Bank = loaded.Present ? loaded.State ?? throw new InvalidOperationException("The saved bank has no state.") : new BankState(0, 0);
        Run = Bank.Runs + 1;
    }

    internal int Depth { get; private set; }
    internal int Run { get; private set; }
    internal BankState Bank { get; private set; }
    internal float RewardMultiplier => tuning.RewardMultiplier.At(Depth);
    /// <summary>How the last run ended, until the player moves.</summary>
    internal string Summary { get; private set; } = "";

    /// <summary>Play takes up the current scene at this depth and reward, and the walker stands at its spawn.</summary>
    internal void EnterScene()
    {
        play.Enter(scenes.Current, scenes.Settings.Tileset, scenes.Seed, Depth, RewardMultiplier);
        walker.Enter(scenes.Current);
    }

    /// <summary>
    /// Goes through a rift. The return rift banks the haul and begins a new run; any other leads one level deeper, to a
    /// level of its world on a layout and seed drawn from this level's seed.
    /// </summary>
    internal void Travel(LevelScene from, RiftPoint rift)
    {
        if (rift.Destination is not { } world)
        {
            int haul = play.Inventory.Haul;
            End(Bank with { Banked = Bank.Banked + haul }, text.SummaryBanked, haul);
            return;
        }
        Random random = new(unchecked(scenes.Seed * 31 + rift.Index + 1));
        Depth++;
        scenes.GoTo(world.Tileset, from.TravelLayouts[random.Next(from.TravelLayouts.Length)], random.Next());
        EnterScene();
    }

    /// <summary>The player fell: the run ends and its haul is lost.</summary>
    internal void Fell() => End(Bank, text.SummaryLost, play.Inventory.Haul);

    /// <summary>The player moved: the summary of the last run gives way to play.</summary>
    internal void Dismiss() => Summary = "";

    /// <summary>The depth line, the bank, and the label of a rift the player stands near.</summary>
    internal (string Depth, string Bank, string? Rift) Hud(Vector3 position)
    {
        string depth = Template.Fill(text.Depth, ("depth", Depth), ("multiplier", RewardMultiplier));
        string bank = Template.Fill(text.Banked, ("total", Bank.Banked));
        string? rift = scenes.Level?.RiftNear(position) is { } near
            ? near.Destination is { } world ? Template.Fill(text.Rift, ("world", world.Name), ("depth", Depth + 1))
                : Template.Fill(text.ReturnRift, ("haul", play.Inventory.Haul))
            : null;
        return (depth, bank, rift);
    }

    public void Dispose() => store.Dispose();

    // A run ends: the bank and run count are saved, the summary is written, and the next run begins at the start level.
    private void End(BankState next, string summary, int haul)
    {
        Bank = next with { Runs = Run };
        store.Save(Key, Bank);
        Summary = Template.Fill(summary, ("run", Run), ("haul", haul), ("depth", Depth), ("total", Bank.Banked))
            + "\n" + Template.Fill(text.SummaryNext, ("next", Run + 1));
        Run++;
        Depth = 0;
        play.ResetPlayer();
        scenes.GoTo(startTileset, startLayout, unchecked(startSeed + Run * tuning.SeedStride));
        EnterScene();
    }
}

/// <summary>What is saved between runs: the banked haul and how many runs have ended.</summary>
internal sealed record BankState(int Banked, int Runs);

/// <summary>
/// How a run escalates (content/run/run.json): the reward multiplier by depth (loot rolls scale with it; the chase curves
/// in content/enemies/chase.json take the same depth), and how far each new run's start seed moves on.
/// </summary>
internal sealed record RunTuning(DepthCurve RewardMultiplier, int SeedStride)
{
    internal const string Path = "run/run.json";

    internal void Validate()
    {
        Authored.Positive(Path, "rewardMultiplier.minimum", RewardMultiplier.Minimum);
        Authored.Require(SeedStride != 0, Path, "seedStride", "must move the seed.");
    }
}

/// <summary>The run's HUD text (content/run/messages.json), as templates.</summary>
internal sealed record RunMessages(string Depth, string Banked, string Rift, string ReturnRift, string SummaryBanked, string SummaryLost,
    string SummaryNext)
{
    internal const string Path = "run/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "depth", Depth, "depth", "multiplier");
        Template.Check(Path, "banked", Banked, "total");
        Template.Check(Path, "rift", Rift, "world", "depth");
        Template.Check(Path, "returnRift", ReturnRift, "haul");
        Template.Check(Path, "summaryBanked", SummaryBanked, "run", "haul", "depth", "total");
        Template.Check(Path, "summaryLost", SummaryLost, "run", "haul", "depth", "total");
        Template.Check(Path, "summaryNext", SummaryNext, "next");
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RunTuning))]
[JsonSerializable(typeof(RunMessages))]
[JsonSerializable(typeof(BankState))]
internal sealed partial class RunJson : JsonSerializerContext;
