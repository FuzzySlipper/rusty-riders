import { icon } from './art.js';

/** The in-view HUD: vitals, hands, the chase, the world's time, notices and prompts, and the run summary. */
export function mountHud(document) {
  const element = document.createElement('div');
  element.className = 'hud';
  element.innerHTML = `
    <header class="dev"><h1>Rusty Riders</h1><div class="status" data-fact="status"></div><div class="problems" data-fact="problems"></div>
      <div class="keys"><span><kbd>WASD</kbd>move</span><span><kbd>Shift</kbd>sprint</span><span><kbd>Space</kbd>jump</span><span><kbd>T</kbd>wait</span><span><kbd>R</kbd>reload</span><span><kbd>E</kbd>open/take</span><span><kbd>1–9</kbd>weapon</span><span><kbd>I</kbd>inventory</span><span><kbd>C</kbd>character</span><span><kbd>F</kbd>fly</span><span><kbd>H</kbd>to start</span><span><kbd>N B V G F2</kbd>developer</span></div><div class="position" data-fact="position"></div></header>
    <div class="banner redbar"><i class="orn rail"></i><i class="orn left"></i><i class="orn right"></i><div class="chase" data-fact="chase"></div><div class="line"><span class="hostiles" data-fact="hostiles"></span><span class="depth" data-fact="depth"></span></div></div>
    <div class="clock"><i class="orn ring"></i><div class="state" data-fact="time"></div><div class="world" data-fact="worldTime"></div></div>
    <div class="notice" data-fact="notice"></div>
    <div class="reticle"></div>
    <div class="action"><span data-fact="action"></span><div class="progress"><i></i></div></div>
    <div class="prompt chip" data-fact="prompt"></div>
    <output class="exhibit chip" data-fact="exhibit" aria-live="polite"></output>
    <div class="summary panel" data-fact="summary"></div>
    <div class="vitals">
      <i class="orn rail"></i><i class="orn corner"></i>
      <div class="health"><div class="bar"><i></i></div><span class="value"></span></div>
      <div class="tracks"></div>
      <div class="effects"></div>
    </div>
    <div class="hotbar framed">
      <div class="hand" data-hand><div class="socket"><i class="orn ring"></i></div><div class="name"></div><div class="load"></div><kbd>LMB</kbd></div>
      <div class="hand ready"><div class="socket"><i class="orn ring"></i></div><div class="name" data-fact="ready"></div><kbd>Q</kbd></div>
      <div class="run"><div data-fact="haul"></div><div data-fact="bank"></div></div>
    </div>
`;
  const fields = [...element.querySelectorAll('[data-fact]')];
  const healthFill = element.querySelector('.health i');
  const healthValue = element.querySelector('.health .value');
  const tracks = element.querySelector('.tracks');
  const effects = element.querySelector('.effects');
  const clock = element.querySelector('.clock');
  const action = element.querySelector('.action');
  const progress = element.querySelector('.action i');
  const hand = element.querySelector('.hand[data-hand]');
  const ready = element.querySelector('.hand.ready');
  let readyIcon = null;
  let handIcon = null;

  // A socket keeps its ring ornament; only the icon after it changes.
  const setIcon = (socket, image) => socket.replaceChildren(socket.firstElementChild, image);

  function draw(facts) {
    for (const field of fields) field.textContent = facts[field.dataset.fact] ?? '';
    const share = facts.healthMax > 0 ? Math.max(0, Math.min(1, facts.health / facts.healthMax)) : 0;
    healthFill.style.width = `${share * 100}%`;
    healthValue.textContent = `${facts.health} / ${facts.healthMax}`;
    tracks.replaceChildren(...(facts.tracks ?? []).map(track => {
      const row = document.createElement('div');
      row.className = 'track';
      row.innerHTML = '<span class="label"></span><div class="bar"><i></i></div><span class="value"></span>';
      row.querySelector('.label').textContent = track.name;
      row.querySelector('i').style.width = `${track.max > 0 ? Math.min(1, track.value / track.max) * 100 : 0}%`;
      row.querySelector('.value').textContent = track.value;
      return row;
    }));
    effects.replaceChildren(...(facts.effects ?? []).map(effect => {
      const chip = document.createElement('span');
      chip.className = 'effect';
      chip.textContent = `${effect.mark} ${effect.text}`;
      return chip;
    }));
    clock.toggleAttribute('data-held', facts.held === true);
    action.toggleAttribute('data-active', !!facts.action);
    progress.style.width = `${Math.max(0, Math.min(1, facts.actionProgress ?? 0)) * 100}%`;
    if (facts.hand) {
      if (handIcon !== facts.hand.icon) {
        handIcon = facts.hand.icon;
        setIcon(hand.querySelector('.socket'), icon(document, handIcon));
      }
      hand.querySelector('.name').textContent = facts.hand.name;
      hand.querySelector('.load').textContent = facts.hand.size > 0 ? `${facts.hand.loaded}/${facts.hand.size}` : '';
      hand.toggleAttribute('data-acting', facts.hand.acting === true);
    }
    if (readyIcon !== facts.readyIcon) {
      readyIcon = facts.readyIcon;
      setIcon(ready.querySelector('.socket'), icon(document, readyIcon));
    }
    ready.toggleAttribute('data-empty', !facts.readyIcon);
  }

  return { element, draw };
}
