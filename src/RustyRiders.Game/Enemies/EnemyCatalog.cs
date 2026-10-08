using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Enemies;

/// <summary>
/// The enemies' authored definitions: their kinds (content/enemies/kinds.json), how the chase escalates
/// (content/enemies/chase.json) and the chase HUD text (content/enemies/messages.json).
/// </summary>
internal sealed record EnemyCatalog(EnemyKind[] Kinds, ChaseTuning Chase, ChaseMessages Text)
{
    internal const string KindsPath = "enemies/kinds.json";

    internal static EnemyCatalog Load(IEngineContext engine, MechanicsDefinition mechanics, ActionCatalog actions)
    {
        EnemyKind[] kinds = Authored.Read(engine, KindsPath, EnemyJson.Default.EnemyKindFile).Kinds;
        Authored.Require(kinds.Length > 0, KindsPath, "kinds", "must name at least one kind.");
        string? repeated = kinds.GroupBy(k => k.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, KindsPath, "kinds", $"id '{repeated}' appears more than once.");
        for (int i = 0; i < kinds.Length; i++) kinds[i].Validate($"kinds[{i}]", mechanics, actions);
        ChaseTuning chase = Authored.Read(engine, ChaseTuning.Path, EnemyJson.Default.ChaseTuning);
        chase.Validate(kinds);
        ChaseMessages text = Authored.Read(engine, ChaseMessages.Path, EnemyJson.Default.ChaseMessages);
        text.Validate();
        return new EnemyCatalog(kinds, chase, text);
    }

    internal EnemyKind Kind(string id) => Kinds.First(k => k.Id == id);
}

/// <summary>
/// One kind of enemy, composed from typed parts: its look (a box, and the colour it flashes while winding up), its stats,
/// the action it fights with and the reach it uses it from, the distance a ranged kind keeps (0 closes in), its speed in
/// metres a second, and its senses: sight distance and field of view, and the radius it hears the player within.
/// </summary>
internal sealed record EnemyKind(string Id, string Name, EnemyLook Look, ActorStatBlock Stats, string Action, float AttackRange,
    float KeepAway, float Speed, float Sight, float FieldOfViewDegrees, float Hearing)
{
    internal void Validate(string at, MechanicsDefinition mechanics, ActionCatalog actions)
    {
        const string path = EnemyCatalog.KindsPath;
        Template.Plain(path, ($"{at}.name", Name));
        Authored.Point(path, $"{at}.look.size", Look.Size);
        Authored.Colour(path, $"{at}.look.color", Look.Color);
        Authored.Colour(path, $"{at}.look.windupColor", Look.WindupColor);
        mechanics.Validate(path, $"{at}.stats", Stats);
        actions.Require(path, $"{at}.action", Action);
        Authored.Positive(path, $"{at}.attackRange", AttackRange);
        Authored.Within(path, $"{at}.keepAway", KeepAway, 0, AttackRange);
        Authored.Positive(path, $"{at}.speed", Speed);
        Authored.Positive(path, $"{at}.sight", Sight);
        Authored.Within(path, $"{at}.fieldOfViewDegrees", FieldOfViewDegrees, 1, 360);
        Authored.AtLeast(path, $"{at}.hearing", Hearing, 0);
    }
}

internal sealed record EnemyLook(float[] Size, float[] Color, float[] WindupColor);

internal sealed record EnemyKindFile(EnemyKind[] Kinds);

/// <summary>A kind's share of a random pick.</summary>
internal sealed record KindWeight(string Kind, int Weight);

/// <summary>
/// How a level's chase escalates, in world seconds. On arrival a timer of <see cref="ChaseSeconds"/> runs; then waves
/// come through the entry portal, the first <see cref="WaveSize"/> strong and each later one <see cref="WaveGrowth"/>
/// larger, <see cref="WaveSeconds"/> apart and each interval <see cref="WaveShrink"/> shorter (never below the curve's
/// minimum), up to <see cref="MaximumAlive"/> enemies at once. Every curve takes the run's depth. Residents wait at each
/// resident spot, <see cref="ResidentsPerSpot"/> to a spot. Enemies spawn within <see cref="SpawnSpread"/> metres of their
/// point, re-plan their route every <see cref="RepathSeconds"/>, keep <see cref="Separation"/> metres from each other,
/// and the HUD warns <see cref="WarningSeconds"/> before the chase begins.
/// </summary>
internal sealed record ChaseTuning(DepthCurve ChaseSeconds, DepthCurve WaveSize, float WaveGrowth, DepthCurve WaveSeconds, float WaveShrink,
    int MaximumAlive, KindWeight[] WaveKinds, int ResidentsPerSpot, KindWeight[] ResidentKinds, float SpawnSpread, float RepathSeconds,
    float Separation, float WarningSeconds)
{
    internal const string Path = "enemies/chase.json";

    internal void Validate(EnemyKind[] kinds)
    {
        Authored.Positive(Path, "chaseSeconds.minimum", ChaseSeconds.Minimum);
        Authored.Positive(Path, "waveSize.minimum", WaveSize.Minimum);
        Authored.AtLeast(Path, "waveGrowth", WaveGrowth, 0);
        Authored.Positive(Path, "waveSeconds.minimum", WaveSeconds.Minimum);
        Authored.AtLeast(Path, "waveShrink", WaveShrink, 0);
        Authored.Require(MaximumAlive >= 1, Path, "maximumAlive", "must be at least 1.");
        Authored.Require(ResidentsPerSpot >= 0, Path, "residentsPerSpot", "must not be negative.");
        Weights("waveKinds", WaveKinds);
        Weights("residentKinds", ResidentKinds);
        Authored.AtLeast(Path, "spawnSpread", SpawnSpread, 0);
        Authored.Positive(Path, "repathSeconds", RepathSeconds);
        Authored.AtLeast(Path, "separation", Separation, 0);
        Authored.AtLeast(Path, "warningSeconds", WarningSeconds, 0);

        void Weights(string field, KindWeight[] weights)
        {
            Authored.Require(weights.Length > 0 && weights.Sum(w => w.Weight) > 0, Path, field, "must give some kind a positive weight.");
            for (int i = 0; i < weights.Length; i++)
            {
                Authored.Require(kinds.Any(k => k.Id == weights[i].Kind), Path, $"{field}[{i}].kind", $"unknown kind '{weights[i].Kind}'.");
                Authored.Require(weights[i].Weight >= 0, Path, $"{field}[{i}].weight", "must not be negative.");
            }
        }
    }

    internal static string Pick(KindWeight[] weights, Random random)
    {
        int roll = random.Next(weights.Sum(w => w.Weight));
        foreach (KindWeight w in weights)
            if ((roll -= w.Weight) < 0) return w.Kind;
        return weights[^1].Kind;
    }
}

/// <summary>The chase HUD's text, as templates.</summary>
internal sealed record ChaseMessages(string Chase, string Wave, string Warning, string Alive)
{
    internal const string Path = "enemies/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "chase", Chase, "seconds");
        Template.Check(Path, "wave", Wave, "wave", "seconds");
        Template.Check(Path, "warning", Warning);
        Template.Check(Path, "alive", Alive, "count");
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EnemyKindFile))]
[JsonSerializable(typeof(ChaseTuning))]
[JsonSerializable(typeof(ChaseMessages))]
internal sealed partial class EnemyJson : JsonSerializerContext;
