using System.Numerics;
using static RustyRiders.Game.Levels.Kit.StoneShape;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>A straight run of a stone loop: from <see cref="A"/> to <see cref="B"/> (Unity metres, open side on the left), laid from <see cref="Bottom"/> to <see cref="Top"/>.</summary>
internal readonly record struct StoneRun(Vector2 A, Vector2 B, float Bottom, float Top);

/// <summary>
/// Stone loops: an outline, or a column's shaft, laid run by run in courses shared round the whole loop, so courses
/// meet at every corner. At a right-angled corner, alternate courses give the corner to one run or the other:
/// - at an inside corner, the winning run's stone goes into the corner and the other run's starts clear of it;
/// - at an outside corner (a jamb, a column's edge), the winning run's stone runs on past the corner by the other run's
///   relief, so its end shows on the other face, as a quoin's does.
/// Each corner stone's relief comes from the corner and the course, so both runs agree on it. Other corners end square.
/// </summary>
internal static partial class WallStones
{
    private enum Turn { Square, Inside, Outside }

    /// <summary>The stones on every outline run, between its area's floor trims and ceiling trims.</summary>
    internal static int Outlines(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, WallStonesDefinition stones,
        List<List<(Vector2 Point, float Height)>> loops, int seed)
    {
        int laid = 0;
        for (int l = 0; l < loops.Count; l++)
        {
            List<(Vector2 Point, float Height)> loop = loops[l];
            StoneRun[] runs = loop.Select((p, i) =>
            {
                PlanArea area = plan.Rules.Area(p.Height);
                return new StoneRun(plan.Metres(p.Point), plan.Metres(loop[(i + 1) % loop.Count].Point), Bottom(recipe, area), p.Height - TopGap(recipe, area));
            }).ToArray();
            laid += Loop(mesh, recipe, stones, stones.Surface, runs, unchecked(seed * 7919 + l * 104729));
        }
        return laid;
    }

    /// <summary>Lays a closed loop of runs in shared courses with interlocking corners, in <paramref name="surface"/>.</summary>
    internal static int Loop(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, KitSurface surface, IReadOnlyList<StoneRun> runs, int seed)
    {
        int n = runs.Count, laid = 0;
        Random heights = new(seed);
        List<float> bounds = [0];
        float highest = runs.Max(r => r.Top);
        while (bounds[^1] < highest) bounds.Add(bounds[^1] + Between(heights, stones.MinCourse, stones.MaxCourse));
        List<(int Key, float Y0, float Y1)>[] courses = runs.Select(r => Courses(bounds, r.Bottom, r.Top, stones.MinCourse)).ToArray();
        Turn[] turns = Enumerable.Range(0, n).Select(c => TurnAt(runs[(c + n - 1) % n], runs[c])).ToArray(); // corner c: run c-1 into run c
        float middle = (stones.MinRelief + stones.MaxRelief) / 2;
        for (int i = 0; i < n; i++)
        {
            StoneRun run = runs[i];
            Vector2 along = Vector2.Normalize(run.B - run.A), inward = new(-along.Y, along.X);
            float length = Vector2.Distance(run.A, run.B);
            int before = i, after = (i + 1) % n; // this run's start and end corners
            Random random = new(unchecked(seed * 31 + i * 104729));
            List<float> below = [];
            foreach ((int key, float y0, float y1) in courses[i])
            {
                float x0 = 0, x1 = length;
                float? firstRelief = null, lastRelief = null;
                if (turns[before] != Turn.Square && courses[(i + n - 1) % n].Any(c => c.Key == key))
                {
                    (float win, float lose) = Reliefs(seed, before, key, stones, middle);
                    bool mine = !Earlier(before, key); // the run starting here wins when the earlier one does not
                    if (turns[before] == Turn.Inside) { if (mine) firstRelief = win; else x0 = win + stones.Joint; }
                    else { if (mine) { x0 = -lose; firstRelief = win; } else firstRelief = lose; }
                }
                if (turns[after] != Turn.Square && courses[after].Any(c => c.Key == key))
                {
                    (float win, float lose) = Reliefs(seed, after, key, stones, middle);
                    bool mine = Earlier(after, key);
                    if (turns[after] == Turn.Inside) { if (mine) lastRelief = win; else x1 = length - win - stones.Joint; }
                    else { if (mine) { x1 = length + lose; lastRelief = win; } else lastRelief = lose; }
                }
                if (x1 - x0 > stones.Joint * 2)
                    laid += Row(mesh, recipe, stones, surface, random, run.A, along, inward, x0, x1, y0, y1, ref below, firstRelief, lastRelief);
            }
        }
        return laid;
    }

    /// <summary>Whether the run ending at corner <paramref name="corner"/> wins it on course <paramref name="key"/>; alternate courses alternate.</summary>
    private static bool Earlier(int corner, int key) => ((corner + key) & 1) == 0;

    /// <summary>The reliefs of the winning and losing corner stones at a corner on a course: the same for both runs.</summary>
    private static (float Win, float Lose) Reliefs(int seed, int corner, int key, WallStonesDefinition stones, float middle)
    {
        Random random = new(unchecked(seed * 92821 + corner * 7919 + key * 104729));
        return (Between(random, stones.MinRelief, stones.MaxRelief), Between(random, stones.MinRelief, middle));
    }

    /// <summary>How the loop turns from one run into the next: a right angle inside or outside, or anything else.</summary>
    private static Turn TurnAt(StoneRun from, StoneRun to)
    {
        Vector2 a = Vector2.Normalize(from.B - from.A), b = Vector2.Normalize(to.B - to.A);
        if (MathF.Abs(Vector2.Dot(a, b)) > .1f) return Turn.Square;
        float cross = a.X * b.Y - a.Y * b.X; // open ground on the left: a left turn closes round it
        return cross > 0 ? Turn.Inside : Turn.Outside;
    }

    /// <summary>
    /// A run's courses between <paramref name="bottom"/> and <paramref name="top"/>, cut from the loop's shared course
    /// heights; a sliver at either end joins its neighbour. Each is keyed by the shared course it starts in.
    /// </summary>
    private static List<(int Key, float Y0, float Y1)> Courses(List<float> bounds, float bottom, float top, float minCourse)
    {
        List<float> cuts = [bottom, .. bounds.Where(y => y > bottom + 1e-3f && y < top - 1e-3f), top];
        if (cuts.Count > 2 && cuts[1] - cuts[0] < minCourse * .5f) cuts.RemoveAt(1);
        if (cuts.Count > 2 && cuts[^1] - cuts[^2] < minCourse * .5f) cuts.RemoveAt(cuts.Count - 2);
        List<(int, float, float)> courses = [];
        for (int i = 0; i + 1 < cuts.Count; i++)
            courses.Add((bounds.FindLastIndex(y => y <= cuts[i] + 1e-3f), cuts[i], cuts[i + 1]));
        return courses;
    }
}
