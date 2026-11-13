// Controlla i rapporti di contrasto (WCAG 2.2 AA) delle coppie di colori di src/styles.css, nei due temi.
// Si lancia con `npm run contrast`; la CI lo esegue, così un colore cambiato non fa tornare indietro.
// Testo normale: 4,5:1. Icone e segni che servono a capire l'interfaccia: 3:1.
import { readFileSync } from 'node:fs';

const css = readFileSync(new URL('../src/styles.css', import.meta.url), 'utf8');

function tokens(selector) {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`Blocco ${selector} non trovato in styles.css`);
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start));
  return Object.fromEntries([...body.matchAll(/--([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)].map((m) => [m[1], m[2]]));
}

const dark = tokens(':root');
const themes = { scuro: dark, chiaro: { ...dark, ...tokens(":root[data-theme='light']") } };

const channel = (value) => {
  const c = value / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};
const luminance = (hex) => {
  const [r, g, b] = [1, 3, 5].map((i) => channel(parseInt(hex.slice(i, i + 2), 16)));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const ratio = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const surfaces = ['main', 'side', 'panel', 'card', 'card-hover', 'card-selected'];
// [primo piano, sfondi, minimo]
const rules = [
  ['text', surfaces, 4.5],
  ['muted', surfaces, 4.5],
  ['accent-fg', surfaces, 4.5],
  ['accent-text', ['accent'], 4.5],
  ...['c-pr', 'c-issue', 'c-review', 'c-assigned', 'c-notifications', 'c-mentions'].map((c) => [c, ['main', 'card', 'card-hover'], 4.5]),
  ['badge-ink', ['c-notifications'], 4.5],
  ['danger', ['danger-bg', 'main'], 4.5],
  ['warn', ['warn-bg', 'main'], 4.5],
  ['ok', ['ok-bg', 'main'], 4.5],
  // Icone e segni: 3:1.
  ['accent', ['main', 'side', 'panel'], 3],
  ['star', ['card', 'card-hover', 'card-selected', 'panel'], 3],
  ['tile', ['card', 'card-hover', 'card-selected', 'panel'], 3],
  ['tile-ink', ['tile'], 3],
];

let failures = 0;
for (const [name, theme] of Object.entries(themes)) {
  for (const [fg, backgrounds, min] of rules) {
    for (const bg of backgrounds) {
      if (!theme[fg] || !theme[bg]) throw new Error(`Token mancante: ${fg} o ${bg} (tema ${name})`);
      const value = ratio(theme[fg], theme[bg]);
      if (value < min) {
        failures += 1;
        console.error(`✗ tema ${name}: --${fg} ${theme[fg]} su --${bg} ${theme[bg]} = ${value.toFixed(2)} (serve ${min})`);
      }
    }
  }
}

if (failures > 0) {
  console.error(`\n${failures} coppie sotto WCAG AA.`);
  process.exit(1);
}
console.log('Contrasti: tutte le coppie rispettano WCAG AA.');
