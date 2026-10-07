using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Enemies;

/// <summary>
/// A level's enemies and its chase: residents wait at the resident spots until they see or hear the player (or are
/// hurt); after the chase timer, waves come through the entry portal, larger and more often each time, and always know
/// where the player is. Each enemy routes over the level's navigation, keeps apart from the others, and fights with its
/// kind's action through the shared pipeline. Everything advances only on the world seconds passed to <see cref="Step"/>.
/// </summary>
internal sealed class EnemyDirector : IDisposable
{
    private const ulong FirstEntity = 20_000;
    private const ulong FirstObjectId = 4_000_000;
    private const float FaceTurn = MathF.PI * 4; // radians a second an enemy turns to face its target
    private const float WaypointReached = .6f;

    private readonly IEngineContext engine;
    private readonly EnemyCatalog catalog;
    private readonly ActionCatalog actions;
    private readonly MechanicsDefinition mechanics;
    private readonly Dictionary<string, CharacterControllerConfig> bodies = [];
    private readonly Dictionary<string, (Appearance Normal, Appearance Windup)> looks = [];
    private readonly Appearance bolt;
    private readonly List<Enemy> enemies = [];
    private readonly List<Enemy> fallen = [];
    private ActionResolution? resolution;
    private LevelNavigation? navigation;
    private SpatialSession? session;
    private Vector3 entry;
    private Random random = new(0);
    private int depth, wave;
    private float chaseLeft, waveLeft;
    private ulong nextEntity = FirstEntity;

