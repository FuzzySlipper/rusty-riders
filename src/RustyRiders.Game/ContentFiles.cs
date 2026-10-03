using Rusty.Engine;

namespace RustyRiders.Game;

internal static class ContentFiles
{
    /// <summary>A whole content-root file through Engine Content; a missing file throws <see cref="EngineCallException"/>.</summary>
    internal static ReadOnlyMemory<byte> Read(IEngineContext engine, string path)
    {
        using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest(path));
        ulong length = engine.Content.ReadReferenceInfo(content).Span[0].ByteLength;
        return engine.Content.ReadBytes(new ContentReadBytesRequest(content, 0, checked((uint)length)));
    }
}
