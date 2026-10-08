# DOM companion

`src/ui/main.js` exports `mountProductUi`. It composes the in-view HUD and the screens, and handles focus and
pausing. It holds no game state: everything it draws comes from the one projection C# publishes each update
(stream `rusty-riders`, contract `rusty.riders.hud`, written by `Ui/Hud.cs`). Every change the player makes on a
screen is a claim to a C# owner. The product project selects this directory and module for SDK staging.

## Modules

| Module | Owns |
| --- | --- |
| `main.js` | Composition, which screen is open, focus and pause (I and C open screens, Esc closes); registers the title font from the facts |
| `hud.js` | The in-view HUD: vitals, hands, the chase banner, the world-time ring, notice, prompt, action, rift label, run summary, controls |
| `inventory-screen.js` | Weapons (click for the main hand, right-click for the off hand), worn armour (click to take off), carried stacks (click to use or wear) |
| `sheet-screen.js` | Attributes, derived stats with what they are made of, resistances by damage kind |
| `art.js` | URLs of the old game's UI art and icons |
| `pause.js` | The pause request flow (from rusty-hotel) |
| `riders.css` | The look: the old frames as CSS `border-image` 9-slices over the brushed fill |

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

## Art

`scripts/import-ui-art.py` copies the old Rift Riders UI art into `src/ui/art/`:
- frames (the red skull hotbar, the skull vitals corner, the round skull button, the horned violet 9-slice panel
  and slot, the crest and the horned ring), downscaled 4x;
- icons (equipment, items, junk and damage kinds), at 128 px.

The CSS slice values are the Unity sprite borders divided by 4. Line-art icons are tinted green, as the old game's
hotbar icons were; painted ones show as they are. Rerun the script to regenerate the folder from `old-game/`.

Fonts are not UI-root files: the host refuses font files there. The title face (Alagard, the old game's pixel
fantasy font) is `content/ui/alagard.ttf`, granted by `Ui/UiFonts.cs` through `Ui.OpenFont`. Its URL arrives as
`titleFont` in the facts, and `main.js` registers it with `FontFace`.

Keep only browser assets in `src/ui/`. Keep this lane to DOM presentation, accessibility and semantic actions.
Game state lives in C#; input delivery, projection transport, the canvas and rendering belong to the Engine. Dispose
event listeners and subscriptions when the host unmounts the UI.
