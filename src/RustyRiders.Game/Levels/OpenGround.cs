using System.Numerics;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Open ground as cells on a square grid (Unity space: x east, z north), each with its ceiling height: how the
/// generated builds trace its outlines, and how they place a Unity-space point in glTF space.
/// </summary>
internal static class OpenGround
{
    /// <summary>
    /// The open ground's outlines as closed loops of cell corners (cell units, corner (i, j) at (i - ½, j - ½)), each
    /// walked with open ground on its left. Each corner carries the ceiling height of the open cell along the edge
    /// that leaves it; straight runs of one height are merged.
    /// </summary>
    internal static List<List<(Vector2 Point, float Height)>> Outlines(Dictionary<(int X, int Z), float> open)
    {
        Dictionary<(int X, int Z), List<((int X, int Z) End, float Height)>> outgoing = [];
        void Edge((int, int) from, (int, int) to, float height)
        {
            if (!outgoing.TryGetValue(from, out var list)) outgoing[from] = list = [];
            list.Add((to, height));
        }
        foreach (((int x, int z), float h) in open)
        {
            if (!open.ContainsKey((x, z - 1))) Edge((x, z), (x + 1, z), h);
            if (!open.ContainsKey((x + 1, z))) Edge((x + 1, z), (x + 1, z + 1), h);
            if (!open.ContainsKey((x, z + 1))) Edge((x + 1, z + 1), (x, z + 1), h);
            if (!open.ContainsKey((x - 1, z))) Edge((x, z + 1), (x, z), h);
        }

        List<List<(Vector2, float)>> loops = [];
        while (outgoing.FirstOrDefault(pair => pair.Value.Count > 0) is { Value: not null } first)
        {
            List<((int X, int Z) Corner, float Height)> chain = [];
            (int X, int Z) start = first.Key, at = start;
            (int X, int Z) heading = (0, 0); // none yet: any edge may start the loop
            do
            {
                List<((int X, int Z) End, float Height)> choices = outgoing[at];
                // Where two cells touch only at this corner, keep turning left so each loop stays one outline.
                int pick = 0;
                for (int i = 1; i < choices.Count; i++)
                    if (Turn(heading, Direction(at, choices[i].End)) > Turn(heading, Direction(at, choices[pick].End))) pick = i;
                ((int X, int Z) end, float height) = choices[pick];
                choices.RemoveAt(pick);
                chain.Add((at, height));
                heading = Direction(at, end);
                at = end;
            }
            while (at != start);

            List<(Vector2, float)> loop = [];
            for (int i = 0; i < chain.Count; i++)
            {
                ((int X, int Z) corner, float height) = chain[i];
                ((int X, int Z) previous, float previousHeight) = chain[(i + chain.Count - 1) % chain.Count];
                (int X, int Z) next = chain[(i + 1) % chain.Count].Corner;
                bool straight = Direction(previous, corner) == Direction(corner, next) && previousHeight == height;
                if (!straight) loop.Add((new Vector2(corner.X - .5f, corner.Z - .5f), height));
            }
            loops.Add(loop);
        }
        return loops;
    }

    private static (int X, int Z) Direction((int X, int Z) from, (int X, int Z) to) => (Math.Sign(to.X - from.X), Math.Sign(to.Z - from.Z));

    /// <summary>1 for a left turn (seen from above, x east and z north), 0 straight, -1 right.</summary>
    private static int Turn((int X, int Z) a, (int X, int Z) b) => Math.Sign(a.X * b.Z - a.Z * b.X);

    /// <summary>A Unity-space point (metres on the ground plane) and height in glTF space: Unity is the X mirror.</summary>
    internal static Vector3 Gltf(Vector2 point, float y) => new(-point.X, y, point.Y);
}
