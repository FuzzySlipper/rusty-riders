using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Enemies;

/// <summary>
/// The enemies as their kinds' converted, animated models: each enemy has its own appearance and animation instance,
/// plays its kind's clip for what it is doing (standing, walking, flinching), samples the attack clip at its action's
/// progress so the windup runs up to the strike, and a fallen enemy plays its death once and stays where it fell. A kind
/// without a model, or whose model or clips did not open, is drawn as its box. Animation runs on the Engine's world
/// time, so it holds with the world. Adapted from rusty-hotel's <c>ResidentView</c> (docs/reuse.md).
/// </summary>
internal sealed class EnemyView : IDisposable
{
    private const string OldArtRoot = "old-art";
    private const ulong FirstObjectId = 4_000_000;
    // How fast an enemy must move for its walk to play (m/s), the crossfade between looping clips (s), and the share of
    // the attack clip after the strike that belongs to the commit (the rest is recovery).
    private const float Walking = .3f, Fade = .18f, CommitShare = .25f;

    private readonly IEngineContext engine;
    private readonly Dictionary<string, (RenderResource Model, float Scale)> models = [];
    private readonly Dictionary<string, (Appearance Normal, Appearance Windup)> boxes = [];
    private readonly Dictionary<ulong, Body> bodies = [];
    private readonly List<string> problems = [];

    internal EnemyView(IEngineContext engine, EnemyCatalog catalog)
    {
        this.engine = engine;
        foreach (EnemyKind kind in catalog.Kinds)
        {
            boxes[kind.Id] = (Box(kind.Look.Color), Box(kind.Look.WindupColor));
            if (kind.Look.Model is not { } model) continue;
            RenderResource? resource = null;
            try
            {
                resource = engine.Animation.OpenAnimatedMesh(new AnimatedMeshResourceRequest($"{OldArtRoot}/{model.Glb}"));
                string[] clips = engine.Animation.ReadClips(resource).ToArray().Select(c => c.Id).ToArray();
                string[] wanted = [model.Clips.Idle, model.Clips.Walk, model.Clips.Attack, model.Clips.Hit, model.Clips.Death];
                if (wanted.FirstOrDefault(c => !clips.Contains(c)) is { } missing)
                    throw new InvalidOperationException($"has no clip '{missing}' (content/{EnemyCatalog.KindsPath} '{kind.Id}'); its clips: {string.Join(", ", clips)}");
                AnimatedMeshInfo info = engine.Animation.ReadMeshInfo(resource);
                models[kind.Id] = (resource, model.Height / MathF.Max(.01f, info.BoundsMax.Y - info.BoundsMin.Y));
            }
            catch (Exception error) when (error is EngineCallException or InvalidOperationException)
            {
                resource?.Dispose();
                problems.Add($"{kind.Id} model {model.Glb}: {error.Message}; drawn as a box");
            }
        }
    }

    internal IReadOnlyList<string> Problems => problems;

