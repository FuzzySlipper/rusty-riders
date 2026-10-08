using System.Text;
using Rusty.Engine;

namespace RustyRiders.Game.Ui;

/// <summary>
/// Writes one Engine <see cref="UiValue"/> tree. The pinned SDK publishes the flat node/edge/UTF-8 form
/// but has no builder, so children are written first and containers then reference them.
/// </summary>
internal sealed class UiValueWriter
{
    private readonly List<StructuredValueNode> nodes = [default]; // Index 0 is the root object.
    private readonly List<uint> edges = [];
    private readonly List<byte> utf8 = [];

    internal uint Text(string key, string value) => Add(StructuredValueKind.String, key, text: value);
    internal uint Number(string key, double value) => Add(StructuredValueKind.Number, key, number: value);
    internal uint Flag(string key, bool value) => Add(StructuredValueKind.Bool, key, flag: value);
    internal uint Object(string key, params uint[] children) => Container(StructuredValueKind.Object, key, children);
    internal uint Array(string key, IReadOnlyList<uint> children) => Container(StructuredValueKind.Array, key, children);

    internal UiValue Finish(params uint[] children)
    {
        nodes[0] = new(StructuredValueKind.Object, 0, 0, 0, 0, 0, 0, (uint)edges.Count, (uint)children.Length);
        edges.AddRange(children);
        return new(nodes.ToArray(), edges.ToArray(), 0, utf8.ToArray());
    }

    /// <summary>True when two written values carry the same tree and text.</summary>
    internal static bool Same(UiValue a, UiValue b) => a.Root == b.Root &&
        a.Nodes.Span.SequenceEqual(b.Nodes.Span) && a.Edges.Span.SequenceEqual(b.Edges.Span) && a.Utf8.Span.SequenceEqual(b.Utf8.Span);

    private uint Container(StructuredValueKind kind, string key, IReadOnlyList<uint> children)
    {
        var name = Write(key);
        uint first = (uint)edges.Count;
        edges.AddRange(children);
        nodes.Add(new(kind, 0, 0, name.Offset, name.Length, 0, 0, first, (uint)children.Count));
        return (uint)nodes.Count - 1;
    }

    private uint Add(StructuredValueKind kind, string key, string text = "", double number = 0, bool flag = false)
    {
        var name = Write(key);
        var contents = Write(text);
        nodes.Add(new(kind, flag ? 1u : 0u, number, name.Offset, name.Length, contents.Offset, contents.Length, 0, 0));
        return (uint)nodes.Count - 1;
    }

    private (uint Offset, uint Length) Write(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint offset = (uint)utf8.Count;
        utf8.AddRange(bytes);
        return (offset, (uint)bytes.Length);
    }
}
