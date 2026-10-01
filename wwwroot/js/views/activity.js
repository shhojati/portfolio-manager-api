// Live feed of the WebSocket messages, so you can watch the sync jobs and manual edits land.

import { h, icon, batched } from '../dom.js';
import { clock, dayKey, money, onUnitChange } from '../format.js';
import { realtime } from '../realtime.js';
import { assetById, loadAssets, onAssetsChange } from '../store.js';

const MAX_EVENTS = 300;
const events = []; // newest first; kept across page visits
const counts = { 'asset.created': 0, 'price.created': 0, 'price.updated': 0 };
let unseen = 0;
let paused = false;
let onChange = null;

realtime.addEventListener('message', e => {
  if (!(e.detail.type in counts)) return;
  counts[e.detail.type]++;
  if (paused) return;
  events.unshift({ ...e.detail, at: new Date() });
  if (events.length > MAX_EVENTS) events.length = MAX_EVENTS;
  if (onChange) onChange();
  else setUnseen(unseen + 1);
});

function setUnseen(n) {
  unseen = n;
  const badge = document.getElementById('activity-badge');
  badge.hidden = n === 0;
  badge.textContent = n > 99 ? '99+' : String(n);
}

const LABELS = { 'asset.created': 'Asset created', 'price.created': 'Price added', 'price.updated': 'Price updated' };

export function renderActivity(main) {
  setUnseen(0);
  loadAssets().catch(() => {}); // for symbol lookups

  const tiles = h('div', { class: 'card stats' });
  const list = h('ul', { class: 'feed' });
  const pauseBtn = h('button', { type: 'button', class: 'btn', onClick: togglePause });

  main.replaceChildren(h('div', { class: 'page' },
    h('div', { class: 'page-head' },
      h('div', null,
        h('h1', null, 'Live activity'),
        h('p', { class: 'sub' }, 'Changes pushed over the real-time connection since this page was opened.')),
      h('div', { class: 'page-actions' },
        pauseBtn,
        h('button', { type: 'button', class: 'btn', onClick: () => { events.length = 0; render(); } }, icon('trash'), 'Clear'))),
    tiles,
    h('div', { class: 'card' },
      h('div', { class: 'card-head' }, h('h2', null, 'Events'), h('span', { class: 'muted' }, `Last ${MAX_EVENTS} kept`)),
      list)));

  function togglePause() {
    paused = !paused;
    render();
  }

  function describe(ev) {
    const d = ev.data;
    if (ev.type === 'asset.created') {
      return [h('a', { href: `#/assets/${d.id}`, class: 'sym' }, d.symbol), ' ', h('span', { class: 'muted', dir: 'auto' }, d.name)];
    }
    const asset = assetById(d.assetId);
    return [
      h('a', { href: `#/assets/${d.assetId}`, class: 'sym' }, asset?.symbol ?? `#${d.assetId}`),
      ' ', h('span', { class: 'muted' }, dayKey(d.date)),
    ];
  }

  function render() {
    pauseBtn.replaceChildren(icon(paused ? 'play' : 'pause'), paused ? 'Resume' : 'Pause');
    tiles.replaceChildren(
      ...Object.entries(counts).map(([type, n]) => h('div', { class: 'stat' },
        h('div', { class: 'label' }, LABELS[type]),
        h('div', { class: 'value num' }, n.toLocaleString('en-US')))),
      h('div', { class: 'stat' },
        h('div', { class: 'label' }, 'Connection'),
        h('div', { class: 'value', style: 'font-size:16px;margin-top:8px' },
          { open: 'Connected', connecting: 'Connecting…', closed: 'Disconnected — retrying' }[realtime.status])));

    if (!events.length) {
      list.replaceChildren(h('li', { class: 'empty', style: 'display:block' },
        h('strong', null, paused ? 'Paused' : 'Waiting for changes…'),
        'Price syncs run on a schedule; creating or editing a price here shows up too.'));
      return;
    }
    list.replaceChildren(...events.map(ev => h('li', null,
      h('span', { class: 'time' }, clock(ev.at)),
      h('span', null, h('span', { class: `badge ${ev.type === 'asset.created' ? 'ev-asset' : ev.type === 'price.created' ? 'ev-created' : ''}` }, LABELS[ev.type])),
      h('span', { class: 'what' }, describe(ev)),
      h('span', { class: 'val num' }, ev.type === 'asset.created' ? ev.data.type : money(ev.data.value)))));
  }

  const rerender = batched(render);
  onChange = rerender;
  const offStatus = () => realtime.removeEventListener('status', rerender);
  realtime.addEventListener('status', rerender);
  const offUnit = onUnitChange(rerender);
  const offAssets = onAssetsChange(rerender);
  render();

  return () => { onChange = null; offStatus(); offUnit(); offAssets(); };
}
