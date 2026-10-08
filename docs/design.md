# Game design

The working design for Rusty Riders' gameplay. It records what the game is meant to be and what the first
prototype has to prove. [direction.md](direction.md) holds the art and level-kit decisions this builds on. Den
tasks hold the open work (rusty-riders #9634 is the prototype's parent task). Values here are starting points
for tuning. Content owns the real numbers.

## Pitch

A first-person science-fantasy action RPG about riding rifts between worlds. You step through a portal into
a hostile level. You have a few seconds before whatever lives there follows you through. Then you scavenge,
fight and run for the next rift. Time moves only while you move or act. Every moment is frantic, but you can
always stop and think about the next one.

## Pillars

- **Frantic, but never rushed.** The world is dangerous and getting worse, yet nothing happens while you
  stand still. The pressure comes from the situation, not from your reflexes.
- **Push or bank.** Every level offers a way home. Going deeper always pays more and always costs more, and
  the choice is made fresh at each level.
- **Read the rift.** Portals hint at what lies beyond them. The tilesets differ wildly, and an environment
  tells you its enemies and rewards before you commit.
- **Science fantasy.** Swords next to laser muskets, spells next to tech charges. Both tool kinds are equally
  valid. The mix comes from the old Rift Riders data (LaserFlintlock, TechAmmo, MagicCharge).
- **Solo and free-moving.** One character, first person, no grid and no party (see
  [direction.md](direction.md#game-direction)).

## Core loop

```text
Base ──▶ enter run ──▶ Level N ──▶ choose a rift ──┬─▶ Level N+1 (harder, richer) ──▶ …
  ▲                                               └─▶ return rift ──▶ bank haul ──▶ Base
  └───────────────────────── death: haul lost ◀───────────────────────────────────────┘
```

1. **Arrive.** You come through the entry portal. A **chase timer** starts, counted in gameplay time.
2. **Scavenge and fight.** Rooms hold supplies (the meta currency), equipment, ammo and consumables. Some
   enemies already live in the level.
3. **The chase.** When the timer runs out, enemies start coming through the entry portal in **waves that
   grow** in size and frequency. They hunt you. Staying put ends badly.
4. **Choose a rift.** Each level has several exit rifts. Each one shows a vague preview of the tileset or
   environment on the other side. One of them is always a **return rift**.
5. **Push or bank.**
   - **Push:** take a normal rift. The depth goes up, rewards go up, and the next chase timer is shorter
     and/or its waves are larger.
   - **Bank:** take the return rift. The run ends and its haul is added to the base.
6. **Die.** The run's haul is lost (whether some of it is kept is an open question).

### Escalation

Depth is how many levels the run has pushed through. These values scale with depth, and content owns the
curves:

| Value | Direction | Starting idea |
| --- | --- | --- |
| Reward multiplier (supplies, loot quality) | up | ×1.0, +0.25 per depth |
| Chase timer | down | 30 s gameplay time, −3 s per depth, floor 8 s |
| Wave size | up | 2 enemies, +1 per depth, plus growth within a level |
| Wave interval | down | 12 s, shrinking within a level |

Escalation comes from the run. A level's tileset decides *which* enemies and rewards appear. Depth decides
*how many* and *how good*.

## Time model

Time runs only while you move or act, as in *SUPERHOT*. The Engine provides this as **gameplay time**
(`engine.GameplayTime`: `Hold`, `SetRate`, `Advance`).

- **Held by default.** While you stand still, the world is frozen: enemies, projectiles, effects, spawn
  timers and the chase timer.
- **Looking is free.** Turning the camera uses host time and never advances the world.
- **Movement runs the world.** Moving (walking, sprinting, a jump or fall in progress) runs it at realtime, and
  partial stick input runs it proportionally slower. The player's body is on the same world clock as everything
  else, so a rate below 1 does not move you faster than the world. It only slows everything down together
  (bullet time), which may later become a deliberate "focus" mode.
- **One knob for standing still.** `content/time.json` `heldRate` is the rate while still. 0 is a true freeze;
  a tiny positive crawl is the global fallback if a freeze ever misbehaves. The move rates and the wait length
  live beside it.
- **Waiting.** A wait control lets a moment of world time pass while you stand still.
- **Actions buy time.** Swinging, firing, casting, reloading and using an item advance the world by that
  action's own duration (windup + commit + recovery) through `Advance`. A slow heavy swing costs more time
  than a quick pistol shot, and that cost is part of a weapon's identity.
- **Everything on the clock.** The chase timer, wave spawns, cooldowns, effects and AI all count gameplay
  time, never wall-clock time. Stopping must always be safe, or the "plan your next move" half of the
  pillar falls apart.
- **Feedback.** The HUD shows whether the world is running, held or advancing (rate meter or tint), so
  the player always knows if time is moving.

Standing still is a true freeze. A tiny crawl is a fallback (`heldRate`), not the design.

## Levels and rifts

Levels are generated with the existing stamping ([levels.md](levels.md)): chunks fitted to a layout graph,
corridors between them, and tiles from a tileset with one of its palettes. The prototype uses the stamped
converted tiles. Generated shells and sweeps stay an option for weak tilesets (Cave, Forest).

Layout node tags map onto gameplay:

| Layout tag | Gameplay use |
| --- | --- |
| `start` | Entry portal: player arrival and the enemies' chase spawn point |
| `exit` | Exit rifts (each exit node holds at least one) |
| `spawner` | Resident enemy groups placed at level start |
| `bonus`, `key` | Supply caches, chests, equipment |
| `midBoss`, `boss` | An elite guarding a better reward or a rarer rift (later) |
| chunk objectives `primary`/`secondary`/`chest` | Concrete spots for rifts, caches and chests inside a room |

The return rift is placed by the run, not by the layout. The prototype can put it at any exit node.

### Tilesets as worlds

Each tileset is a world with its own faction (from the old `styles.json`), enemies and reward bias. The
enemy themes below are tentative and depend on which old enemy models convert well.

| Tileset | Old faction | Feel | Likely enemies | Reward bias (idea) |
| --- | --- | --- | --- | --- |
| UndergroundPalace | Astral Mandate | Gilded halls, arches | Demons, succubi, crusaders | Spells, magic charges |
| LavaMap | Astral Mandate | Volcanic, emissive | Fire demons, brutes | Burn weapons, heavy melee |
| StoneRoadMap | Open Hand | Ruined dungeon road | Undead, skeleton knights | Melee gear, armour |
| CaveMap | Hermetic Circle | Organic caverns | Beasts, watchers | Supplies, ore |
| ForestMap | Keeper Gate | Wild clearing | Orcs, were-beasts | Consumables, bows |
| Cybertubes | Maelstrom | Sci-fi conduits | Robots, aliens | Guns, tech ammo |

A rift's **preview** is a glimpse of its destination: its tileset's colours and emissive tint on the portal
surface at first, later perhaps a rendered peek. Depth, and perhaps a danger sign, shows on its label.

## Character and combat

### Stats

Few attributes, and derived values read from content. The structure follows rusty-hotel's `stats.json`
(attributes → derived stats via per-point contributions):
- **Attributes (draft):** Might (melee damage, carry), Agility (move speed, action speed), Focus (spell
  power, charge capacity), Grit (health, resistances).
- **Derived:** maximum health, move speed, action-time multiplier (faster actions spend less time), carry
  capacity, a resistance per damage kind.
- **Damage kinds** come from the old game: Physical, Burn, Freeze, Lightning, Spirit.

Progression within a run comes from equipment, not levels. Permanent growth belongs to the base.

### Actions

Every swing, shot, cast and use is one **action**. Player and enemies share one pipeline (adapted from
rusty-hotel's `Actions/`):
- **Phases:** windup → commit → recovery, plus a cooldown. The total time is what an action costs in
  gameplay time.
- **Delivery:** melee sweep (swept capsule), hitscan ray, projectile (per-step segment cast), area, or
  self.
- **Cost:** ammo, charge, health or nothing.
- **Payload:** damage packets by kind, scaled by stats, plus effects (burn, slow, stun, knockback).

### Weapons

| Family | Examples (old art) | Feel |
| --- | --- | --- |
| Melee | Swords, axes, hammers, halberds, daggers, MagicAxe | No ammo, reach and arc, heavy = slow and costly in time |
| Gun | Laser flintlock, laser musket, sci-fi rifles and pistols, shotgun, taser | Hitscan or fast projectile, uses ammo, reload is an action |
| Focus / spell | Spells from the old ability schools (Elemental, Theurgy, Invocation …) | Charges, projectiles and areas, element effects |

Ammo kinds from the old data: BulletClip, TechAmmo, TechCharge, MagicCharge, PowerCharge (Arrows if bows
come back). The player holds a main hand and an off hand. A gun in one hand and a blade in the other is
encouraged.

### Spells

The prototype has no separate spell system: spells are actions granted by a focus item or an equipment
slot, and they spend charges. The old `Abilities/` data (157 assets in 15 schools) is the idea pool, not a
spec. Its dice, action points and turn costs don't carry over.

### Enemies

- They share the action pipeline and stat model.
- They **chase** along the level's walk grid (`WalkCells`). rusty-hotel's enemies only close in straight
  lines, so pathing is new work here.
- **Residents** wait in `spawner` rooms and react to sight or sound. **Chasers** come through the entry
  portal and always know roughly where you are.
- They start as placeholder shapes. Old enemy models are converted only when the loop works (many enemy
  FBX are FBX 6 and need the `old-game-fbx2013` re-save first).

## Items and supplies

- **Supplies:** the meta currency. Abstract (one or a few kinds) for the prototype. They count only
  when banked.
- **Equipment:** weapons, a focus, armour or outfit, and accessories. Found in a run, lost on death.
  Rarity and affixes come later (rusty-hotel's loot tables, qualities and affixes are the donor).
- **Consumables:** healing, ammo, charges, and one-shot utility such as a blink or a time-free action
  (later).
- **Carrying:** a small grid or slot inventory (rusty-hotel `FieldCase`, craftsurvive slots). Weight or
  slot pressure makes you choose what to haul.
- **Pickups:** walk-over pickups for ammo and supplies (quick, no time cost). Equipment is taken on use.

## Base and meta (later)

The base is where banked supplies are spent: unlock starting kits, permanent stat growth, new rift kinds
and better previews. It stays out of the first prototype. "Run ends → haul tallied → new run" is enough to
test push-or-bank.

## UI

A DOM companion over the Engine ([ui.md](ui.md)), mined from rusty-hotel and rusty-craftsurvive:
- **HUD:** health, ammo and charge per hand, time state (running, held or advancing), chase timer and wave
  warning, depth and reward multiplier, supplies carried.
- **Inventory and character sheet:** paused over the held world. Grid and equipment slots from rusty-hotel
  `field-case.js`/`worn-view.js` and craftsurvive `screens.ts`/`slots.ts`. A stat readout is new work
  (rusty-hotel has no sheet).
- **Rift choice:** looking at a rift shows its label (preview, depth, return or push).
- **Run summary:** haul banked or lost, depth reached.

## Reusing sibling code

Sibling repos are one-time donors, copied and adapted with their provenance recorded. They are never build
dependencies (see AGENTS.md). Main donors:

| Need | Donor |
| --- | --- |
| Content loading, typed authored data with path-precise errors | rusty-hotel `Content/` (`Authored`, `ContentJson`, `Template`) |
| Stats, effects, damage | rusty-hotel `Mechanics/` (`ActorStats`, `ActorEffects`, `DamageContribution`) |
| Action pipeline (melee, hitscan, projectile, area) | rusty-hotel `Actions/` |
| Enemy senses and conduct | rusty-hotel `Residents/`, craftsurvive `Rpg/` encounter director, `Creatures/CreatureNavigation` |
| Inventory and loot | rusty-hotel `Supplies/FieldCase`, `Loot/`; craftsurvive `Inventory/` |
| Gameplay time adoption | Engine `docs/csharp-lifecycle.md` (Gameplay time) and `fixtures/csharp-gameplay-time` |
| UI screens | rusty-hotel `src/ui`, craftsurvive `src/ui` |

rusty-hotel's `docs/reuse.md` describes a copying procedure: record revisions and source hashes, adapt into
one owner, keep Engine-admitted time. Rusty Riders follows it and keeps the provenance in
`docs/reuse.md` once the first code is copied.

## Old art for gameplay

Converted only when a task needs it ([direction.md](direction.md#what-the-old-art-is-worth)):
- **Weapons:** `old-game/Assets/Art/Models/Weapons/` (72 FBX 7.x, including the sci-fi gun set). They are
  needed for first-person held weapons. There are no first-person arms, so weapons float or a simple
  hand model is made.
- **Enemies:** `Art/Models/Enemies/` (31 creatures with animation clips, 81 FBX 6 files, some already
  re-saved in `old-game-fbx2013/`).
- **Portal:** `Environment/Futuristic Portal/` and `PortalMachine/`.
- **Containers:** `PiratesChest_A1` and `_Cap` (already converted), `Big_box`.
- **VFX:** Unity particle prefabs, which have no GLB equivalent. Their textures and sprite sheets may seed
  Engine particles later.
- **Sounds:** large packs under `Assets/Sounds/` (melee, weapons, spells, monsters, pickups).

## First prototype: what it must prove

1. Gameplay time feels right: frozen while still, readable while moving, and action costs that make weapon
   choice meaningful.
2. The chase timer and waves create pressure without making stopping unsafe.
3. Melee, a gun and a spell all work against chasing enemies on stamped levels.
4. Rifts chain levels across tilesets, and push-or-bank is a real choice by depth 3–4.

Out of scope for it: the base, affixes and rarity, converted enemy animation, the third-person camera, save
games beyond a banked total.

### What the escalation does today

With the authored curves (`content/run/run.json`, `content/enemies/chase.json`), each rift pushed through:

| Depth | Chase timer | First wave | Reward multiplier |
| --- | --- | --- | --- |
| 0 | 30 s | 2 | ×1.0 |
| 2 | 24 s | 4 | ×1.7 |
| 4 | 18 s | 6 | ×2.4 |

Wave intervals also shrink with depth (12 s − 1 per depth, floor 4 s). The multiplier scales cache and drop rolls,
so supplies, ammunition and gear rise together.

Every level has exactly one return rift (placed by the seed). Its label shows the haul it would bank, and a rift
to another world shows that world and the depth it leads to.

### Push or bank (question 4): first read

Question 4 is not yet answered by real play: the run loop was exercised with developer travel, not a played
run. What is in place:
- The stakes are visible (haul, bank, the depth a rift leads to).
- A fall costs the whole haul.
- The cost of pushing (timer down 3 s and one more chaser per wave per depth) and the reward (×0.35 per depth)
  are both on screen.

Things to watch in the first human playtests:
- The return rift can be the farthest rift, which makes banking a trek; it may want to be nearer the entry, or
  marked.
- Supplies only come from caches and drops, so a level rushed for its rift banks little. The haul may need its
  own source in each level (a bonus room cache) for the choice to bite by depth 3–4.
- At depth 4 the timer (18 s) still leaves room to open a cache. Whether that feels frantic depends on wave
  pathing time, which is level-size dependent.

## Open questions

- How much a death keeps (nothing, a fraction, or banked-at-checkpoint).
- Whether residents count toward the chase or only the waves do.
- How informative rift previews should be: tileset only, or also a danger or reward sign.
- Whether depth also changes which tilesets appear (harder worlds deeper).
