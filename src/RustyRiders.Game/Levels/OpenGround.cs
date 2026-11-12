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

    /// <summary>The open ground's outlines (<see cref="Outlines"/>) with their diagonal cuts straightened (<see cref="Diagonals"/>).</summary>
    internal static List<List<(Vector2 Point, float Height)>> Walls(Dictionary<(int X, int Z), float> open) =>
        Outlines(open).Select(Diagonals).ToList();

    /// <summary>
    /// An outline with each staircase of single-square steps (a diagonally cut corner on the grid) straightened into one
    /// diagonal wall. The diagonal runs through the staircase's inner corners, so the open squares behind it still reach
    /// past it (their floor and ceiling hide behind the wall), and it meets the walls before and after the staircase
    /// where their lines cross it. A staircase needs at least four steps of one height between longer runs.
    /// </summary>
    internal static List<(Vector2 Point, float Height)> Diagonals(List<(Vector2 Point, float Height)> loop)
    {
        int n = loop.Count;
        Vector2 P(int i) => loop[((i % n) + n) % n].Point;
        float H(int i) => loop[((i % n) + n) % n].Height;
        bool Unit(int i) => MathF.Abs(Vector2.Distance(P(i), P(i + 1)) - 1) < 1e-4f;
        // Edge i is a step when it is one square long and turns the other way from the step before it.
        bool Step(int i) => Unit(i) && H(i) == H(i - 1);
        int start = Enumerable.Range(0, n).FirstOrDefault(i => !Unit(i), -1);
        if (start < 0) return loop;
        List<(Vector2 Point, float Height)> result = [];
        int k = 0;
        while (k < n)
        {
            int i = start + k;
            if (!Unit(i))
            {
                result.Add(loop[((i % n) + n) % n]);
                k++;
                continue;
            }
            int j = i; // the run of unit edges i..j
            while (j - i + 1 < n && Unit(j + 1) && Step(j + 1) && Turn(P(j), P(j + 1), P(j + 2)) == -Turn(P(j - 1), P(j), P(j + 1))) j++;
            int steps = j - i + 1;
            if (steps < 4 || !Step(i))
            {
                for (int e = i; e <= j; e++) result.Add(loop[((e % n) + n) % n]);
                k += steps;
                continue;
            }
            // Inner corners turn right (open ground is on the left): the first and last of them set the diagonal.
            List<Vector2> inner = Enumerable.Range(i + 1, steps - 1).Where(v => Turn(P(v - 1), P(v), P(v + 1)) < 0).Select(P).ToList();
            if (inner.Count < 2)
            {
                for (int e = i; e <= j; e++) result.Add(loop[((e % n) + n) % n]);
                k += steps;
                continue;
            }
            Vector2 along = inner[^1] - inner[0];
            Vector2 enter = Cross(P(i - 1), P(i), inner[0], along), leave = Cross(P(j + 2), P(j + 1), inner[0], along);
            result.Add((enter, H(i)));
            if ((j + 1 - start) % n == 0) result[0] = (leave, H(j + 1)); // the run ends where the walk began
            else result.Add((leave, H(j + 1)));
            k += steps + 1; // the vertex after the run became 'leave'
        }
        return result;
    }

    /// <summary>1 for a left turn at <paramref name="b"/> (x east, z north), −1 for a right turn, 0 straight.</summary>
    private static int Turn(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 u = b - a, v = c - b;
        return Math.Sign(u.X * v.Y - u.Y * v.X);
    }

    /// <summary>Where the line through <paramref name="from"/> and <paramref name="to"/> crosses the line through <paramref name="origin"/> along <paramref name="along"/>.</summary>
    private static Vector2 Cross(Vector2 from, Vector2 to, Vector2 origin, Vector2 along)
    {
        Vector2 d = to - from;
        float denominator = d.X * along.Y - d.Y * along.X;
        if (MathF.Abs(denominator) < 1e-6f) return to;
        float t = ((origin.X - from.X) * along.Y - (origin.Y - from.Y) * along.X) / denominator;
        return from + d * t;
    }

    /// <summary>A Unity-space point (metres on the ground plane) and height in glTF space: Unity is the X mirror.</summary>
    internal static Vector3 Gltf(Vector2 point, float y) => new(-point.X, y, point.Y);
}
