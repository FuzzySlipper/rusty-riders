namespace RustyRiders.Game.Content;

/// <summary>
/// An authored value that changes by <see cref="PerDepth"/> for each level a run has pushed through, never below
/// <see cref="Minimum"/>: chase timers, wave sizes, reward multipliers.
/// </summary>
internal sealed record DepthCurve(float Base, float PerDepth, float Minimum)
{
    internal float At(int depth) => MathF.Max(Minimum, Base + PerDepth * depth);
}
