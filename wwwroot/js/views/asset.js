// Asset detail: headline stats, price/NAV chart, and the editable price history (live).

import * as api from '../api.js';
import { lineChart } from '../chart.js';
import { h, icon, iconButton, batched } from '../dom.js';
import { dateTime, dayKey, dayToTime, jalali, longDay, money, moneyCompact, onUnitChange, percent, unitLabel } from '../format.js';
import { realtime } from '../realtime.js';
import { assetById, loadAssets, onAssetsChange } from '../store.js';
import { assetDialog, deleteAsset, deletePrice, priceDialog } from './forms.js';

const PAGE_SIZE = 25;
const RANGES = [['1M', 30], ['3M', 91], ['6M', 182], ['1Y', 365], ['All', Infinity]];

const byDateDesc = (a, b) => dayKey(b.date).localeCompare(dayKey(a.date)) || b.id - a.id;

export function renderAsset(main, params) {
  const id = Number(params.id);
  const cleanups = [];
  let asset = assetById(id) ?? null;
  let prices = [];
  let range = '3M';
  let page = 1;
  let chart = null;

  main.replaceChildren(h('div', { class: 'page' }, h('div', { class: 'skeleton' }, 'Loading asset…')));

  // ---------- derived ----------

  // One row per day (the latest inserted wins), newest first.
  function daily() {
    const seen = new Set();
    return prices.filter(p => {
      const key = dayKey(p.date);
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  }

  // ---------- rendering ----------

  function render() {
    if (!asset) return;
    const days = daily();
    const [last, prev] = days;
    const change = last && prev ? last.value - prev.value : null;
    const changePct = change != null && prev.value ? change / prev.value : null;
    const hasNav = prices.some(p => p.nav != null);

    const stats = [
      stat('Last price', last ? money(last.value) : '—', last ? unitLabel() : null,
        last ? [dayKey(last.date), ' · ', jalali(dayKey(last.date))] : 'No prices yet'),
      stat('Change', change == null ? '—' : deltaText(change, money), null,
        changePct == null ? 'vs. previous day' : [deltaEl(changePct), ' vs. ', dayKey(prev.date)]),
    ];
    if (hasNav) {
      const navRow = days.find(p => p.nav != null);
      const premium = navRow && navRow.nav ? navRow.value / navRow.nav - 1 : null;
      stats.push(
        stat('NAV', navRow ? money(navRow.nav) : '—', navRow ? unitLabel() : null, navRow ? dayKey(navRow.date) : ''),
        stat('Premium to NAV', premium == null ? '—' : percent(premium), null, 'price ÷ NAV − 1'));
    }
    stats.push(stat('Price records', prices.length.toLocaleString('en-US'), null,
      days.length ? `since ${dayKey(days[days.length - 1].date)}` : ''));

    const chartHost = h('div', { class: 'card-body' });
    const tableCard = h('div', { class: 'card' });

    main.replaceChildren(h('div', { class: 'page' },
      h('div', { class: 'page-head' },
        h('div', null,
          h('div', { class: 'crumbs' }, h('a', { href: '#/assets' }, 'Assets'), '/', h('span', null, asset.symbol)),
          h('h1', { class: 'asset-title' },
            asset.symbol,
            h('span', { class: 'badge' }, asset.type || '—'),
            h('span', { class: 'name-fa', dir: 'auto' }, asset.name)),
          h('p', { class: 'sub' },
            h('span', { class: 'mono' }, asset.identifier), ' · created ', dateTime(asset.createdAt))),
        h('div', { class: 'page-actions' },
          h('button', { type: 'button', class: 'btn', onClick: () => assetDialog(asset) }, icon('edit'), 'Edit'),
          h('button', { type: 'button', class: 'btn', onClick: remove }, icon('trash'), 'Delete'),
          h('button', { type: 'button', class: 'btn btn-primary', onClick: addPrice }, icon('plus'), 'Add price'))),
      h('div', { class: 'card stats' }, stats),
      h('div', { class: 'card' },
        h('div', { class: 'card-head' },
          h('h2', null, `Price history (${unitLabel()})`),
          h('div', { class: 'toolbar' },
            hasNav ? h('div', { class: 'legend' },
              h('span', null, h('i', { style: 'background:var(--series-1)' }), 'Price'),
              h('span', null, h('i', { style: 'background:var(--series-2)' }), 'NAV')) : null,
            h('div', { class: 'segmented', role: 'group', 'aria-label': 'Chart range' },
              RANGES.map(([label]) => h('button', {
                type: 'button', 'aria-pressed': String(range === label),
                onClick: () => { range = label; render(); },
              }, label))))),
        chartHost),
      tableCard));

    renderChart(chartHost, days, hasNav);
    renderTable(tableCard, days);
  }

  function renderChart(host, days, hasNav) {
    chart?.destroy();
    chart = null;
    const span = RANGES.find(([label]) => label === range)[1];
    const newest = days.length ? dayToTime(dayKey(days[0].date)) : 0;
    const inRange = days.filter(p => newest - dayToTime(dayKey(p.date)) <= span * 864e5).reverse();

    if (!inRange.length) {
      host.replaceChildren(h('div', { class: 'empty' },
        h('strong', null, 'No prices yet'), 'Prices appear here as the sync jobs import them, or add one manually.'));
      return;
    }

    const series = [{ name: 'Price', color: 'var(--series-1)', points: inRange.map(p => ({ t: dayToTime(dayKey(p.date)), v: p.value })) }];
    if (hasNav) {
      series.push({ name: 'NAV', color: 'var(--series-2)',
        points: inRange.filter(p => p.nav != null).map(p => ({ t: dayToTime(dayKey(p.date)), v: p.nav })) });
    }
    chart = lineChart(host, {
      series,
      label: `${asset.symbol} price history, ${range}. Use the arrow keys to read values.`,
      formatValue: money,
      formatAxis: moneyCompact,
      formatDate: t => {
        const key = new Date(t).toISOString().slice(0, 10);
        return `${longDay(key)} · ${jalali(key)}`;
      },
    });
  }

  function renderTable(card, days) {
    const pages = Math.max(1, Math.ceil(prices.length / PAGE_SIZE));
    page = Math.min(page, pages);
    const rows = prices.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
    // Previous day's value for each day, to show the daily change.
    const prevByDay = new Map(days.map((p, i) => [dayKey(p.date), days[i + 1]?.value]));

    const head = h('div', { class: 'card-head' },
      h('h2', null, 'Prices'),
      h('span', { class: 'muted' }, `${prices.length.toLocaleString('en-US')} records · newest first`));

    if (!prices.length) {
      card.replaceChildren(head, h('div', { class: 'empty' }, h('strong', null, 'No prices recorded'),
        h('button', { type: 'button', class: 'btn btn-primary', style: 'margin-top:12px', onClick: addPrice }, icon('plus'), 'Add price')));
      return;
    }

    const body = rows.map(p => {
      const key = dayKey(p.date);
      const prevValue = prevByDay.get(key);
      const pct = prevValue ? p.value / prevValue - 1 : null;
      return h('tr', { dataset: { id: p.id } },
        h('td', { class: 'num' }, key),
        h('td', { class: 'num muted' }, jalali(key)),
        h('td', { class: 'r num' }, money(p.value)),
        h('td', { class: 'r num' }, pct == null ? h('span', { class: 'faint' }, '—') : deltaEl(pct)),
        h('td', { class: 'r num muted' }, p.nav == null ? '—' : money(p.nav)),
        h('td', { class: 'actions' },
          iconButton('edit', `Edit price for ${key}`, () => editPrice(p)),
          iconButton('trash', `Delete price for ${key}`, () => removePrice(p), 'danger')));
    });

    const pager = pages > 1 ? h('div', { class: 'pager' },
      h('span', null, `Page ${page} of ${pages}`),
      h('div', { class: 'btns' },
        h('button', { type: 'button', class: 'btn btn-sm', disabled: page <= 1, onClick: () => { page--; renderTable(card, daily()); } }, icon('back'), 'Newer'),
        h('button', { type: 'button', class: 'btn btn-sm', disabled: page >= pages, onClick: () => { page++; renderTable(card, daily()); } }, 'Older', icon('next')))) : null;

    card.replaceChildren(head,
      h('div', { class: 'table-wrap' }, h('table', null,
        h('thead', null, h('tr', null,
          h('th', null, 'Date'), h('th', null, 'Jalali'), h('th', { class: 'r' }, 'Price'),
          h('th', { class: 'r' }, 'Day change'), h('th', { class: 'r' }, 'NAV'),
          h('th', null, h('span', { class: 'visually-hidden' }, 'Actions')))),
        h('tbody', null, body))),
      pager);
  }

  // ---------- actions ----------

  async function addPrice() {
    const saved = await priceDialog(asset);
    if (saved) upsertPrice(saved);
  }

  async function editPrice(price) {
    const saved = await priceDialog(asset, price);
    if (saved) upsertPrice(saved);
  }

  async function removePrice(price) {
    if (await deletePrice(asset, price, dayKey(price.date))) {
      prices = prices.filter(p => p.id !== price.id);
      render();
    }
  }

  async function remove() {
    if (await deleteAsset(asset)) location.hash = '#/assets';
  }

  function upsertPrice(price) {
    prices = [price, ...prices.filter(p => p.id !== price.id)].sort(byDateDesc);
    rerender();
  }

  // ---------- load & live updates ----------

  const rerender = batched(render);

  async function load() {
    try {
      const [a, list] = await Promise.all([api.assets.get(id), api.assets.prices(id)]);
      asset = a;
      prices = list.sort(byDateDesc);
      render();
      loadAssets().catch(() => {}); // warm the shared cache for the type list in dialogs
    } catch (err) {
      main.replaceChildren(h('div', { class: 'page' },
        h('div', { class: 'crumbs' }, h('a', { href: '#/assets' }, 'Assets')),
        h('div', { class: 'error-box' }, err.status === 404 ? 'This asset does not exist (it may have been deleted).' : `Could not load asset: ${err.message}`)));
    }
  }

  const onMessage = e => {
    const { type, data } = e.detail;
    if ((type === 'price.created' || type === 'price.updated') && data.assetId === id && asset) upsertPrice(data);
  };
  realtime.addEventListener('message', onMessage);
  cleanups.push(() => realtime.removeEventListener('message', onMessage));
  cleanups.push(onUnitChange(rerender));
  cleanups.push(onAssetsChange(() => {
    const fresh = assetById(id);
    if (fresh && asset && fresh !== asset) { asset = fresh; rerender(); }
  }));
  cleanups.push(() => chart?.destroy());

  load();
  return () => cleanups.forEach(fn => fn());
}

function stat(label, value, unit, meta) {
  return h('div', { class: 'stat' },
    h('div', { class: 'label' }, label),
    h('div', { class: 'value num' }, value, unit ? h('small', null, unit) : null),
    meta ? h('div', { class: 'meta' }, meta) : null);
}

function deltaText(change, fmt) {
  const sign = change > 0 ? '+' : change < 0 ? '−' : '';
  return `${sign}${fmt(Math.abs(change))}`;
}

function deltaEl(fraction) {
  const dir = fraction > 0 ? 'up' : fraction < 0 ? 'down' : '';
  const arrow = fraction > 0 ? '▲ ' : fraction < 0 ? '▼ ' : '';
  return h('span', { class: `delta ${dir}` }, arrow, percent(fraction));
}
