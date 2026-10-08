using Rusty.Engine;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Which scene is current and how the next is built: a generated level from the level settings and seed (rejecting
/// unplayable seeds), or the gallery. Building the next scene leaves the current one whole until it succeeds; the old
/// one is then disposed. The developer variants (next seed, build, floor texture, an exact level) change the settings
/// and rebuild.
/// </summary>
internal sealed class Scenes : IDisposable
{
    private readonly IEngineContext engine;
    private readonly LevelRules rules;

    internal Scenes(IEngineContext engine, LevelRules rules)
    {
        this.engine = engine;
        this.rules = rules;
        Settings = LevelSettings.Load(engine);
        Seed = Settings.Seed;
        Current = Build(true);
    }

    internal IWalkScene Current { get; private set; }
    internal LevelSettings Settings { get; private set; }
    internal int Seed { get; private set; }
    internal bool ShowingLevel => Current is LevelScene;
    internal LevelScene? Level => Current as LevelScene;

    /// <summary>Builds and switches to a level of a tileset, layout and seed (the palette is the seed's).</summary>
    internal void GoTo(string tileset, string layout, int seed)
    {
        LevelSettings previous = Settings;
        int previousSeed = Seed;
        Settings = Settings with { Tileset = tileset, Layout = layout, Palette = null };
        Seed = seed;
        try
        {
            Switch(true);
        }
        catch
        {
            Settings = previous;
            Seed = previousSeed;
            throw;
        }
    }

    /// <summary>Switches between the current level and the gallery.</summary>
    internal void ToggleGallery() => Switch(!ShowingLevel);

    /// <summary>The same level settings on the next seed (developer key).</summary>
    internal void NextSeed()
    {
        Seed++;
        Switch(true);
    }

    /// <summary>The next level build: tiles, shells, sweeps (developer key).</summary>
    internal void NextBuild()
    {
        Settings = Settings with { Build = Settings.NextBuild };
        Switch(true);
    }

    /// <summary>The tileset's next shell floor texture after the current one, then its own again (developer key).</summary>
    internal void NextFloorTexture()
    {
        string[] ids = ShellDefinition.Load(engine, Settings.Tileset)?.FloorTextures.Select(t => t.Id).ToArray() ?? [];
        int next = Array.IndexOf(ids, Settings.FloorTexture) + 1;
        Settings = Settings with { FloorTexture = next < ids.Length ? ids[next] : null };
        Switch(true);
    }

    public void Dispose() => Current.Dispose();

    private void Switch(bool level)
    {
        IWalkScene next = Build(level); // the current scene stays whole until the next one is built
        Current.Dispose();
        Current = next;
    }

    private IWalkScene Build(bool level)
    {
        if (!level) return new GalleryScene(engine, GalleryDefinition.Load(engine));
        LevelData data = LevelData.Load(engine, Settings.Tileset);
        LayoutDefinition layout = data.Layouts.FirstOrDefault(layout => layout.Id == Settings.Layout)
            ?? throw new InvalidOperationException($"content/levels/layouts.json has no layout '{Settings.Layout}'.");
        // A level whose arrival cannot reach enough rifts is rejected; the next seed is tried, and the last kept.
        for (int attempt = 1; ; attempt++)
        {
            LevelScene built = new(engine, Settings, data, layout, Seed, rules);
            if (built.Playable || attempt >= rules.Points.LevelAttempts) return built;
            built.Dispose();
            Seed++;
        }
    }
}
