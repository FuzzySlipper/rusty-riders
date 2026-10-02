# DOM companion

`src/ui/main.js` exports `mountProductUi`. It shows the gallery facts C# publishes
(what loaded, what is missing, the nearest exhibit and the walker position) and
the controls; it submits no intents. The product project
selects this directory and module for SDK staging.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility, and semantic actions.
Gallery and walker state live in C#; input delivery, projection transport, the canvas,
and rendering belong to Engine. Dispose event listeners and subscriptions
when the host unmounts the UI.
