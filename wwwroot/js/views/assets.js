// Asset list: type filter, search, sorting, paging, latest prices (live) and CRUD.

import * as api from '../api.js';
import { h, icon, iconButton, batched } from '../dom.js';
import { dateTime, dayKey, fold, money, onUnitChange, unitLabel } from '../format.js';
import { realtime } from '../realtime.js';
import { allAssets, loadAssets, onAssetsChange } from '../store.js';
import { assetDialog, deleteAsset } from './forms.js';

const navigate = hash => { location.hash = hash; };

const PAGE_SIZE = 50;
const TYPE_ORDER = ['Stock', 'ETF', 'Fund', 'Crypto', 'Currency', 'Gold', 'Coin', 'Metal'];

export function renderAssets(main, _params, query) {
  const state = {
    type: query.get('type') ?? '',
    q: query.get('q') ?? '',
    sort: query.get('sort') ?? 'symbol',
    dir: query.get('dir') === 'desc' ? 'desc' : 'asc',
    page: Math.max(1, Number(query.get('page')) || 1),
  };
  const latest = new Map(); // assetId -> latest price
  const cleanups = [];
  let pageRows = [];

  const chips = h('div', { class: 'chips', role: 'group', 'aria-label': 'Filter by type' });
  const search = h('input', {
    class: 'input', type: 'search', placeholder: 'Search symbol, name or identifier…', value: state.q,
    'aria-label': 'Search assets',
    onInput: () => { state.q = search.value; state.page = 1; update(); },
  });
  const count = h('p', { class: 'sub' }, 'Loading…');
  const tableHost = h('div', { class: 'table-wrap' }, h('div', { class: 'skeleton' }, 'Loading assets…'));
  const pager = h('div', { class: 'pager', hidden: true });

  main.replaceChildren(h('div', { class: 'page' },
    h('div', { class: 'page-head' },
      h('div', null, h('h1', null, 'Assets'), count),
      h('div', { class: 'page-actions' },
        h('button', { type: 'button', class: 'btn', onClick: () => refresh(true), title: 'Reload from server' }, icon('refresh'), 'Refresh'),
        h('button', { type: 'button', class: 'btn btn-primary', onClick: create }, icon('plus'), 'New asset'))),
    h('div', { class: 'card' },
      h('div', { class: 'card-head' },
        h('div', { class: 'toolbar', style: 'flex:1' },
          h('div', { class: 'search' }, icon('search'), search),
          chips)),
      tableHost,
      pager)));

  // ---------- data ----------

  function filtered() {
    const term = fold(state.q.trim());
    let list = allAssets();
    if (state.type) list = list.filter(a => a.type === state.type);
    if (term) {
      list = list.filter(a => fold(a.symbol).includes(term) || fold(a.identifier).includes(term) || fold(a.name).includes(term));
    }
    const dir = state.dir === 'asc' ? 1 : -1;
    const key = {
      symbol: a => a.symbol,
      name: a => a.name,
      type: a => a.type,
      created: a => a.createdAt,
    }[state.sort] ?? (a => a.symbol);
    return list.sort((a, b) => dir * String(key(a)).localeCompare(String(key(b)), 'en') || a.id - b.id);
  }

  async function loadLatest(rows) {
    const missing = rows.filter(a => !latest.has(a.id));
    if (!missing.length) return;
    missing.forEach(a => latest.set(a.id, null));
    try {
      const result = await api.prices.latest(missing.map(a => a.identifier));
      for (const p of result) latest.set(p.assetId, { id: p.priceId, value: p.value, date: p.date, nav: p.nav });
      renderTable();
    } catch {
      missing.forEach(a => latest.delete(a.id));
    }
  }

  // ---------- rendering ----------

  function renderChips() {
    const counts = new Map();
    for (const a of allAssets()) counts.set(a.type, (counts.get(a.type) ?? 0) + 1);
    const types = [...counts.keys()].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
    const chip = (type, label, n) => h('button', {
      type: 'button', class: 'chip', 'aria-pressed': String(state.type === type),
      onClick: () => { state.type = type; state.page = 1; update(); },
    }, label, h('span', { class: 'count' }, n.toLocaleString('en-US')));
    chips.replaceChildren(chip('', 'All', allAssets().length), ...types.map(t => chip(t, t || '(none)', counts.get(t))));
  }

  function sortHeader(key, label, cls) {
    const sorted = state.sort === key;
    return h('th', { class: cls, 'aria-sort': sorted ? (state.dir === 'asc' ? 'ascending' : 'descending') : null },
      h('button', {
        type: 'button', class: 'sort',
        onClick: () => {
          state.dir = sorted && state.dir === 'asc' ? 'desc' : 'asc';
          state.sort = key;
          update();
        },
      }, label, icon(sorted ? state.dir : 'sort')));
  }

  function priceCells(asset) {
    const p = latest.get(asset.id);
    if (!p) return [h('td', { class: 'r num faint' }, '—'), h('td', { class: 'hide-sm' })];
    return [
      h('td', { class: 'r num' }, money(p.value)),
      h('td', { class: 'num muted hide-sm' }, dayKey(p.date)),
    ];
  }

  function row(asset) {
    const open = () => navigate(`#/assets/${asset.id}`);
    return h('tr', { dataset: { id: asset.id } },
      h('td', null, h('a', { class: 'sym', href: `#/assets/${asset.id}` }, asset.symbol)),
      h('td', null, h('div', { class: 'name', dir: 'auto', title: asset.name }, asset.name)),
      h('td', { class: 'mono muted hide-sm' }, asset.identifier),
      h('td', { class: 'hide-sm' }, h('span', { class: 'badge' }, asset.type || '—')),
      ...priceCells(asset),
      h('td', { class: 'num muted hide-sm' }, dateTime(asset.createdAt)),
      h('td', { class: 'actions' },
        iconButton('edit', `Edit ${asset.symbol}`, async e => { e.stopPropagation(); await assetDialog(asset); }),
        iconButton('trash', `Delete ${asset.symbol}`, async e => { e.stopPropagation(); await deleteAsset(asset); }, 'danger'),
        iconButton('next', `Open ${asset.symbol}`, open)));
  }

  function renderTable() {
    const list = filtered();
    const pages = Math.max(1, Math.ceil(list.length / PAGE_SIZE));
    state.page = Math.min(state.page, pages);
    pageRows = list.slice((state.page - 1) * PAGE_SIZE, state.page * PAGE_SIZE);

    const total = allAssets().length;
    count.textContent = list.length === total
      ? `${total.toLocaleString('en-US')} assets · prices in ${unitLabel()}`
      : `${list.length.toLocaleString('en-US')} of ${total.toLocaleString('en-US')} assets · prices in ${unitLabel()}`;

    if (!list.length) {
      tableHost.replaceChildren(h('div', { class: 'empty' },
        h('strong', null, total ? 'No matching assets' : 'No assets yet'),
        total ? 'Try a different search or type filter.' : 'Create one, or wait for the price sync jobs to import them.'));
      pager.hidden = true;
      return;
    }

    tableHost.replaceChildren(h('table', null,
      h('thead', null, h('tr', null,
        sortHeader('symbol', 'Symbol'),
        sortHeader('name', 'Name'),
        h('th', { class: 'hide-sm' }, 'Identifier'),
        sortHeader('type', 'Type', 'hide-sm'),
        h('th', { class: 'r' }, 'Last price'),
        h('th', { class: 'hide-sm' }, 'Price date'),
        sortHeader('created', 'Created', 'hide-sm'),
        h('th', null, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', null, pageRows.map(row))));

    pager.hidden = pages <= 1;
    const first = (state.page - 1) * PAGE_SIZE + 1;
    pager.replaceChildren(
      h('span', { class: 'num' }, `${first.toLocaleString('en-US')}–${(first + pageRows.length - 1).toLocaleString('en-US')} of ${list.length.toLocaleString('en-US')}`),
      h('div', { class: 'btns' },
        h('button', { type: 'button', class: 'btn btn-sm', disabled: state.page <= 1, onClick: () => { state.page--; update(true); } }, icon('back'), 'Previous'),
        h('button', { type: 'button', class: 'btn btn-sm', disabled: state.page >= pages, onClick: () => { state.page++; update(true); } }, 'Next', icon('next'))));

    loadLatest(pageRows);
  }

  function syncUrl() {
    const qs = new URLSearchParams();
    if (state.type) qs.set('type', state.type);
    if (state.q) qs.set('q', state.q);
    if (state.sort !== 'symbol') qs.set('sort', state.sort);
    if (state.dir !== 'asc') qs.set('dir', state.dir);
    if (state.page > 1) qs.set('page', state.page);
    const hash = `#/assets${qs.size ? `?${qs}` : ''}`;
    if (location.hash !== hash) history.replaceState(null, '', hash);
  }

  function update(scrollTop = false) {
    renderChips();
    renderTable();
    syncUrl();
    if (scrollTop) main.scrollIntoView({ block: 'start' });
  }

  async function refresh(force = false) {
    try {
      await loadAssets({ force });
      if (force) latest.clear();
      update();
    } catch (err) {
      tableHost.replaceChildren(h('div', { class: 'card-body' }, h('div', { class: 'error-box' }, `Could not load assets: ${err.message}`)));
    }
  }

  async function create() {
    const asset = await assetDialog();
    if (asset) navigate(`#/assets/${asset.id}`);
  }

  // ---------- live updates ----------

  const rerender = batched(update);
  cleanups.push(onAssetsChange(rerender));
  cleanups.push(onUnitChange(rerender));

  const onMessage = e => {
    const { type, data } = e.detail;
    if (type !== 'price.created' && type !== 'price.updated') return;
    const current = latest.get(data.assetId);
    if (current === undefined) return; // not loaded, nothing on screen
    if (current && dayKey(data.date) < dayKey(current.date)) return;
    latest.set(data.assetId, data);

    const tr = tableHost.querySelector(`tr[data-id="${data.assetId}"]`);
    const asset = pageRows.find(a => a.id === data.assetId);
    if (!tr || !asset) return;
    const cells = priceCells(asset);
    tr.children[4].replaceWith(cells[0]);
    tr.children[5].replaceWith(cells[1]);
    cells[0].classList.add('flash');
  };
  realtime.addEventListener('message', onMessage);
  cleanups.push(() => realtime.removeEventListener('message', onMessage));

  refresh();
  return () => cleanups.forEach(fn => fn());
}

function rank(type) {
  const i = TYPE_ORDER.indexOf(type);
  return i < 0 ? TYPE_ORDER.length : i;
}
