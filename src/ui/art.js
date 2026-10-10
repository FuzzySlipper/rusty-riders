/**
 * The old Rift Riders UI art copied into ./art by scripts/import-ui-art.py: frame and icon URLs. Icon names come from
 * the facts (each item and damage kind names its icon in content); the line-art ones are white and drawn tinted.
 */
const PAINTED = new Set(['ammo', 'wand', 'medkit', 'tonic', 'shield-cell', 'charge', 'scrap', 'shard']);

export const artUrl = name => new URL(`./art/${name}`, import.meta.url).href;

/** An <img> for an icon name, or an empty span for none. */
export function icon(document, name, className = 'icon') {
  if (!name) return document.createElement('span');
  const image = document.createElement('img');
  image.className = `${className}${PAINTED.has(name) ? '' : ' line'}`;
  image.src = artUrl(`icons/${name}.png`);
  image.alt = '';
  image.draggable = false;
  return image;
}
