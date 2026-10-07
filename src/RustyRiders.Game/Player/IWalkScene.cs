using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Player;

/// <summary>A scene the walker can stand in: its collision session, spawn and what the HUD says about it.</summary>
internal interface IWalkScene : IDisposable
{
    SpatialSession Session { get; }
    Vector3 SpawnFeet { get; }
    float SpawnYawDegrees { get; }
    string Status { get; }
    IReadOnlyList<string> Problems { get; }

    /// <summary>What is at or nearest to a position, for the HUD.</summary>
    string Describe(Vector3 position);

    /// <summary>The scene's appearances as they stand now; the product publishes them with combat's in one snapshot.</summary>
    IEnumerable<AppearanceFact> Facts { get; }

    /// <summary>Moves what the scene animates on world time to <paramref name="worldSeconds"/>.</summary>
    void Animate(double worldSeconds);
}
