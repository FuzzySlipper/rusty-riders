/**
 * DOM-only product UI. Engine owns the canvas, pointer lock, input delivery and projection
 * transport; this module only shows the facts C# publishes.
 */
export function mountProductUi(root, context) {
  const panel = document.createElement('div');
  panel.className = 'riders-gallery';
  panel.innerHTML = `
    <style>
      .riders-gallery { position:fixed; inset:0; pointer-events:none; color:#eef2f7; font:13px/1.5 system-ui,sans-serif; }
      .riders-gallery header { position:absolute; top:20px; left:24px; border-left:3px solid #e0763a; padding-left:12px; text-shadow:0 1px 3px #000; }
      .riders-gallery h1 { margin:0; font-size:20px; font-weight:600; }
      .riders-gallery .status { color:#cfd8e3; font-size:12px; }
      .riders-gallery .problems { white-space:pre-line; color:#f2b48c; font:11px/1.5 ui-monospace,monospace; max-width:60ch; }
      .riders-gallery .problems:empty { display:none; }
      .riders-gallery .exhibit { position:absolute; bottom:64px; left:50%; transform:translateX(-50%); background:#121922cc; border-radius:6px; padding:6px 14px; font-size:15px; white-space:nowrap; }
      .riders-gallery .exhibit:empty { display:none; }
      .riders-gallery footer { position:absolute; bottom:20px; left:24px; right:24px; display:flex; justify-content:space-between; color:#b9c5d3; font-size:12px; text-shadow:0 1px 3px #000; }
      .riders-gallery kbd { color:#fff; font:11px ui-monospace,monospace; background:#ffffff1c; padding:1px 4px; border-radius:3px; }
      .riders-gallery .clock { position:absolute; top:20px; right:24px; text-align:right; text-shadow:0 1px 3px #000; }
      .riders-gallery .clock .state { font-size:15px; font-weight:600; letter-spacing:.04em; text-transform:uppercase; color:#8fd3ff; }
      .riders-gallery .clock .state[data-held] { color:#cfd8e3; }
      .riders-gallery .clock .world { color:#b9c5d3; font:12px ui-monospace,monospace; }
      .riders-gallery .reticle { position:absolute; left:50%; top:50%; width:4px; height:4px; border-radius:50%; background:#fff; box-shadow:0 0 0 2px #0007; transform:translate(-50%,-50%); }
    </style>
    <header><h1>Rusty Riders</h1><div class="status" data-fact="status">Loading</div><div class="problems" data-fact="problems"></div></header>
    <div class="clock"><div class="state" data-fact="time"></div><div class="world" data-fact="worldTime"></div></div>
    <div class="reticle"></div>
    <output class="exhibit" data-fact="exhibit" aria-live="polite"></output>
    <footer><span>Click to capture the mouse · <kbd>WASD</kbd> move · <kbd>Shift</kbd> sprint · <kbd>Space</kbd> jump · <kbd>T</kbd> wait · <kbd>F</kbd> fly (<kbd>Space</kbd>/<kbd>Ctrl</kbd> up/down) · <kbd>R</kbd> back to start · <kbd>N</kbd> new level · <kbd>B</kbd> tiles / shells / sweeps · <kbd>V</kbd> floor texture · <kbd>G</kbd> level / gallery · <kbd>Esc</kbd> release</span><span data-fact="position"></span></footer>`;
  root.append(panel);

  const fields = [...panel.querySelectorAll('[data-fact]')];
  const clockState = panel.querySelector('.clock .state');
  const unsubscribe = context?.projection?.subscribe?.((envelope) => {
    const facts = envelope?.value;
    if (facts === undefined || facts === null) return;
    for (const field of fields) field.textContent = facts[field.dataset.fact] ?? '';
    clockState.toggleAttribute('data-held', facts.time === 'held');
  });

  return Object.freeze({
    dispose: () => {
      unsubscribe?.();
      panel.remove();
    },
  });
}
