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
| First-person held motion, arms, workbench | rusty-hotel `src/Hotel.Game/Combat/` (`Held*`, `ArmsView`, `HandFitting`, `GripLandmarks`), `Interface/Workbench/`; rusty-fptester `docs/reference.md` (controller, actions, developer shell) | fptester is the maintained first-person reference; hotel holds the held-motion and arms tooling |
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
| rusty-hotel | `4485dfd4dc0190f4547aae1874cc9e4687828681` | `src/Hotel.Game/Combat/ResidentView.cs` | `Enemies/EnemyView.cs`: clip choice by state and the attack clip sampled at action progress (strike share, commit share, crossfade); per-enemy bodies and corpses are new |
| rusty-hotel | `4485dfd4dc0190f4547aae1874cc9e4687828681` | `src/Hotel.Game/Interface/UiValueWriter.cs` | `Ui/UiValueWriter.cs`, as written |
| rusty-hotel | `4485dfd4dc0190f4547aae1874cc9e4687828681` | `src/ui/pause.js` | `src/ui/pause.js`, with the error text made about the game, not the hotel |
| rusty-hotel | `dba13227a382a3afa74dc751a93626aaddcfd901` | `src/Hotel.Game/Combat/HeldDefinition.cs`, `HeldStance.cs`, `HeldMotion.cs`, `CombatView.cs` (held presentation) | `Held/`: held looks seated on stances, keyed motion played as Engine tween timelines (started that far along, settled when cut short), the motion viewer's views and live reload in place (`Refresh`). Adapted to two hands that each hold their own weapon: the off hand is the main hand's placement mirrored (`Frames.Mirror`), each look is drawn in a metre look space with its converted model fitted from its bounds (rotation and length tunable live), keys carry one hand's `grip` closure, and timelines follow the action's time scale. Dropped landmarks, hand shapes and fitting, off grips, muzzle flash and the stance guide. |
| rusty-hotel | `dba13227a382a3afa74dc751a93626aaddcfd901` | `src/Hotel.Game/Interface/HeldMotionDebugCommands.cs`, `Interface/Workbench/HotelWorkbench.cs`, `ViewmodelTunables.cs` | `Developer/HeldMotionDebugCommands.cs` and `Developer/Workbench/`: the `riders.dev.motion*` viewer (plays in real time, since the world holds while the player stands still), nudge and stance commands, a model-fit command, and the workbench page with a hand chooser. Dropped the arms and hands controls until the arms exist. |
| rusty-hotel | `dba13227a382a3afa74dc751a93626aaddcfd901` | `src/Hotel.Game/Combat/ArmsView.cs`, `content/combat/arms.json`, `art/arms/base.json` | `Held/ArmsView.cs`, `content/held/arms.json`, `art/arms/base.json`: one rigged arms model on the viewmodel layer, each hand reaching its weapon's grip by Engine two-bone IK over the grip clip, digits opened or squeezed by joint overrides as the motion keys' `grip` says. Both hands hold their own weapon: the left's wrist, turn, hold and elbow pole are the right's mirrored, so one hold serves both. Rig profiles (`ArmRig`) and measured hand frames and digit axes are new, so any arms model can draw them. Dropped hand shapes and fitting, landmarks, the off-hand rest and support grips. The arms model is generated (`art/arms/provenance.json`). |
| rusty-hotel | `dba13227a382a3afa74dc751a93626aaddcfd901` | `src/Hotel.Game/Combat/HeldDefinition.cs` (`HeldShape`, `HandShape`, `HeldLandmarks`), `ArmsView.cs` (`Close`), `GripLandmarks.cs`, `Interface/HandFitDebugCommands.cs` (`hotel.dev.hands.contact`), `content/combat/arms.json` (`hands.landmarks`) | `Held/HandShape.cs`, `Held/GripLandmarks.cs`, `ArmsView.Close`, `riders.dev.hands.contact`, `content/held/arms.json` `landmarks`: per-look hand shapes applied through each rig's measured digit frames, and grip landmarks in look space measured as Engine landmark pairs on the rig's `HandRig`. Dropped the hand fitter and its contact probes. |
| rusty-hotel | `dba13227a382a3afa74dc751a93626aaddcfd901` | `content/models/held/coach-gun.glb`, `content/combat/held.json` (`coach-gun`), `stances.json` (`longarm`), `held-motion.json` (`shot`: `shouldered`, `shoulderKick`, `coach-gun-blast`) | Model copied as-is into the ignored `content/art/hotel/coach-gun.glb` by `scripts/import-hotel-held.sh` (hotel's own asset, not third-party). The look was converted into `content/held/looks.json` `coach-gun`: grips and landmarks in look space, holds in the canonical hand frame (docs/direction.md). Also the `longarm` motion and stance (stance raised for riders' view). |
| rusty-fptester | `61f58a91103e6fed1a2bb3a6cac7457f02122b53` | `src/ui/developer.js` | `src/ui/developer.js`: availability probe and the docked Engine developer shell on its `onClose`, with hotel's grip inspection views and the motion viewer reset on close. |
| rusty-hotel | `82451c5dfb7b457654940c5822d832a19be9eef8` | `src/Hotel.Game/Content/Template.cs` | `Content/Template.cs`: `Fill`, `Check` and `Plain`, as written |

The reusable part of the held presentation adapted above (motion format and timelines, stances and frames, sway,
head bob, feedback layers, the arms and their rig profiles, hand shapes and knuckles, grip landmarks) has since moved
upstream into the Engine as `Rusty.Engine.FirstPerson` (rusty-engine #10188); riders keeps its content, loading and
policy in `Held/`.

## Old game art in the UI

The DOM UI uses Rift Riders' own UI art (`old-game/Assets/Art/Sprites/UI/RiftRiders`, `GameData/Icons`,
`Art/Sprites/UI/Fonts`), copied by `scripts/import-ui-art.py`, which lists every source. The `OH_`-prefixed frames
(hotbar, banner, button, fill) share a prefix with a third-party asset pack the old game used, so confirm their
licence before any public release. The unprefixed `UI SLICE BOX`, `UI INVENTORY` and `int_sphere` pieces, the icons
and Alagard are the old game's own or carry their own licence terms.