    /// <summary>The living enemies and the fallen as they stand now; bodies of enemies gone from both are released.</summary>
    internal IEnumerable<AppearanceFact> Facts(IReadOnlyList<Enemy> living, IReadOnlyList<Enemy> fallen)
    {
        HashSet<ulong> present = [.. living.Select(e => e.Entity), .. fallen.Select(e => e.Entity)];
        foreach (ulong gone in bodies.Keys.Where(id => !present.Contains(id)).ToArray()) Release(gone);
        List<AppearanceFact> facts = [];
        foreach (Enemy enemy in living.Concat(fallen))
        {
            ulong id = FirstObjectId + enemy.Entity;
            if (Model(enemy) is { } body)
            {
                EnemyModel look = enemy.Kind.Look.Model!;
                Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -enemy.Yaw + look.YawDegrees * MathF.PI / 180);
                facts.Add(new AppearanceFact(id, false, 0, new Transform(enemy.Feet, turn, new Vector3(body.Scale)), body.Appearance, true, RenderLayer.Scene));
            }
            else if (enemy.Alive)
            {
                (Appearance normal, Appearance windup) = boxes[enemy.Kind.Id];
                facts.Add(new AppearanceFact(id, false, 0, enemy.Transform, enemy.User.Phase == ActionPhase.Windup ? windup : normal, true, RenderLayer.Scene));
            }
        }
        return facts;
    }

    /// <summary>Sets each modelled enemy's clip once its body is in the published snapshot.</summary>
    internal void Animate(IReadOnlyList<Enemy> living, IReadOnlyList<Enemy> fallen)
    {
        foreach (Enemy enemy in living.Concat(fallen))
        {
            if (!bodies.TryGetValue(enemy.Entity, out Body? body)) continue;
            body.Instance ??= engine.Animation.CreateInstance(new(body.Appearance, FirstObjectId + enemy.Entity));
            EnemyModel look = enemy.Kind.Look.Model!;
            EnemyClips clips = look.Clips;
            if (!enemy.Alive)
            {
                Play(body, clips.Death, AnimationLoopMode.Once);
                continue;
            }
            float progress = enemy.User.PhaseProgress, strike = look.StrikeAt;
            float? sample = enemy.User.Phase switch
            {
                ActionPhase.Windup => strike * progress,
                ActionPhase.Commit => strike + (1 - strike) * CommitShare * progress,
                ActionPhase.Recovery => strike + (1 - strike) * (CommitShare + (1 - CommitShare) * progress),
                _ => null,
            };
            if (sample is { } at)
            {
                // The attack clip starts once, then holds at the action's progress.
                Play(body, clips.Attack, AnimationLoopMode.Once);
                engine.Animation.SetPlayback(new(body.Instance, AnimationPlaybackKind.Sample, clips.Attack, AnimationLoopMode.Once, 1, 1, false, 0, false,
                    Math.Clamp(at, 0, 1)));
                continue;
            }
            Vector3 velocity = enemy.Motion.ControlledVelocity;
            bool walking = new Vector2(velocity.X, velocity.Z).Length() > Walking;
            if (enemy.HurtFor > 0) Play(body, clips.Hit, AnimationLoopMode.Once);
            else Play(body, walking ? clips.Walk : clips.Idle, AnimationLoopMode.Repeat);
        }
    }

    /// <summary>Releases every body (a level ends).</summary>
    internal void Clear()
    {
        foreach (ulong id in bodies.Keys.ToArray()) Release(id);
    }

    public void Dispose()
    {
        Clear();
        foreach ((RenderResource model, _) in models.Values) model.Dispose();
        foreach ((Appearance normal, Appearance windup) in boxes.Values)
        {
            normal.Dispose();
            windup.Dispose();
        }
    }

    private Body? Model(Enemy enemy)
    {
        if (bodies.TryGetValue(enemy.Entity, out Body? body)) return body;
        if (!models.TryGetValue(enemy.Kind.Id, out var model)) return null;
        body = new Body(engine.Animation.CreateAnimatedMeshAppearance(new AnimatedMeshAppearanceRequest(model.Model)), model.Scale);
        bodies[enemy.Entity] = body;
        return body;
    }

    private void Play(Body body, string clip, AnimationLoopMode loop)
    {
        if (body.Clip == clip) return;
        engine.Animation.SetPlayback(new(body.Instance!, AnimationPlaybackKind.Play, clip, loop, 1, 1, true, Fade, true, 0));
        body.Clip = clip;
    }

    private void Release(ulong id)
    {
        Body body = bodies[id];
        body.Instance?.Dispose();
        body.Appearance.Dispose();
        bodies.Remove(id);
    }

    private Appearance Box(float[] rgb) =>
        engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Authored.Color(rgb)));

    private sealed class Body(Appearance appearance, float scale)
    {
        internal Appearance Appearance { get; } = appearance;
        internal float Scale { get; } = scale;
        internal AnimationInstance? Instance { get; set; }
        internal string? Clip { get; set; }
    }
}
