using System.Diagnostics;
using Rusty.Engine.Debugging;

namespace RustyRiders.Game.Developer;

/// <summary>
/// Live-debug readout of the process's memory, for load-cost evidence: the managed heap (what C# holds and what the
/// collector has kept committed) beside the whole process (which adds the Engine's native resources).
/// </summary>
internal sealed class MemoryDebugCommands : IDebugCommandModule
{
    private const double Mib = 1024 * 1024;

    [DebugCommand("riders.dev.memory", Description = "Developer readout: managed heap, its committed and fragmented memory, and the process's private and resident memory in MiB; with collect true, after a full compacting collection.")]
    public DebugCommandResult Memory(bool collect)
    {
        if (collect)
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        using Process process = Process.GetCurrentProcess();
        return DebugCommandResult.Success(FormattableString.Invariant(
            $"managed {GC.GetTotalMemory(false) / Mib:0} MiB (heap {gc.HeapSizeBytes / Mib:0}, committed {gc.TotalCommittedBytes / Mib:0}, fragmented {gc.FragmentedBytes / Mib:0}, allocated so far {GC.GetTotalAllocatedBytes() / Mib:0}); process private {process.PrivateMemorySize64 / Mib:0}, resident {process.WorkingSet64 / Mib:0} MiB"));
    }
}
