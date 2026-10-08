using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Ui;

/// <summary>The character sheet's facts: attributes, derived stats with what they are made of, and resistances with their icons.</summary>
internal static class SheetFacts
{
    internal static uint Write(UiValueWriter w, ActorStats stats)
    {
        MechanicsDefinition m = stats.Mechanics;
        uint[] attributes = m.Attributes.Select(a => w.Object("", w.Text("name", a.Name), w.Number("value", stats.Stat(a.Id).Value))).ToArray();
        uint[] derived = m.Derived.Select(d =>
        {
            var e = stats.Explain(d.Id);
            string detail = FormattableString.Invariant($"{e.Base:0.##} base {(e.AfterAdditions - e.Base >= 0 ? "+" : "−")} {Math.Abs(e.AfterAdditions - e.Base):0.##}")
                + (Math.Abs(e.AfterScaling - e.AfterAdditions) > 1e-6 ? FormattableString.Invariant($" × → {e.AfterScaling:0.##}") : "");
            return w.Object("", w.Text("name", d.Name), w.Number("value", e.Value), w.Text("detail", detail));
        }).ToArray();
        uint[] resistances = m.DamageKinds.Select(k => w.Object("", w.Text("name", k.Name), w.Text("icon", k.Icon),
            w.Number("value", stats.Resistance(k.Id)))).ToArray();
        return w.Object("sheet", w.Array("attributes", attributes), w.Array("derived", derived), w.Array("resistances", resistances));
    }
}
