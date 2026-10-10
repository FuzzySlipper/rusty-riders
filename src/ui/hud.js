import { icon } from './art.js';

/** The in-view HUD: vitals, hands, the chase, the world's time, notices and prompts, and the run summary. */
export function mountHud(document) {
  const element = document.createElement('div');
  element.className = 'hud';
  element.innerHTML = `
    <header class="dev"><h1>Rusty Riders</h1><div class="status" data-fact="status"></div><div class="problems" data-fact="problems"></div></header>
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
    <div class="hotbar redbar">
      <i class="orn rail"></i><i class="orn left"></i><i class="orn right"></i>
      <div class="hand" data-hand="0"><div class="socket"><i class="orn ring"></i></div><div class="name"></div><div class="load"></div><kbd>LMB</kbd></div>
      <div class="hand" data-hand="1"><div class="socket"><i class="orn ring"></i></div><div class="name"></div><div class="load"></div><kbd>RMB</kbd></div>
      <div class="hand ready"><div class="socket"><i class="orn ring"></i></div><div class="name" data-fact="ready"></div><kbd>Q</kbd></div>
      <div class="run"><div data-fact="haul"></div><div data-fact="bank"></div></div>
    </div>
    <footer class="controls"><span><kbd>WASD</kbd> move · <kbd>Shift</kbd> sprint · <kbd>Space</kbd> jump · <kbd>T</kbd> wait · <kbd>R</kbd> reload · <kbd>E</kbd> open/take · <kbd>1</kbd>–<kbd>9</kbd> weapon · <kbd>I</kbd> inventory · <kbd>C</kbd> character · <kbd>F</kbd> fly · <kbd>H</kbd> back to start · <kbd>N</kbd>/<kbd>B</kbd>/<kbd>V</kbd>/<kbd>G</kbd> developer</span><span data-fact="position"></span></footer>`;
  const fields = [...element.querySelectorAll('[data-fact]')];
  const healthFill = element.querySelector('.health i');
  const healthValue = element.querySelector('.health .value');
  const tracks = element.querySelector('.tracks');
  const effects = element.querySelector('.effects');
  const clock = element.querySelector('.clock');
  const action = element.querySelector('.action');
  const progress = element.querySelector('.action i');
  const hands = [...element.querySelectorAll('.hand[data-hand]')];
  const ready = element.querySelector('.hand.ready');
  let readyIcon = null;
  const handIcons = [null, null];

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
    (facts.hands ?? []).forEach((hand, index) => {
      const slot = hands[index];
      if (!slot) return;
      if (handIcons[index] !== hand.icon) {
        handIcons[index] = hand.icon;
        setIcon(slot.querySelector('.socket'), icon(document, hand.icon));
      }
      slot.querySelector('.name').textContent = hand.name;
      slot.querySelector('.load').textContent = hand.size > 0 ? `${hand.loaded}/${hand.size}` : '';
      slot.toggleAttribute('data-acting', hand.acting === true);
    });
    if (readyIcon !== facts.readyIcon) {
      readyIcon = facts.readyIcon;
      setIcon(ready.querySelector('.socket'), icon(document, readyIcon));
    }
    ready.toggleAttribute('data-empty', !facts.readyIcon);
  }

  return { element, draw };
}
