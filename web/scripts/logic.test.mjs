// Prove della logica del frontend che non dipende dal browser (si lanciano con `npm test`, in Node, senza dipendenze).
// Node legge direttamente i file TypeScript (solo import di tipi, che spariscono).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { findNew } from '../src/newItems.ts';
import { isHidden, newComments, tomorrowMorning } from '../src/seen.ts';
import { latestPerReviewer, newCommentsSince, reviewBadge } from '../src/review.ts';
import { ciState } from '../src/checks.ts';
import { searchNearlyExhausted } from '../src/limits.ts';

const item = (over) => ({ id: 'a:review:1', account: 'a', section: 'review', type: 'pr', title: 't', repo: 'o/r', url: 'https://github.com/o/r/pull/1', updatedAt: '2026-11-10T10:00:00Z', comments: 0, ...over });
const data = (...items) => ({ items });

test('findNew: il primo aggiornamento non avvisa di nulla', () => {
  assert.deepEqual(findNew(null, data(item({}), item({ id: 'a:mentions:2', section: 'mentions', url: 'u2' }))), { mentions: [], reviews: [] });
});

test('findNew: una menzione e una review comparse dopo sono nuove, il resto no', () => {
  const before = data(item({ id: 'a:review:1' }), item({ id: 'a:assigned:9', section: 'assigned', url: 'u9' }));
  const after = data(
    item({ id: 'a:review:1' }),
    item({ id: 'a:review:2', url: 'https://github.com/o/r/pull/2' }),
    item({ id: 'a:mentions:3', section: 'mentions', url: 'u3' }),
    item({ id: 'a:assigned:10', section: 'assigned', url: 'u10' }),
  );
  const found = findNew(before, after);
  assert.deepEqual(found.reviews.map((i) => i.id), ['a:review:2']);
  assert.deepEqual(found.mentions.map((i) => i.id), ['a:mentions:3']);
});

test('findNew: lo stesso elemento in due sezioni è nuovo in ciascuna', () => {
  const before = data(item({ id: 'a:review:1', url: 'x' }));
  const after = data(item({ id: 'a:review:1', url: 'x' }), item({ id: 'a:mentions:1', section: 'mentions', url: 'x' }));
  assert.equal(findNew(before, after).mentions.length, 1);
  assert.equal(findNew(before, after).reviews.length, 0);
});

test('findNew: lo stesso indirizzo visto da due account si avvisa una volta sola', () => {
  const after = data(item({ id: 'a:review:5', url: 'same' }), item({ id: 'b:review:5', account: 'b', url: 'same' }));
  assert.equal(findNew(data(), after).reviews.length, 1);
});

test('findNew: ciò che è già visto o rimandato non si avvisa', () => {
  const after = data(item({ id: 'a:review:2', url: 'hidden' }), item({ id: 'a:review:3', url: 'shown' }));
  const found = findNew(data(), after, (i) => i.url === 'hidden');
  assert.deepEqual(found.reviews.map((i) => i.url), ['shown']);
});

test('isHidden: visto nasconde finché non cambia, rimandato finché non scade, il punto di partenza non nasconde', () => {
  const now = Date.parse('2026-11-14T12:00:00Z');
  const base = { url: 'u', updatedAt: '2026-11-10T10:00:00Z', snoozeUntil: null, savedAt: '', comments: null, hidden: true };
  assert.equal(isHidden(undefined, { updatedAt: base.updatedAt }, now), false);
  assert.equal(isHidden(base, { updatedAt: '2026-11-10T10:00:00Z' }, now), true);
  assert.equal(isHidden(base, { updatedAt: '2026-11-11T10:00:00Z' }, now), false, 'cambiato dopo: riappare');
  assert.equal(isHidden({ ...base, snoozeUntil: '2026-11-15T08:00:00Z' }, { updatedAt: base.updatedAt }, now), true);
  assert.equal(isHidden({ ...base, snoozeUntil: '2026-11-14T08:00:00Z' }, { updatedAt: base.updatedAt }, now), false, 'scaduto');
  assert.equal(isHidden({ ...base, hidden: false }, { updatedAt: base.updatedAt }, now), false, 'solo punto di partenza');
});

