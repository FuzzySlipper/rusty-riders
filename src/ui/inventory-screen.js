import { icon } from './art.js';

/**
 * The inventory screen: carried weapons (take one in hand), worn armour (take a piece off), and the item stacks
 * (wear spare armour, use a consumable). Every change is a claim the C# inventory applies; the screen redraws from facts.
 */
export function mountInventoryScreen(document, intents) {
  const element = document.createElement('section');
  element.className = 'screen panel inventory-screen';
  element.setAttribute('aria-label', 'Inventory');
  element.innerHTML = `
    <i class="orn crest"></i>
    <div class="body">
    <h2>Pack</h2>
    <div class="meta"><span class="slots"></span><span class="haul"></span></div>
    <h3>Weapons</h3><div class="grid weapons"></div>
    <h3>Worn</h3><div class="grid worn"></div>
    <h3>Carried</h3><div class="grid stacks"></div>
    <footer><kbd>I</kbd> or <kbd>Esc</kbd> back to the rift · click a weapon to take it in hand</footer>
    </div>`;
  const grids = { weapons: element.querySelector('.weapons'), worn: element.querySelector('.worn'), stacks: element.querySelector('.stacks') };
  const claim = (action, index) => {
    try {
      intents?.claim('riders.inventory', { kind: 'product-payload', contract: 'riders.inventory.v1', data: { action, index } });
    } catch (error) {
      console.warn('inventory claim refused', error);
    }
  };
  const slot = (entry, caption, actions) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'slot';
    button.append(icon(document, entry.icon));
    const label = document.createElement('span');
    label.className = 'caption';
    label.textContent = caption;
    button.append(label);
    button.title = entry.adds ? `${entry.name} (${entry.adds})` : entry.name;
    button.addEventListener('click', () => actions.primary?.());
    button.addEventListener('contextmenu', event => { event.preventDefault(); actions.secondary?.(); });
    return button;
  };

  function draw(facts) {
    const inventory = facts.inventory;
    if (!inventory) return;
    element.querySelector('.slots').textContent = `${inventory.used} / ${inventory.slots} slots`;
    element.querySelector('.haul').textContent = facts.haul ?? '';
    grids.weapons.replaceChildren(...inventory.weapons.map(weapon => {
      const button = slot(weapon, weapon.held ? `${weapon.name} · in hand` : weapon.name, { primary: () => claim('hold', weapon.index) });
      button.toggleAttribute('data-held', weapon.held);
      return button;
    }));
    grids.worn.replaceChildren(...inventory.worn.map(piece => slot(piece, `${piece.name} · ${piece.adds}`, { primary: () => claim('takeOff', piece.index) })));
    grids.stacks.replaceChildren(...inventory.stacks.map(stack => {
      const button = slot(stack, stack.count > 1 ? `${stack.name} ×${stack.count}` : stack.name, {
        primary: stack.usable ? () => claim('use', stack.index) : stack.wearable ? () => claim('wear', stack.index) : null,
      });
      button.disabled = !stack.usable && !stack.wearable;
      return button;
    }));
  }

  return { name: 'inventory', element, draw, enter() { element.querySelector('button')?.focus(); }, leave() {} };
}
