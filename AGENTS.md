# Rusty Riders agent guidance

Rusty Riders is a Rusty Engine game inspired by an unfinished Unity game kept in
the ignored `old-game/`. It is not a port: old art and data are donor material.
Its current slice walks levels stamped the old game's way from its chunks,
layouts and tilesets (docs/levels.md), plus a gallery of the converted combat art.

> The product decides. The Engine guarantees.

## Start here

Read [README.md](README.md) for setup and commands,
[docs/architecture.md](docs/architecture.md) for the current owners and
[docs/direction.md](docs/direction.md) for the game direction and how the old art is used,
[docs/design.md](docs/design.md) for the gameplay design. Before
changing the Engine boundary, read the Engine's
[C# SDK guide](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-sdk.md)
and architecture. `rusty --help` is the workflow reference. Ordinary builds
consume the pinned package; verify capabilities against that pin (its release
notes and API surface) rather than against Engine source at another revision.

The user request and owning task define scope and acceptance. If work is tied
to Den, resolve that project's live guidance, task, and dependencies. Report
failed reads; do not invent task state. Continue independently authorized work
and pause only decisions that need unavailable authority.

## Ownership and source

- `src/RustyRiders.Game/` owns level generation and stamping, the gallery, the walker, application policy,
  semantic input interpretation, and UI facts. Organize additions by product domain;
  keep the product entry focused on explicit composition and lifecycle.
- `Rusty.Engine` owns named Engine mechanisms: lifecycle/update admission,
  input delivery, rendering/resources, spatial queries, content delivery,
  persistence primitives, and host integration. Search the safe SDK and
  existing product owners before adding a mechanism.
- `src/ui/` is a DOM companion. It observes Engine projections and submits
  semantic intents. Gameplay state, game rendering, canvas, transport, and
  scheduling stay with their C#/Engine owners.
- `content/` holds product-authored data. Interpret it in typed C# through
  Engine content services. Keep authored definitions, live state, and transient
  presentation distinct.
- The SDK generates the bind entry point and interop under ignored `obj/` output.
  Product code stays safe C#: no handwritten ABI/PInvoke, exports, raw native
  access, downstream Rust, or checked-in composition projects.

There is one Engine-admitted update path. Use its time/input facts; do not add
another loop, clock, scheduler, renderer, or state authority downstream.

## Product style

Prefer ordinary readable C#, explicit composition, direct methods, and one
clear mutable owner per domain. Keep operations thin: read, decide, apply,
publish. Use typed boundaries where they help; do not introduce a framework,
reflection discovery, generic bus, or service locator for hypothetical needs.

Use nullable types, file-scoped namespaces, and `internal`/`sealed` defaults
where the public product contract does not require otherwise. Keep structural
constants beside their algorithm; give meaningful identities names. Gameplay
values and authored definitions live in domain-owned content, not call sites;
see the next section.

## Content and code organization

Rusty Riders is content-heavy: many weapons, spells, items, enemies, effects
and worlds. Build gameplay so that a new one is mostly a content edit composed
from existing typed parts, not new code. Organize ahead of need: agents and
people extend the shape they find, so an ad hoc catch-all becomes the pattern
every later change follows. Put an addition where its future siblings will
live. If the file, class or module you are extending is already a catch-all,
split it along domain lines first, as its own change. Organization means
folders, files, typed records and named owners, not a framework, generic
registry, rule engine or plugin system.

- **Compose from typed parts.** Gameplay definitions are built from small
  typed records that their owning domain dispatches: an action is timing, a
  delivery (melee sweep, hitscan, projectile, area, self), a cost and payloads
  (damage by kind, effects); an enemy kind is a look, stats, senses, movement
  and action choices; an item grants actions, stat contributions or a
  consumable effect. A weapon is an item that grants actions; input never
  names a weapon. Player and enemies share one action pipeline and one stat
  model. A new kind from existing parts is a content edit; a genuinely new
  behaviour adds one new typed part to its owner's vocabulary.
- **Timed conditions are effects.** A burn, slow, stun, heal over time or buff
  is an effect definition advanced on Engine-admitted gameplay time, not a
  separate timer. Resources (health, ammo, charges) are stats or tracks on
  their owner, not loose numbers beside it.
- **Authored data is a domain-organized tree.** `content/` has directories and
  files named for what they hold (levels, mechanics, actions, items, enemies,
  run tuning). A product-named or catch-all file (`riders.json`, `data.json`,
  `config.json`) is a smell. Keep reusable definitions (an item, weapon,
  enemy kind) apart from where a level or table places them, and geometry
  apart from tuning. Each domain loads its own typed records with its own JSON
  context through `Content/Authored`; do not funnel all content through one
  record every owner reads.
- **Authored files are strict.** Their JSON contexts reject missing required
  values, nulls in non-nullable fields and unknown members; an optional field
  has a default in its record. References between files and value ranges that
  parse but are wrong fail through `Authored.Require` (and its helpers) naming
  the file and field. No silent defaults. Generated files from other tools
  (asset-pipeline placements) may ignore unknown members.
- **No gameplay prose in C# or JS.** Names, prompts, notices, refusal reasons
  and labels belong in their domain's content as templates (`Content/Template`)
  filled from definition values. Code composes text; it does not author it. A
  screen's fixed chrome and developer output may live in its markup or code.
- **Gameplay values are tuning.** Timings, ranges, damage, costs, spawn
  curves and limits go in content. A C# `const` is not tuning support.
- **Variants are typed.** A behaviour, kind or mode is an enum or typed record
  in its definition, dispatched by the owning domain. Do not compare strings
  in views.
- **One table per vocabulary.** Key bindings and their labels, damage kinds,
  HUD facts and saved fields each have one declaration that every consumer
  (C# input, HUD, UI, playtest actions) reads.
- **Size signals prompt a split.** A C# owner growing past roughly 300 lines,
  a method interleaving several domains, a JS module holding more than one
  screen, or a JSON file spanning several domains is the cue to divide it
  before extending. These are prompts to look, not gates.

Sibling Rusty repositories are one-time code donors, never build or content
dependencies. Follow [docs/reuse.md](docs/reuse.md) and record what was copied.

Trust first-party runtime state and Engine-admitted data. Preserve concrete
eligibility rules, current-data errors, and resource lifetime/disposal. Do not
add repeated hashing, compatibility layers, whole-state rollback, or validation
ceremony without a task-owned failure it prevents. Save meaningful values at
explicit save boundaries; native handles and presentation resources are not
product save state.

## Engine dependencies and gaps

`Directory.Build.props` owns the exact SDK/runtime pin. Install it with
`rusty install`; deliberately advance it with `rusty update`, read the release
notes it lists, then run the focused checks. `rusty status` reports the pin,
installation and missing prerequisites. Keep exact
versions in executable configuration and evidence, not duplicated in prose.
Normal development uses the matched runtime pack through `rusty dev`.
NativeAOT is an explicit fidelity/release check. Do not make an adjacent
Engine checkout a build dependency or modify it as part of downstream work.

If a required mechanism is missing, verify the safe API, name the blocked
behavior and upstream owner, and file/link one narrow Engine request when
that is authorized. Distinguish a missing mechanism or binding from a helper
or documentation gap. Stop that dependent slice; continue independent work.
Do not conceal the gap with a local substitute, fake success, or proof-only path.

## Review and evidence

Use [docs/agent-review/README.md](docs/agent-review/README.md). Every change gets
an Engine-reuse and existing-product-reuse check; trivial changes may record
that no mechanism is affected. Assign bounded independent lanes when review
agents are requested or the task's review workflow calls for them. Keep the
same reviewer for fix rounds and reconcile source-backed findings against the
original task. Review is not an extra user-approval gate.

`rusty build --project src/RustyRiders.Game/RustyRiders.Game.csproj` builds
and stages the ordinary CoreCLR product; `--aot` additionally publishes NativeAOT. Use focused
semantic or interaction evidence only when it answers the changed behavior;
do not add broad test gates to this small product. Distinguish build/staging,
host launch, and visible interaction claims. Repeat passed checks only after
material changes or an unresolved failure.

Preserve unrelated edits. Keep generated output and installed artifacts
ignored. Do not reset, force-push, or change adjacent repositories. Report what
changed, relevant checks, and concrete limitations. Commit/push when requested
or authorized by the active task; a review packet does not authorize publishing.
