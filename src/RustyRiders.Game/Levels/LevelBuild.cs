using System.Diagnostics;
using Rusty.Engine;
using RustyRiders.Game.Levels.Kit;

namespace RustyRiders.Game.Levels;

/// <summary>
/// One level being built, at once or in the background. In the background its <see cref="LevelBlueprint"/> is made on the
/// Engine's worker lane; a later update makes the scene from it (art, materials, collision, navigation and points, which
/// need the Engine), rejecting it for the next seed when its arrival cannot reach enough rifts; later updates then make
/// its sections a few at a time (<see cref="LevelScene.Prepare"/>) until the renderer has drawn them.
/// </summary>
internal sealed class LevelBuild : IDisposable
{
    private readonly IEngineContext engine;
    private readonly PreparationJobs jobs;
    private readonly LevelRules rules;
    private readonly LevelData data;
    private readonly LayoutDefinition layout;
    private readonly KitRecipe? kit;
    private readonly long started = Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
    private PreparationJob<LevelBlueprint>? job;
    private LevelScene? scene;
    private int attempts, updates;
    private double longestUpdate, sceneSeconds;

    /// <summary>Starts building a level of <paramref name="settings"/> and <paramref name="seed"/> in the background.</summary>
    internal LevelBuild(IEngineContext engine, PreparationJobs jobs, LevelRules rules, LevelSettings settings, int seed)
    {
        this.engine = engine;
        this.jobs = jobs;
        this.rules = rules;
        (data, layout, kit) = Inputs(engine, rules, settings);
        Settings = settings;
        Seed = seed;
        Begin();
    }

    internal LevelSettings Settings { get; }
    /// <summary>The seed being built: the one asked for, or a later one when that level was rejected.</summary>
    internal int Seed { get; private set; }

    /// <summary>What the build cost (load-cost evidence): its seconds, attempts and updates, and the most it took of one.</summary>
    internal string Report => FormattableString.Invariant(
        $"{Settings.Tileset} · {Settings.Layout} · seed {Seed}: {Stopwatch.GetElapsedTime(started).TotalSeconds:0.000} s, {attempts} attempts, {updates} updates, longest {longestUpdate * 1000:0.0} ms (making the scene {sceneSeconds * 1000:0.0} ms)");

    /// <summary>Builds a level of <paramref name="settings"/> at once, from <paramref name="seed"/> on, keeping the last when every attempt is rejected.</summary>
    internal static LevelScene Now(IEngineContext engine, LevelRules rules, LevelSettings settings, ref int seed)
    {
        (LevelData data, LayoutDefinition layout, KitRecipe? kit) = Inputs(engine, rules, settings);
        for (int attempt = 1; ; attempt++)
        {
            LevelScene built = new(engine, settings, data, layout, seed, rules, LevelBlueprint.Make(data, layout, seed, rules, kit), deferred: false);
            if (built.Playable || attempt >= rules.Points.LevelAttempts) return built;
            built.Dispose();
            seed++;
        }
    }

    /// <summary>
    /// This update's share of the build, about <paramref name="budgetSeconds"/> of making sections; returns the scene once
    /// it is drawn, which then belongs to the caller. A failed job throws its exception here.
    /// </summary>
    internal LevelScene? Advance(double budgetSeconds)
    {
        long begun = Stopwatch.GetTimestamp();
        LevelScene? done = Step(budgetSeconds);
        longestUpdate = Math.Max(longestUpdate, Stopwatch.GetElapsedTime(begun).TotalSeconds);
        updates++;
        return done;
    }

    public void Dispose()
    {
        job?.Dispose();
        job = null;
        scene?.Dispose();
        scene = null;
    }

    private LevelScene? Step(double budgetSeconds)
    {
        if (job is not null)
        {
            if (!job.TryTake(out LevelBlueprint? blueprint)) return null;
            job.Dispose();
            job = null;
            long making = Stopwatch.GetTimestamp();
            scene = new LevelScene(engine, Settings, data, layout, Seed, rules, blueprint, deferred: true);
            sceneSeconds = Stopwatch.GetElapsedTime(making).TotalSeconds;
            if (!scene.Playable && attempts < rules.Points.LevelAttempts)
            {
                scene.Dispose();
                scene = null;
                Seed++;
                Begin();
            }
            return null;
        }
        if (scene is null || !scene.Prepare(budgetSeconds)) return null;
        LevelScene ready = scene;
        scene = null;
        return ready;
    }

    private void Begin()
    {
        attempts++;
        (LevelData data, LayoutDefinition layout, int seed, LevelRules rules, KitRecipe? kit) = (this.data, this.layout, Seed, this.rules, this.kit);
        job = jobs.Run(_ => LevelBlueprint.Make(data, layout, seed, rules, kit));
    }

    // What a level of these settings is made from, read through the Engine's content on the update.
    private static (LevelData Data, LayoutDefinition Layout, KitRecipe? Kit) Inputs(IEngineContext engine, LevelRules rules, LevelSettings settings)
    {
        LevelData data = LevelData.Load(engine, settings.Tileset);
        LayoutDefinition layout = data.Layouts.FirstOrDefault(layout => layout.Id == settings.Layout)
            ?? throw new InvalidOperationException($"content/levels/layouts.json has no layout '{settings.Layout}'.");
        KitRecipe? kit = settings.Build == LevelSettings.KitBuild ? KitRecipe.Load(engine, data.Tileset.Id, rules.Plan, data.Generator.CellSize) : null;
        return (data, layout, kit);
    }
}
