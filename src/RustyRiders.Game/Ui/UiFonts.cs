using Rusty.Engine;

namespace RustyRiders.Game.Ui;

/// <summary>
/// The fonts the DOM UI uses, granted by the Engine from content (the UI root serves no font files): each is opened once
/// and its same-origin URL goes out with the facts for a CSS <c>@font-face</c>. Disposed with the product.
/// </summary>
internal sealed class UiFonts : IDisposable
{
    internal const string TitlePath = "ui/alagard.ttf";

    private readonly UiFont title;

    internal UiFonts(IEngineContext engine)
    {
        using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest(TitlePath));
        title = engine.Ui.OpenFont(new UiFontRequest(content));
    }

    /// <summary>The title font's URL (Alagard, the old game's pixel fantasy face).</summary>
    internal string TitleUrl => title.Url();

    public void Dispose() => title.Dispose();
}
