using System.Text.Json.Serialization;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Mechanics;

/// <summary>Where in a hit's resolution a contribution acts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContributionStage>))]
internal enum ContributionStage
{
    /// <summary>Whether the hit lands at all: a contribution that prevents turns it aside, with no damage and no effects.</summary>
    Hit,
    /// <summary>The damage calculated before resistance: added, then multiplied.</summary>
    Damage,
    /// <summary>The damage about to be applied, after resistance and before wards and health: added, then multiplied.</summary>
    Applying,
    /// <summary>A hit that would take the last health: the target keeps <see cref="DamageContribution.Retain"/>, and the contribution is spent.</summary>
    Defeating,
}

/// <summary>Which side of a hit a contribution belongs to: the hit its bearer deals, or the one it takes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContributionSide>))]
internal enum ContributionSide { Outgoing, Incoming }

/// <summary>
/// One typed change to how a hit resolves, for hits of the named damage kinds (none is every kind), authored on a worn
/// item or carried by an effect. A hit gathers the user's outgoing contributions, then the target's incoming ones, at
/// each stage, around the one change to the target's health. Adapted from rusty-hotel's (itself from rusty-dagger's
/// <c>ICombatContribution</c>; docs/reuse.md).
/// </summary>
internal sealed record DamageContribution(ContributionStage Stage, ContributionSide Side, string[] Kinds, int Add = 0, float Multiply = 1,
    bool Prevent = false, int Retain = 0)
{
    internal bool Applies(ContributionStage stage, ContributionSide side, string kind) =>
        Stage == stage && Side == side && (Kinds.Length == 0 || Kinds.Contains(kind));

    /// <summary>A damage or applying amount after this contribution: added, then multiplied, never below nothing.</summary>
    internal float Change(float amount) => Math.Max(0, (amount + Add) * Multiply);

    /// <summary>Checks contributions authored in a file; a defeating contribution must come with an effect, which it spends.</summary>
    internal static void Validate(DamageContribution[] contributions, string path, string field, MechanicsDefinition mechanics, bool fromEffect)
    {
        for (int i = 0; i < contributions.Length; i++)
        {
            DamageContribution c = contributions[i];
            string at = $"{field}[{i}]";
            for (int k = 0; k < c.Kinds.Length; k++)
                Authored.Require(mechanics.DamageKind(c.Kinds[k]) is not null, path, $"{at}.kinds[{k}]", $"unknown damage kind '{c.Kinds[k]}'.");
            Authored.AtLeast(path, $"{at}.multiply", c.Multiply, 0);
            switch (c.Stage)
            {
                case ContributionStage.Hit:
                    Authored.Require(c.Prevent && c.Add == 0 && c.Multiply == 1 && c.Retain == 0, path, at, "a hit contribution only prevents.");
                    break;
                case ContributionStage.Defeating:
                    Authored.Require(fromEffect, path, at, "a defeating contribution is spent when used, so it comes with an effect.");
                    Authored.Require(c.Side == ContributionSide.Incoming && c.Retain >= 1 && !c.Prevent && c.Add == 0 && c.Multiply == 1,
                        path, at, "a defeating contribution is incoming and retains at least 1 health.");
                    break;
                default:
                    Authored.Require(!c.Prevent && c.Retain == 0, path, at, "a damage or applying contribution adds and multiplies.");
                    break;
            }
        }
    }
}

/// <summary>A contribution in force on an actor, and how to spend its source when it is used up (a defeating effect).</summary>
internal sealed record ActiveContribution(DamageContribution Contribution, Action? Spend = null);