test('newComments: conta solo i commenti in più e mai meno di zero', () => {
  const entry = { url: 'u', updatedAt: '', snoozeUntil: null, savedAt: '', comments: 5, hidden: false };
  assert.equal(newComments(entry, { comments: 8 }), 3);
  assert.equal(newComments(entry, { comments: 5 }), 0);
  assert.equal(newComments(entry, { comments: 2 }), 0);
  assert.equal(newComments(undefined, { comments: 8 }), 0);
  assert.equal(newComments({ ...entry, comments: null }, { comments: 8 }), 0);
});

test('tomorrowMorning: le 8 del giorno dopo, anche a fine mese', () => {
  const t = tomorrowMorning(new Date(2026, 10, 30, 23, 59));
  assert.deepEqual([t.getFullYear(), t.getMonth(), t.getDate(), t.getHours(), t.getMinutes()], [2026, 11, 1, 8, 0]);
});

test('reviewBadge e latestPerReviewer', () => {
  assert.equal(reviewBadge({ decision: 'APPROVED' }), 'approved');
  assert.equal(reviewBadge({ decision: 'CHANGES_REQUESTED' }), 'changes');
  assert.equal(reviewBadge({ decision: 'REVIEW_REQUIRED' }), 'required');
  assert.equal(reviewBadge({ decision: null }), null);
  assert.equal(reviewBadge(undefined), null);
  const reviews = [
    { login: 'Carla', state: 'APPROVED', at: '2026-11-09T10:00:00Z' },
    { login: 'carla', state: 'COMMENTED', at: '2026-11-10T10:00:00Z' },
    { login: 'dario', state: 'CHANGES_REQUESTED', at: '2026-11-08T10:00:00Z' },
  ];
  assert.deepEqual(latestPerReviewer(reviews).map((r) => `${r.login}:${r.state}`), ['carla:COMMENTED', 'dario:CHANGES_REQUESTED']);
});

test('newCommentsSince: serve un «visto» da cui contare', () => {
  const pr = { review: { comments: [{ login: 'a', at: '2026-11-10T09:00:00Z' }, { login: 'b', at: '2026-11-10T11:00:00Z' }] } };
  assert.equal(newCommentsSince(pr, undefined), null);
  assert.equal(newCommentsSince({ review: null }, { updatedAt: '2026-11-10T10:00:00Z' }), null);
  assert.equal(newCommentsSince(pr, { updatedAt: '2026-11-10T10:00:00Z' }), 1);
});

test('ciState: stati di GitHub riportati a riuscita, fallita o in corso', () => {
  assert.equal(ciState({ state: 'SUCCESS' }), 'success');
  assert.equal(ciState({ state: 'FAILURE' }), 'failure');
  assert.equal(ciState({ state: 'ERROR' }), 'failure');
  assert.equal(ciState({ state: 'PENDING' }), 'pending');
  assert.equal(ciState({ state: 'EXPECTED' }), 'pending');
  assert.equal(ciState(null), null);
});

test('searchNearlyExhausted: aspetta solo con poche ricerche e finestra non ancora azzerata', () => {
  const now = Date.parse('2026-11-14T12:00:00Z');
  const account = (remaining, resetsAt) => ({ rateLimits: [{ resource: 'search', limit: 30, remaining, resetsAt }] });
  assert.equal(searchNearlyExhausted([account(2, '2026-11-14T12:00:30Z')], now), true);
  assert.equal(searchNearlyExhausted([account(3, '2026-11-14T12:00:30Z')], now), true);
  assert.equal(searchNearlyExhausted([account(4, '2026-11-14T12:00:30Z')], now), false);
  assert.equal(searchNearlyExhausted([account(0, '2026-11-14T11:59:00Z')], now), false, 'finestra già azzerata');
  assert.equal(searchNearlyExhausted([{ rateLimits: [{ resource: 'core', limit: 5000, remaining: 0, resetsAt: '2026-11-14T13:00:00Z' }] }], now), false, 'solo la ricerca conta');
  assert.equal(searchNearlyExhausted(undefined, now), false);
});

