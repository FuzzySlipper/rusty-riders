import { mountHud } from './hud.js';
import { mountInventoryScreen } from './inventory-screen.js';
import { mountSheetScreen } from './sheet-screen.js';
import { createPauseFlow } from './pause.js';

/**
 * Composition, navigation and focus only. The HUD and each screen own their markup and drawing; the Engine owns the
 * canvas, pointer lock, input delivery, lifecycle and projection transport, and the C# owners own all game state.
 * A screen pauses the game while it is open and gives focus back to play when it closes.
 */
const SCREEN_KEYS = { KeyI: 'inventory', KeyC: 'sheet' };

export function mountProductUi(root, context) {
  const document = root.ownerDocument;
  const layer = document.createElement('section');
  layer.className = 'riders-ui';
  layer.innerHTML = `<link rel="stylesheet" href="${new URL('./riders.css', import.meta.url)}">`;
  layer.style.setProperty('--art', `url("${new URL('./art/', import.meta.url).href}")`);
  const hud = mountHud(document);
  const screens = Object.fromEntries([mountInventoryScreen(document, context?.intents), mountSheetScreen(document)].map(view => [view.name, view]));
  const foreground = document.createElement('div');
  foreground.className = 'foreground';
  foreground.hidden = true;
  foreground.setAttribute('data-rusty-ui-interactive', '');
  foreground.append(...Object.values(screens).map(view => view.element));
  layer.append(hud.element, foreground);
  root.append(layer);
  let screen = null;
  let disposed = false;
  let latest = null;

  const present = next => {
    for (const view of Object.values(screens)) view.leave();
    screen = next;
    foreground.hidden = next === null;
    for (const view of Object.values(screens)) view.element.hidden = view.name !== next;
    context?.ui?.setInteractionMode?.(next === null ? 'gameplay' : 'interface');
    if (next === null) context?.ui?.focusGameplay?.();
    else {
      if (latest) screens[next].draw(latest);
      screens[next].enter();
    }
  };
  const pause = createPauseFlow(context?.lifecycle, () => {});
  const show = async next => {
    if (disposed || pause.snapshot().pending) return;
    if (next === null) {
      if (await pause.resume() && !disposed) present(null);
    } else {
      present(next);
      await pause.pause();
    }
  };
  const onKey = event => {
    if (event.ctrlKey || event.altKey || event.metaKey || event.repeat) return;
    const target = SCREEN_KEYS[event.code];
    if (target && (screen === null || screen === target)) {
      event.preventDefault();
      event.stopPropagation();
      void show(screen === target ? null : target);
    } else if (event.code === 'Escape' && screen !== null) {
      event.preventDefault();
      event.stopPropagation();
      void show(null);
    }
  };
  let titleFont = '';
  const draw = envelope => {
    const facts = envelope?.value;
    if (disposed || facts === undefined || facts === null) return;
    latest = facts;
    if (facts.titleFont && facts.titleFont !== titleFont && typeof FontFace === 'function') {
      titleFont = facts.titleFont;
      new FontFace('Alagard', `url("${titleFont}")`).load().then(face => document.fonts.add(face), error => console.warn('title font', error));
    }
    hud.draw(facts);
    if (screen !== null) screens[screen].draw(facts);
  };
  document.addEventListener('keydown', onKey, true);
  const unsubscribe = context?.projection?.subscribe?.(draw);
  draw(context?.projection?.current?.());

  return Object.freeze({
    dispose() {
      disposed = true;
      unsubscribe?.();
      pause.dispose();
      document.removeEventListener('keydown', onKey, true);
      layer.remove();
    },
  });
}
