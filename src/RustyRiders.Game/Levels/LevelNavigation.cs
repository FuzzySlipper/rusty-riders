using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>
/// The level's Engine navigation: a planar projection derived from the session's collision for the body enemies walk
/// with, and the queries other owners use on it. A query names live foot positions; the Engine resolves each to the
/// nearest support within the tuning's snap. Routes are suggestions: character steps stay the authority for movement.
/// Derived at once, or on Engine workers (a level built in the background): then it answers nothing until
/// <see cref="Installed"/>.
/// </summary>
internal sealed class LevelNavigation
{
    private const ulong GridId = 1;

    private readonly IEngineContext engine;
    private readonly SpatialSession session;
    private readonly NavigationTuning tuning;

    /// <param name="deferred">Whether the grid is derived on the Engine's workers from the session's collision as it stands.</param>
    internal LevelNavigation(IEngineContext engine, SpatialSession session, NavigationTuning tuning, Vector3 floorMin, Vector3 floorMax, bool deferred)
    {
        this.engine = engine;
        this.session = session;
        this.tuning = tuning;
        CharacterControllerConfig character = engine.Spatial.DefaultCharacterControllerConfig();
        character = character with
        {
            Shape = character.Shape with { StandingHeight = tuning.AgentHeight, Radius = tuning.AgentRadius },
            Surface = character.Surface with { MaximumStepHeight = tuning.MaximumStepHeight },
        };
        CollisionNavigationConfig config = engine.Spatial.DefaultCollisionNavigationConfig() with
        {
            GridId = GridId,
            CellSize = tuning.CellSize,
            MaximumCells = tuning.MaximumCells,
            Character = character,
            MaximumDrop = tuning.MaximumStepHeight,
            DiagonalNeighbors = tuning.DiagonalNeighbors,
            SnapAcross = tuning.SnapAcrossMetres,
        };
        Receipt = engine.Spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(session,
            floorMin - Vector3.UnitY * tuning.BelowFloorMetres, floorMax + Vector3.UnitY * tuning.AboveFloorMetres, config, deferred));
    }

    internal CollisionNavigationReplaceReceipt Receipt { get; private set; }

    /// <summary>Whether the grid is derived and answers queries; a derivation the Engine refused fails the level.</summary>
    internal bool Installed()
    {
        if (!Receipt.Pending) return true;
        CollisionNavigationReadout read = engine.Spatial.ReadCollisionNavigation(new CollisionNavigationReadRequest(session, GridId));
        if (read.Refused) throw new InvalidOperationException("The level's navigation could not be derived; the host log says why.");
        if (read.Pending) return false;
        Receipt = read.Installed;
        return true;
    }

    internal string Summary => FormattableString.Invariant(
        $"nav {Receipt.WalkableCellCount} cells, {Receipt.ComponentCount} parts, {Receipt.DerivationMicroseconds / 1000.0:0} ms");

    /// <summary>One bounded step from <paramref name="fromFeet"/> toward <paramref name="targetFeet"/> along the navigation.</summary>
    internal NavigationStepResult Step(Vector3 fromFeet, Vector3 targetFeet, float maximumStep) =>
        engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(session, fromFeet, targetFeet, maximumStep, tuning.QueryVisitedCells));

    /// <summary>
    /// The standing point navigation resolves a position to (the nearest support within the snap), or null when there is
    /// none: a step to itself reaches at once, and its waypoint is that support.
    /// </summary>
    internal Vector3? Nearest(Vector3 position)
    {
        NavigationStepResult result = Step(position, position, tuning.CellSize);
        return result.Outcome == NavigationPathOutcome.Reached ? result.NextWaypoint : null;
    }

    /// <summary>
    /// How a walking body at <paramref name="fromFeet"/> fares toward <paramref name="targetFeet"/>, searching the whole
    /// grid if need be (a load-time check, not a per-step query).
    /// </summary>
    internal NavigationStepResult Route(Vector3 fromFeet, Vector3 targetFeet) =>
        engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(session, fromFeet, targetFeet, tuning.CellSize,
            checked((uint)Math.Max(tuning.QueryVisitedCells, Receipt.WalkableCellCount))));
}
