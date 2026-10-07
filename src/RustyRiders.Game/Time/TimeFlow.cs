using Rusty.Engine;

namespace RustyRiders.Game.Time;

/// <summary>What the player is doing this update that decides how fast the world runs.</summary>
/// <param name="Realtime">A mode outside play (the gallery, free flight) that runs the world at realtime.</param>
/// <param name="Movement">Movement input magnitude, 0 to 1.</param>
internal readonly record struct TimeDemand(bool Realtime, float Movement, bool Sprinting, bool Airborne, bool Wait);

/// <summary>
/// The product's gameplay-time policy over the Engine's <see cref="IGameplayTimeService"/>, the one world clock:
/// the world holds while the player stands still, runs while they move, and advances by what an action costs. It
/// chooses each update from the player's demand; an advance in progress (a wait or an action) runs to its end before
/// movement chooses again.
/// </summary>
internal sealed class TimeFlow
{
    private readonly IEngineContext engine;

    internal TimeFlow(IEngineContext engine, TimeTuning tuning)
    {
        this.engine = engine;
        Tuning = tuning;
        engine.GameplayTime.SetRate(tuning.HeldRate); // opt in: every observation now updates
    }

    internal TimeTuning Tuning { get; }

    /// <summary>The selection in force after this update's admission, as the update reported it.</summary>
    internal TimeState State { get; private set; }

    /// <summary>World seconds elapsed, from admitted steps.</summary>
    internal double WorldSeconds { get; private set; }

    /// <summary>Records the update's selection; call before choosing.</summary>
    internal void Observe(ProductUpdateFacts facts)
    {
        WorldSeconds = (facts.SimulationStep + facts.AdmittedStepCount) * facts.FixedDeltaSeconds;
        State = new TimeState(facts.GameplayRate, facts.GameplayAdvanceRemainingSteps * facts.FixedDeltaSeconds);
    }

    /// <summary>Chooses the rate for the next observation from what the player is doing.</summary>
    internal void Choose(TimeDemand demand)
    {
        if (demand.Realtime)
        {
            engine.GameplayTime.RunRealtime();
            return;
        }
        if (State.AdvanceSeconds > 0) return; // a wait or an action is still being paid for
        if (demand.Wait)
        {
            Spend(Tuning.WaitSeconds);
            return;
        }
        double moving = demand.Sprinting ? Tuning.SprintRate : Tuning.WalkRate;
        double rate = Tuning.HeldRate + (moving - Tuning.HeldRate) * Math.Clamp(demand.Movement, 0, 1);
        if (demand.Airborne) rate = Math.Max(rate, Tuning.AirborneRate);
        engine.GameplayTime.SetRate(rate);
    }

    /// <summary>Lets <paramref name="seconds"/> of world time pass at realtime, then holds: what an action costs.</summary>
    internal void Spend(double seconds) => engine.GameplayTime.Advance(seconds);

    /// <summary>Back to the held rate, as after a restart (which returns the Engine to realtime).</summary>
    internal void Hold() => engine.GameplayTime.SetRate(Tuning.HeldRate);
}

/// <summary>The world's rate and how much of an advance is still to run.</summary>
internal readonly record struct TimeState(double Rate, double AdvanceSeconds)
{
    internal string Describe(double heldRate) => AdvanceSeconds > 0
        ? FormattableString.Invariant($"advancing · {AdvanceSeconds:0.0} s")
        : Rate <= heldRate ? "held" : Rate >= 1 ? "running" : FormattableString.Invariant($"running · {Rate:0.00}×");
}