// --- Home personalizzabile ---
import { COLLAPSED_CHOICES, defaultHomePrefs, isDefaultHome, moveSection, normalizeHomePrefs, toggleSection, visibleSections } from '../src/homePrefs.ts';

const HOME = ['review', 'assigned', 'notifications', 'mentions', 'mine-prs', 'mine-issues'];

test('homePrefs: i valori predefiniti mostrano tutto nell\'ordine di sempre con 6 card', () => {
  const prefs = defaultHomePrefs(HOME);
  assert.deepEqual(visibleSections(prefs), HOME);
  assert.equal(prefs.collapsed, 6);
  assert.equal(isDefaultHome(prefs, HOME), true);
});

test('homePrefs: normalizza preferenze vecchie o rovinate', () => {
  assert.deepEqual(normalizeHomePrefs(null, HOME), defaultHomePrefs(HOME));
  assert.deepEqual(normalizeHomePrefs('boh', HOME), defaultHomePrefs(HOME));
  const p = normalizeHomePrefs({ order: ['mine-prs', 'sezione-che-non-esiste', 'mine-prs', 'review'], hidden: ['mentions', 'altra'], collapsed: 12 }, HOME);
  assert.deepEqual(p.order, ['mine-prs', 'review', 'assigned', 'notifications', 'mentions', 'mine-issues'], 'doppioni e sconosciute tolte, le mancanti in fondo');
  assert.deepEqual(p.hidden, ['mentions']);
  assert.equal(p.collapsed, 12);
});

test('homePrefs: il numero di card è uno di quelli ammessi', () => {
  assert.equal(normalizeHomePrefs({ collapsed: 7 }, HOME).collapsed, 6);
  assert.equal(normalizeHomePrefs({ collapsed: '12' }, HOME).collapsed, 6);
  for (const n of COLLAPSED_CHOICES) assert.equal(normalizeHomePrefs({ collapsed: n }, HOME).collapsed, n);
});

test('homePrefs: non si può nascondere tutto', () => {
  assert.deepEqual(normalizeHomePrefs({ hidden: HOME }, HOME).hidden, []);
  let prefs = defaultHomePrefs(HOME);
  for (const s of HOME.slice(0, 5)) prefs = toggleSection(prefs, s);
  assert.deepEqual(visibleSections(prefs), ['mine-issues']);
  assert.deepEqual(toggleSection(prefs, 'mine-issues'), prefs, "l'ultima sezione resta");
  assert.deepEqual(visibleSections(toggleSection(prefs, 'review')), ['review', 'mine-issues']);
});

test('homePrefs: spostare una sezione scambia con la vicina e non esce dai bordi', () => {
  const prefs = defaultHomePrefs(HOME);
  assert.deepEqual(moveSection(prefs, 'assigned', -1).order.slice(0, 2), ['assigned', 'review']);
  assert.deepEqual(moveSection(prefs, 'review', 1).order.slice(0, 2), ['assigned', 'review']);
  assert.deepEqual(moveSection(prefs, 'review', -1), prefs);
  assert.deepEqual(moveSection(prefs, 'mine-issues', 1), prefs);
  assert.equal(isDefaultHome(moveSection(prefs, 'review', 1), HOME), false);
});

test('homePrefs: le sezioni nascoste non compaiono ma restano nell\'ordine', () => {
  const prefs = toggleSection(moveSection(defaultHomePrefs(HOME), 'mine-prs', -1), 'assigned');
  assert.deepEqual(visibleSections(prefs), ['review', 'notifications', 'mine-prs', 'mentions', 'mine-issues']);
  assert.ok(prefs.order.includes('assigned'));
  assert.equal(isDefaultHome(prefs, HOME), false);
});
