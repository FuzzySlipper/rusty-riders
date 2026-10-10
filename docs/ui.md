# DOM companion

`src/ui/main.js` exports `mountProductUi`. It composes the in-view HUD and the screens, and handles focus and
pausing. It holds no game state: everything it draws comes from the one projection C# publishes each update
(stream `rusty-riders`, contract `rusty.riders.hud`, written by `Ui/Hud.cs`). Every change the player makes on a
screen is a claim to a C# owner. The product project selects this directory and module for SDK staging.

## Modules

| Module | Owns |
| --- | --- |
| `main.js` | Composition, which screen is open, focus and pause (I and C open screens, Esc closes); registers the title font from the facts |
| `hud.js` | The in-view HUD: vitals, hands, the chase banner, the world-time ring, notice, prompt, action, rift label, run summary, the developer block with the controls |
| `inventory-screen.js` | Weapons (click for the main hand, right-click for the off hand), worn armour (click to take off), carried stacks (click to use or wear) |
| `sheet-screen.js` | Attributes, derived stats with what they are made of, resistances by damage kind |
| `art.js` | URLs of the old game's UI art and icons |
| `pause.js` | The pause request flow (from rusty-hotel) |
| `riders.css` | The look: the functional layer from its tokens, and where each ornament is pinned |

A screen pauses the game through the Engine lifecycle port while it is open, and gives focus back to play when it
closes. Its claims still reach C# while paused (`HandlePausedIntents`).

## Facts and intents

- **Facts** are one structured value (`Ui/UiValueWriter.cs`):
  - flat text (status, time, chase, notice, prompt, depth, bank, summary);
  - numbers (health and its maximum, action progress);
  - arrays (tracks, effects, hands);
  - objects (`inventory`, `sheet`).
  - Item and damage-kind icons are names from content (`icon` in `content/items/items.json` and
    `content/mechanics/damage.json`); the UI turns a name into `art/icons/<name>.png`.
- **Intents.** The only intent is `riders.inventory` with contract `riders.inventory.v1`. It is declared in the project as a
  `RustyEngineProductInputIntent`; an undeclared intent is dropped by the host. Its payload is
  `{ action, index, hand }` with `action` one of `hold`, `wear`, `takeOff` or `use`. `Run/Play.HandleIntents`
  applies it, and a claim that no longer fits does nothing.

## Look: function first, ornaments on top

The art never carries layout. The UI has two layers:

- **Function**: plain DOM and CSS drawn from the tokens at the top of `riders.css`: colours, spacing, text sizes and
  one scale, `--u`. Wells, chips, bars, slots, panels and lists are CSS and read whole with every ornament gone.
- **Ornaments**: the old game's art, pinned to an element at a fixed scale (`<i class="orn …">`, `pointer-events:
  none`). An ornament keeps its own proportions and never stretches; changing, adding or regenerating one cannot move
  anything else. Pieces that run along a length are repeating rails or fills, never stretched middles:
  - the chase banner: the red bar's skull caps at both ends and a rail that repeats between them, upside down, its
    legs' flat feet on the screen's top edge and its horns facing into the view;
  - framed panels (`.framed`): a quiet bone frame, a 9-slice with small horned corners and repeating double rails,
    for the hands (standing on the bottom edge) and for the screens to come (map, base/meta);
  - the vitals: the skull corner mirrored to face the centre, flush in the screen's bottom-left corner: its leg's flat
    foot on the bottom edge, its horn arm a rail along the bars' top that runs to the screen's left edge. It stays in
    the screen's own corner on ultrawide screens, outside the HUD band;
  - panels (screens, the run summary): the violet horned frame as a 9-slice whose corners keep their size and whose
    rails repeat, drawn outside the box with its rail on the box's edge;
  - the round skull socket behind each hand, the horned ring round the clock, the crest over a screen's title, and
    the brushed fill that repeats inside wells;
  - generated in the old style: horned caps on two corners of each slot, the divider under a screen's title, and the
    ammunition icon.

The developer block (status, problems, the controls and the position) is one compact box in the screen's top-left
corner, so it can be switched off as one.

`--u` steps with the viewport (0.65 to 2 px) so the whole UI scales together. The HUD keeps to a centred band no
wider than 2:1, so ultrawide screens keep it in view. It was checked at 16:9, 21:9, 4:3 and a 960×540 window.

### Art

`scripts/import-ui-art.py` cuts the old Rift Riders UI sprites into `src/ui/art/`:
- the ornaments above, each exported at twice its display size at `--u: 1px`, so it stays sharp up to the largest step;
- rails and the fill, taken from a flat run of the art and cross-faded over their ends so they repeat without a seam;
- icons (equipment, items, junk and damage kinds), at 128 px.

Line-art icons are tinted green, as the old game's hotbar icons were; painted ones show as they are. Rerun the script
to regenerate the folder from `old-game/`.

Ornaments the old art lacks are generated in its style (GPT image through the `codex-image-gen` skill, with crops of
the old art as references) on flat green, then keyed and cropped. The masters and their prompts are in
`content/art/ui/` (`sources.json`); the import script exports them with the old pieces. They are experiments, not a
house-style recipe. The generated pieces are the slot corners, the title divider, the ammunition icon and the bone
frame.

Fonts are not UI-root files: the host refuses font files there. The title face (Alagard, the old game's pixel
fantasy font) is `content/ui/alagard.ttf`, granted by `Ui/UiFonts.cs` through `Ui.OpenFont`. Its URL arrives as
`titleFont` in the facts, and `main.js` registers it with `FontFace`.

Keep only browser assets in `src/ui/`. Keep this lane to DOM presentation, accessibility and semantic actions.
Game state lives in C#; input delivery, projection transport, the canvas and rendering belong to the Engine. Dispose
event listeners and subscriptions when the host unmounts the UI.
