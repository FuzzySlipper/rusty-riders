# Reusing sibling product code

Sibling Rusty projects in the parent directory are one-time code and pattern
donors. Rusty Riders stays an ordinary safe C# product with the packaged Engine
as its only runtime dependency. No build, restore, content load or gameplay
path may refer to another checkout.

## Where to look

Paths are relative to the named sibling repository. They are consultation
candidates, not compatibility promises.

| Need | Donor | Notes |
| --- | --- | --- |
| Typed authored content with file-precise errors | rusty-hotel `src/Hotel.Game/Content/` | Incorporated (below) |
| Stats, effects, damage kinds | rusty-hotel `src/Hotel.Game/Mechanics/` (`ActorStats`, `ActorEffects`, `EffectDefinition`, `DamageContribution`) | Built on the Engine stats and effects components |
| One action pipeline (melee, hitscan, projectile, area, self) | rusty-hotel `src/Hotel.Game/Actions/` | Strip `HotelCombat`'s supplies/expedition coupling |
| Enemy senses and conduct | rusty-hotel `src/Hotel.Game/Residents/`; rusty-craftsurvive `src/CraftSurvive.Game/Modules/Rpg/` (encounter director/policy) and `Modules/Creatures/` | Hotel residents do not pathfind; use the Engine's navigation |
| Inventory, equipment, loot tables | rusty-hotel `Supplies/FieldCase.cs`, `Loot/`; rusty-craftsurvive `Modules/Inventory/` | Leave qualities and affixes until wanted |
| Growth and a refuge checkpoint | rusty-hotel `Progression/`, `Expedition/` | For the base |
| DOM screens | rusty-hotel `src/ui/` (plain ES modules); rusty-craftsurvive `src/ui/` (TypeScript) | Projections in, semantic intents out |
| Gameplay time adoption | rusty-engine `docs/csharp-lifecycle.md` (Gameplay time), `fixtures/csharp-gameplay-time` | Engine docs, not a code donor |

rusty-hotel's own `docs/reuse.md` lists further donors (rusty-fptester,
rusty-doom, rusty-dungeon) for player control and save stores.

## Copying procedure

1. Find the smallest relevant flow: entry point, state owner, implementation
   and a meaningful caller. Read it rather than copying a class by name.
2. Check the installed package selected by `Directory.Build.props` first.
   Prefer its safe helpers where they already express the mechanism. A donor's
   different pin does not establish API compatibility here.
3. Adapt into the existing Rusty Riders domain owner (see AGENTS.md "Content
   and code organization"): one owner per domain, Engine-admitted gameplay
   time and input, no imported accumulator, clock, input authority, renderer,
   collision solver or persistence transport. Rename donor vocabulary to this
   game's.
4. Record the donor revision and paths below (for uncommitted donor source,
   its SHA-256). Retain applicable attribution; code reuse does not authorize
   importing third-party art.
5. Verify against this repository's pin and the ordinary changed behaviour.

Do not change or clean donor repositories to make them easier to copy.

## Incorporated

| Donor | Revision | Source | Rusty Riders owner and what was kept |
| --- | --- | --- | --- |
| rusty-hotel | `82451c5dfb7b457654940c5822d832a19be9eef8` | `src/Hotel.Game/Content/Authored.cs` | `Content/Authored.cs`: read through Engine Content with errors naming the file, optional files, `Require` and the numeric/point/colour helpers. Dropped the `{key.action}` control-label substitution until bindings live in content. Kept per-domain JSON contexts instead of Hotel's single `ContentJson`, each with Hotel's strict options. |
| rusty-hotel | `82451c5dfb7b457654940c5822d832a19be9eef8` | `src/Hotel.Game/Mechanics/` (`MechanicsDefinition.cs`, `ActorStats.cs`, `ActorEffects.cs`, `EffectDefinition.cs`, `DamageContribution.cs`) | `Mechanics/`: the stat vocabulary and validation, Engine-backed actor stats with explainable derived sources, tracks and resistances, effects over the Engine effects component with stacking, ticks, wards and guards, and damage contributions. Renamed hold to stun and push to knockback. Dropped reveal, light and lure, growth and equipment sources, and checkpoint capture/validate/restore. Own JSON context (`MechanicsJson`). |
| rusty-hotel | `89ffa8fa869916d1f9a38099c0eadc6cde13204a` | `src/Hotel.Game/Actions/` (`ActionDefinition.cs`, `ActionUser.cs`, `ActionResolution.cs`) | `Actions/`: the action catalog and validation, per-actor timing, and resolution by Engine spatial queries. Added a power stat that multiplies a packet, magazine rounds as a cost and reloading actions, a time scale per use and a float-residue tolerance on phase ends. Dropped item-classification costs. Own JSON context (`ActionJson`). |
| rusty-hotel | `82451c5dfb7b457654940c5822d832a19be9eef8` | `content/mechanics/messages.json` | `content/mechanics/messages.json`, as written |
| rusty-hotel | `82451c5dfb7b457654940c5822d832a19be9eef8` | `src/Hotel.Game/Content/Template.cs` | `Content/Template.cs`: `Fill`, `Check` and `Plain`, as written |
