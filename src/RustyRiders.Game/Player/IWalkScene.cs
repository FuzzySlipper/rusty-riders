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

    void Publish();
}
