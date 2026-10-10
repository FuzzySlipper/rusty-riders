import { icon } from './art.js';

/** The character sheet: attributes, derived stats with what they are made of, and resistances by damage kind. */
export function mountSheetScreen(document) {
  const element = document.createElement('section');
  element.className = 'screen panel sheet-screen';
  element.setAttribute('aria-label', 'Character');
  element.innerHTML = `
    <i class="orn crest"></i>
    <div class="body">
    <h2>Rider</h2>
    <div class="columns">
      <div><h3>Attributes</h3><dl class="attributes"></dl><h3>Resistances</h3><div class="resistances"></div></div>
      <div><h3>Derived</h3><dl class="derived"></dl></div>
    </div>
    <footer><kbd>C</kbd> or <kbd>Esc</kbd> back to the rift</footer>
    </div>`;
  const row = (name, value, detail) => {
    const fragment = document.createDocumentFragment();
    const term = document.createElement('dt');
    term.textContent = name;
    const description = document.createElement('dd');
    description.textContent = value;
    if (detail) {
      const small = document.createElement('small');
      small.textContent = detail;
      description.append(small);
    }
    fragment.append(term, description);
    return fragment;
  };
  const number = value => (Math.round(value * 100) / 100).toString();

  function draw(facts) {
    const sheet = facts.sheet;
    if (!sheet) return;
    element.querySelector('.attributes').replaceChildren(...sheet.attributes.map(a => row(a.name, number(a.value))));
    element.querySelector('.derived').replaceChildren(...sheet.derived.map(d => row(d.name, number(d.value), d.detail)));
    element.querySelector('.resistances').replaceChildren(...sheet.resistances.map(r => {
      const chip = document.createElement('div');
      chip.className = 'resistance';
      chip.append(icon(document, r.icon));
      const label = document.createElement('span');
      label.textContent = `${r.name} ${Math.round(r.value * 100)}%`;
      chip.append(label);
      return chip;
    }));
  }

  return { name: 'sheet', element, draw, enter() {}, leave() {} };
}
