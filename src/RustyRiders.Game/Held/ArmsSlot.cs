using Rusty.Engine;

namespace RustyRiders.Game.Held;

/// <summary>
/// The arms drawn now, by one rig profile, and the switch to another: each rig is drawn on its own object (an object
/// keeps one animation instance), alternating between two, and replaced arms are published hidden once, then left out,
/// and released after that snapshot. A rig whose model does not open (an old-game model not imported here) is reported
/// and the generated arms draw instead. Split from <see cref="HeldView"/>.
/// </summary>
internal sealed class ArmsSlot : IDisposable
{
    /// <summary>The rig profile that draws the arms when none other is chosen or opens: the generated arms, kept in git.</summary>
    internal const string FallbackRig = "generated";

    private readonly IEngineContext engine;
    private readonly HeldCatalog held;
    private readonly ulong firstObject;
    private readonly List<string> problems = [];
    private ArmsView? retired;

    /// <param name="firstObject">The first of the two objects the arms alternate between.</param>
    internal ArmsSlot(IEngineContext engine, HeldCatalog held, ulong firstObject)
    {
        this.engine = engine;
        this.held = held;
        this.firstObject = firstObject;
        Drawn = Open(held.Arms.Rig, firstObject) ?? Open(FallbackRig, firstObject)
            ?? throw new InvalidOperationException($"The fallback arms did not open: {string.Join("; ", problems)}");
    }

    /// <summary>The arms drawn now.</summary>
    internal ArmsView Drawn { get; private set; }

    /// <summary>Rigs whose model did not open.</summary>
    internal IReadOnlyList<string> Problems => problems;

    /// <summary>
    /// Developer arms comparison: draws the arms by another rig profile, with the same hold; returns why it could not,
    /// or null. The caller publishes, after which the previous arms are released.
    /// </summary>
    internal string? Show(string id)
    {
        if (!held.Rigs.ContainsKey(id)) return $"Unknown rig '{id}'. Rigs: {string.Join(", ", held.Rigs.Keys)}.";
        if (id == Drawn.Id) return null;
        int known = problems.Count;
        if (Open(id, Drawn.Object == firstObject ? firstObject + 1 : firstObject) is not { } next) return problems[known];
        retired?.Dispose();
        (retired, Drawn) = (Drawn, next);
        held.Arms.Rig = id;
        return null;
    }

    /// <summary>The replaced arms' fact while they still need publishing hidden, once.</summary>
    internal AppearanceFact? Retired => retired is { Published: false } arms ? arms.Fact(false) : null;

    /// <summary>After a snapshot is published: marks replaced arms published hidden, or releases them once they were.</summary>
    internal void Published()
    {
        if (retired is null) return;
        if (!retired.Published)
        {
            retired.Published = true;
            return;
        }
        retired.Dispose();
        retired = null;
    }

    public void Dispose()
    {
        Drawn.Dispose();
        retired?.Dispose();
    }

    private ArmsView? Open(string id, ulong objectId)
    {
        try
        {
            return new ArmsView(engine, held.Arms, held.Rigs[id], id, objectId);
        }
        catch (Exception error) when (error is EngineCallException or InvalidOperationException)
        {
            problems.Add($"arms rig {id}: {error.Message}");
            return null;
        }
    }
}