    internal EnemyDirector(IEngineContext engine, EnemyCatalog catalog, ActionCatalog actions, MechanicsDefinition mechanics)
    {
        this.engine = engine;
        this.catalog = catalog;
        this.actions = actions;
        this.mechanics = mechanics;
        CharacterControllerConfig baseline = engine.Spatial.DefaultCharacterControllerConfig();
        foreach (EnemyKind kind in catalog.Kinds)
        {
            float radius = MathF.Min(kind.Look.Size[0], kind.Look.Size[2]) / 2;
            CharacterControllerConfig body = baseline with
            {
                Shape = baseline.Shape with { StandingHeight = kind.Look.Size[1], CrouchedHeight = kind.Look.Size[1] * .6f, Radius = radius }, // enemies never crouch; the Engine wants a lower crouch
                Ground = baseline.Ground with { ForwardSpeed = kind.Speed, BackwardSpeed = kind.Speed, StrafeSpeed = kind.Speed },
                Air = baseline.Air with { MaximumSpeed = kind.Speed, WishSpeedCap = kind.Speed },
            };
            engine.Spatial.ValidateCharacterControllerConfig(body);
            bodies[kind.Id] = body;
            looks[kind.Id] = (Primitive(kind.Look.Color), Primitive(kind.Look.WindupColor));
        }
        bolt = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Sphere, false, new Color(.6f, .9f, 1, 1)));
    }

    /// <summary>The living enemies, for the player's actions to hit.</summary>
    internal IReadOnlyList<IActionActor> Living => enemies;
    internal bool Active => resolution is not null;

    /// <summary>Enemies that fell since this was last read (what they drop is the loot owner's).</summary>
    internal IReadOnlyList<Enemy> TakeFallen()
    {
        Enemy[] taken = [.. fallen];
        fallen.Clear();
        return taken;
    }

    /// <summary>A level begins: its residents take their spots and the chase timer starts, by the run's depth.</summary>
    internal void Enter(LevelScene level, int runDepth, int seed)
    {
        Clear();
        session = level.Session;
        navigation = level.Navigation;
        resolution = new ActionResolution(engine, level.Session, mechanics);
        entry = level.Points.Entry;
        depth = runDepth;
        random = new Random(seed);
        chaseLeft = catalog.Chase.ChaseSeconds.At(depth);
        wave = 0;
        waveLeft = 0;
        foreach (Vector3 spot in level.Points.Residents)
            for (int i = 0; i < catalog.Chase.ResidentsPerSpot; i++)
                Spawn(ChaseTuning.Pick(catalog.Chase.ResidentKinds, random), spot, EnemyRole.Resident);
    }

    /// <summary>Sets the time left before the chase begins (developer overrides); 0 brings the first wave at once.</summary>
    internal void SetChase(float seconds)
    {
        chaseLeft = seconds;
        if (seconds <= 0) waveLeft = 0;
    }

    /// <summary>One line per enemy: kind, role, awareness, health and where it stands relative to a point.</summary>
    internal IEnumerable<string> Describe(Vector3 from) => enemies.Select(e => FormattableString.Invariant(
        $"{e.Entity} {e.Kind.Id} {e.Role}{(e.Aware ? " aware" : "")} health {e.Stats.Track(ActorStats.HealthTrack).ValueInt} at {e.Feet.X:0.0},{e.Feet.Z:0.0} ({Vector3.Distance(e.Feet, from):0.0} m){(e.User.Busy ? " " + e.User.Phase : "")}{(e.Waypoint is { } w ? $" → {w.X:0.0},{w.Z:0.0} ({e.Route})" : "")}"));

    /// <summary>No level (the gallery): no enemies and no chase.</summary>
    internal void Leave()
    {
        Clear();
        resolution = null;
        navigation = null;
        session = null;
    }

    /// <summary>
    /// Advances the chase, every enemy's senses, route, body and action, and their shots, by world seconds.
    /// <paramref name="player"/> is who they hunt and hit.
    /// </summary>
    internal void Step(float seconds, IActionActor player)
    {
        if (resolution is null || navigation is null || session is null) return;
        Escalate(seconds);
        Vector3 target = player.Position;
        foreach (Enemy enemy in enemies.ToArray())
        {
            enemy.Stats.Effects.Advance(seconds);
            if (!enemy.Alive) continue;
            Sense(enemy, player);
            ActionDefinition action = actions.Action(enemy.Kind.Action)!;
            float distance = Vector3.Distance(Planar(enemy.Feet), Planar(target));
            bool sees = resolution.Clear(enemy.Eye, player.Eye);
            if (enemy.Aware) Face(enemy, target, seconds);
            if (enemy.Aware && player.Alive && !enemy.Stats.Effects.Stunned && enemy.User.Readiness(action) == ActionRefusal.None
                && distance <= enemy.Kind.AttackRange && sees)
                enemy.User.Begin(action, Vector3.Normalize(player.Eye - enemy.Eye), (float)enemy.Stats.Stat(ActorStats.ActionTimeStat).Value);
            Move(enemy, target, distance, sees, seconds);
            if (enemy.User.Step(seconds) is { } landed && enemy.Alive)
                resolution.Land(landed, enemy, enemy.User.Aim, [player]);
        }
        resolution.Step(seconds, [player]);
        foreach (Enemy enemy in enemies.Where(e => !e.Alive).ToArray())
        {
            enemies.Remove(enemy);
            fallen.Add(enemy);
        }
    }

    /// <summary>Enemies (a windup shows in the kind's warning colour) and their shots, as they stand now.</summary>
    internal IEnumerable<AppearanceFact> Facts()
    {
        ulong id = FirstObjectId;
        foreach (Enemy enemy in enemies)
        {
            (Appearance normal, Appearance windup) = looks[enemy.Kind.Id];
            Appearance shown = enemy.User.Phase == ActionPhase.Windup ? windup : normal;
            yield return new AppearanceFact(id++, false, 0, enemy.Transform, shown, true, RenderLayer.Scene);
        }
        foreach (Projectile shot in resolution?.Projectiles ?? [])
            yield return new AppearanceFact(id++, false, 0, new Transform(shot.Position, Quaternion.Identity, new Vector3(.25f)), bolt, true, RenderLayer.Scene);
    }

    /// <summary>The chase line (time to the chase or the next wave, or the warning) and how many enemies are about.</summary>
    internal (string Chase, string Hostiles) Hud()
    {
        if (resolution is null) return ("", "");
        ChaseMessages text = catalog.Text;
        string chase = chaseLeft > 0
            ? chaseLeft <= catalog.Chase.WarningSeconds ? text.Warning : Template.Fill(text.Chase, ("seconds", MathF.Ceiling(chaseLeft)))
            : Template.Fill(text.Wave, ("wave", wave + 1), ("seconds", MathF.Ceiling(waveLeft)));
        return (chase, enemies.Count == 0 ? "" : Template.Fill(text.Alive, ("count", enemies.Count)));
    }

    public void Dispose()
    {
        foreach ((Appearance normal, Appearance windup) in looks.Values)
        {
            normal.Dispose();
            windup.Dispose();
        }
        bolt.Dispose();
    }

    // The chase timer, then waves through the entry portal, each larger and sooner than the last.
    private void Escalate(float seconds)
    {
        if (chaseLeft > 0)
        {
            chaseLeft -= seconds;
            if (chaseLeft > 0) return;
        }
        waveLeft -= seconds;
        if (waveLeft > 0) return;
        ChaseTuning chase = catalog.Chase;
        int size = (int)MathF.Round(chase.WaveSize.At(depth) + chase.WaveGrowth * wave);
        for (int i = 0; i < size && enemies.Count < chase.MaximumAlive; i++)
            Spawn(ChaseTuning.Pick(chase.WaveKinds, random), entry, EnemyRole.Chaser);
        wave++;
        waveLeft = MathF.Max(chase.WaveSeconds.Minimum, chase.WaveSeconds.At(depth) - chase.WaveShrink * wave);
    }

    private void Spawn(string kindId, Vector3 around, EnemyRole role)
    {
        EnemyKind kind = catalog.Kind(kindId);
        float angle = (float)(random.NextDouble() * Math.Tau), reach = (float)random.NextDouble() * catalog.Chase.SpawnSpread;
        Vector3 at = around + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * reach;
        Vector3 feet = navigation!.Nearest(at) ?? around;
        enemies.Add(new Enemy(nextEntity++, kind, mechanics, feet, role) { Yaw = (float)(random.NextDouble() * Math.Tau) });
    }

    // A resident notices the player it can see within its sight and field of view, or hears within its hearing, or that hurts it.
    private void Sense(Enemy enemy, IActionActor player)
    {
        int health = enemy.Stats.Track(ActorStats.HealthTrack).ValueInt;
        if (health < enemy.LastHealth && enemy.LastHealth != int.MaxValue) enemy.Aware = true;
        enemy.LastHealth = health;
        if (enemy.Aware) return;
        Vector3 to = player.Position - enemy.Position;
        float distance = to.Length();
        if (distance <= enemy.Kind.Hearing) { enemy.Aware = true; return; }
        if (distance > enemy.Kind.Sight) return;
        Vector3 facing = new(MathF.Sin(enemy.Yaw), 0, -MathF.Cos(enemy.Yaw));
        float cosine = Vector3.Dot(Vector3.Normalize(to with { Y = 0 }), facing);
        if (cosine >= MathF.Cos(enemy.Kind.FieldOfViewDegrees * MathF.PI / 360) && resolution!.Clear(enemy.Eye, player.Eye)) enemy.Aware = true;
    }

    // Turns the enemy toward a point at a bounded rate.
    private static void Face(Enemy enemy, Vector3 target, float seconds)
    {
        Vector3 to = target - enemy.Position;
        if (to.X * to.X + to.Z * to.Z < 1e-4f) return;
        float want = MathF.Atan2(to.X, -to.Z);
        float turn = MathF.IEEERemainder(want - enemy.Yaw, MathF.Tau);
        enemy.Yaw += Math.Clamp(turn, -FaceTurn * seconds, FaceTurn * seconds);
    }

    // Closes along its route (or backs off, for a kind that keeps its distance), standing still while it acts or is stunned.
    private void Move(Enemy enemy, Vector3 target, float distance, bool sees, float seconds)
    {
        Vector3 wish = Vector3.Zero;
        bool acting = enemy.User.Busy || enemy.Stats.Effects.Stunned;
        if (enemy.Aware && !acting)
        {
            if (enemy.Kind.KeepAway > 0 && distance < enemy.Kind.KeepAway && sees) wish = Planar(enemy.Feet - target);
            else if (distance > enemy.Kind.AttackRange * .85f || !sees) wish = Route(enemy, target, seconds);
        }
        foreach (Enemy other in enemies)
        {
            if (other == enemy || !other.Alive) continue;
            Vector3 apart = Planar(enemy.Feet - other.Feet);
            float gap = apart.Length();
            if (gap > 1e-3f && gap < catalog.Chase.Separation) wish += apart / gap * (1 - gap / catalog.Chase.Separation);
        }
        float pace = (float)enemy.Stats.Pace;
        Vector2 movement = Vector2.Zero;
        float heading = enemy.Yaw;
        if (wish.LengthSquared() > 1e-4f)
        {
            heading = MathF.Atan2(wish.X, -wish.Z);
            movement = new Vector2(0, MathF.Min(1, wish.Length()) * pace);
        }
        CharacterStepReceipt receipt = engine.Spatial.ProposeCharacterStep(new CharacterStepRequest(session!, enemy.Centre, enemy.Motion, default,
            ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty, bodies[enemy.Kind.Id],
            new CharacterControllerCommand(movement, heading, false, false, false, enemy.Stats.Effects.KnockbackVelocity(enemy.Feet), Vector3.Zero,
                seconds, ++enemy.Sequence)));
        enemy.Centre = receipt.Transform.Translation;
        enemy.Motion = receipt.Motion;
        if (!enemy.Aware && movement != Vector2.Zero) enemy.Yaw = heading;
    }

    // The direction to the next waypoint toward the target, re-planned on the tuning's interval or when it is reached.
    private Vector3 Route(Enemy enemy, Vector3 target, float seconds)
    {
        enemy.RepathIn -= seconds;
        if (enemy.Waypoint is not { } waypoint || enemy.RepathIn <= 0 || Vector3.Distance(Planar(waypoint), Planar(enemy.Feet)) < WaypointReached)
        {
            NavigationStepResult step = navigation!.Step(enemy.Feet, target with { Y = enemy.Feet.Y }, 3f);
            enemy.Route = step.Outcome;
            // No route (the target stands where the enemy's floor does not reach): head for the nearest floor that does, or keep
            // the last waypoint rather than walking straight at a wall.
            enemy.Waypoint = step.Outcome == NavigationPathOutcome.Reached ? step.NextWaypoint : step.NearestPresent ? step.Nearest : enemy.Waypoint ?? enemy.Feet;
            enemy.RepathIn = catalog.Chase.RepathSeconds;
            waypoint = enemy.Waypoint.Value;
        }
        Vector3 to = Planar(waypoint - enemy.Feet);
        return to.LengthSquared() < 1e-6f ? Vector3.Zero : Vector3.Normalize(to);
    }

    private void Clear()
    {
        enemies.Clear();
        fallen.Clear();
        resolution?.Clear();
    }

    private Appearance Primitive(float[] rgb) =>
        engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Authored.Color(rgb)));

    private static Vector3 Planar(Vector3 v) => v with { Y = 0 };
}
