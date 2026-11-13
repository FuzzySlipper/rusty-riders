using Rusty.Engine;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Which scene is current and how the next is built: a generated level from the level settings and seed (rejecting
/// unplayable seeds), or the gallery. Travel and the developer keys build the next level in the background
/// (<see cref="LevelBuild"/>) while the current scene stays shown, and <see cref="Advance"/> switches to it once it is
/// drawn. The first level, the gallery and the developer overrides (an exact level or build) are built at once, the
/// current scene staying whole until the next one is built. Either way the old scene is then disposed.
/// </summary>
internal sealed class Scenes : IDisposable
{
    // How long an update may spend making a background level's meshes; one band is made each update at least.
    private const double BuildSecondsPerUpdate = .002;

    private readonly IEngineContext engine;
    private readonly PreparationJobs jobs;
    private readonly LevelRules rules;
    private LevelBuild? pending;

    internal Scenes(IEngineContext engine, PreparationJobs jobs, LevelRules rules)
    {
        this.engine = engine;
        this.jobs = jobs;
        this.rules = rules;
        Settings = LevelSettings.Load(engine);
        int seed = Settings.Seed;
        Current = LevelBuild.Now(engine, rules, Settings, ref seed);
        Seed = seed;
        Level!.Show();
    }

    internal IWalkScene Current { get; private set; }
    internal LevelSettings Settings { get; private set; }
    internal int Seed { get; private set; }
    internal bool ShowingLevel => Current is LevelScene;
    internal LevelScene? Level => Current as LevelScene;
    /// <summary>Whether a level is being built in the background; the current scene stays until it is drawn.</summary>
    internal bool Building => pending is not null;
    /// <summary>What the last background build cost, or why it failed (developer readout).</summary>
    internal string LastBuild { get; private set; } = "";

    /// <summary>Starts building a level of a tileset, layout and seed in the background (a rift's destination).</summary>
    internal void Travel(string tileset, string layout, int seed) => Begin(Settings with { Tileset = tileset, Layout = layout }, seed);

    /// <summary>The same level settings on the next seed, built in the background (developer key).</summary>
    internal void NextSeed() => Begin(Settings, Seed + 1);

    /// <summary>The next level build (tiles, shells, sweeps, kit), built in the background (developer key).</summary>
    internal void NextBuild() => Begin(Settings with { Build = Settings.NextBuild }, Seed);

    /// <summary>The tileset's next shell floor texture after the current one, then its own again, built in the background (developer key).</summary>
    internal void NextFloorTexture()
    {
        string[] ids = ShellDefinition.Load(engine, Settings.Tileset)?.FloorTextures.Select(t => t.Id).ToArray() ?? [];
        int next = Array.IndexOf(ids, Settings.FloorTexture) + 1;
        Begin(Settings with { FloorTexture = next < ids.Length ? ids[next] : null }, Seed);
    }

    /// <summary>
    /// This update's share of a background build; true when it switched to the built level. A build that fails is
    /// dropped (the current scene stays) and its error is <see cref="LastBuild"/>.
    /// </summary>
    internal bool Advance()
    {
        if (pending is null) return false;
        LevelScene? built;
        try
        {
            built = pending.Advance(BuildSecondsPerUpdate);
        }
        catch (Exception error) when (error is InvalidOperationException or EngineCallException)
        {
            LastBuild = $"{pending.Settings.Tileset} · {pending.Settings.Layout} failed: {error.Message}";
            Console.Error.WriteLine($"Level build {LastBuild}");
            Cancel();
            return false;
        }
        if (built is null) return false;
        (Settings, Seed) = (pending.Settings, pending.Seed);
        string report = pending.Report;
        Cancel();
        long switching = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only
        Show(built);
        LastBuild = FormattableString.Invariant($"{report}; switch {System.Diagnostics.Stopwatch.GetElapsedTime(switching).TotalMilliseconds:0.0} ms");
        return true;
    }

    /// <summary>Builds and switches to a level of a tileset, layout and seed at once (developer override).</summary>
    internal void GoTo(string tileset, string layout, int seed) => SwitchNow(Settings with { Tileset = tileset, Layout = layout }, seed);

    /// <summary>Rebuilds the level as the named build at once (developer override), keeping the settings if that fails.</summary>
    internal void SetBuild(string build) => SwitchNow(Settings with { Build = build }, Seed);

    /// <summary>Switches between the current level and the gallery, at once.</summary>
    internal void ToggleGallery()
    {
        if (ShowingLevel)
        {
            Cancel();
            Show(new GalleryScene(engine, GalleryDefinition.Load(engine)));
        }
        else
        {
            SwitchNow(Settings, Seed);
        }
    }

    public void Dispose()
    {
        Cancel();
        Current.Dispose();
    }

    private void Begin(LevelSettings settings, int seed)
    {
        Cancel();
        pending = new LevelBuild(engine, jobs, rules, settings, seed);
    }

    private void Cancel()
    {
        pending?.Dispose();
        pending = null;
    }

    // The current scene stays whole until the next one is built; a failure leaves the settings as they were.
    private void SwitchNow(LevelSettings settings, int seed)
    {
        Cancel();
        LevelScene next = LevelBuild.Now(engine, rules, settings, ref seed);
        (Settings, Seed) = (settings, seed);
        Show(next);
    }

    private void Show(IWalkScene next)
    {
        Current.Dispose();
        Current = next;
        (next as LevelScene)?.Show();
        // A level's build leaves its builders' arrays behind, up to a gigabyte for a large stone kit, which the collector
        // could keep committed long into play; it is collected here, inside the switch, while little else lives.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
