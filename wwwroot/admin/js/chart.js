// Dependency-free SVG line chart with a snapping crosshair and tooltip.
// series: [{ name, color (CSS value), points: [{ t: epoch ms, v: number }] sorted by t }]

import { h } from './dom.js';

const NS = 'http://www.w3.org/2000/svg';
const M = { top: 12, right: 12, bottom: 28, left: 64 };

function s(tag, attrs = {}) {
  const el = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
  return el;
}

function niceTicks(min, max, count) {
  const step0 = (max - min) / count;
  const mag = 10 ** Math.floor(Math.log10(step0));
  const err = step0 / mag;
  const step = (err >= 7.5 ? 10 : err >= 3.5 ? 5 : err >= 1.5 ? 2 : 1) * mag;
  // Ticks cover the data range, so the domain ends on round numbers.
  const ticks = [];
  for (let v = Math.floor(min / step) * step; v < max + step * 0.999; v += step) ticks.push(Number(v.toPrecision(12)));
  return ticks;
}

const shortDay = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' });
const monthYear = new Intl.DateTimeFormat('en-US', { month: 'short', year: '2-digit', timeZone: 'UTC' });

export function lineChart(container, { series, formatValue, formatAxis, formatDate, label }) {
  const root = h('div', { class: 'chart', tabindex: 0, role: 'img', 'aria-label': label });
  const tooltip = h('div', { class: 'tooltip', hidden: true });
  const svg = s('svg');
  root.append(svg, tooltip);
  container.replaceChildren(root);

  // Every distinct date across series, for snapping the crosshair.
  const times = [...new Set(series.flatMap(sr => sr.points.map(p => p.t)))].sort((a, b) => a - b);
  const lookup = series.map(sr => new Map(sr.points.map(p => [p.t, p.v])));
  let geom = null;
  let active = -1;

  function draw() {
    const width = root.clientWidth;
    const height = root.clientHeight;
    if (!width || !height) return;
    svg.replaceChildren();
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);

    const values = series.flatMap(sr => sr.points.map(p => p.v));
    let lo = Math.min(...values);
    let hi = Math.max(...values);
    if (lo === hi) { const pad = Math.abs(lo) * 0.01 || 1; lo -= pad; hi += pad; }
    const yTicks = niceTicks(lo, hi, Math.max(2, Math.round((height - M.top - M.bottom) / 56)));
    lo = yTicks[0];
    hi = yTicks[yTicks.length - 1];

    const t0 = times[0];
    const t1 = times[times.length - 1];
    const iw = width - M.left - M.right;
    const ih = height - M.top - M.bottom;
    const x = t => M.left + (t1 === t0 ? iw / 2 : ((t - t0) / (t1 - t0)) * iw);
    const y = v => M.top + ih - ((v - lo) / (hi - lo)) * ih;
    geom = { x, y, iw, ih, width };

    const grid = s('g', { class: 'grid' });
    const axis = s('g', { class: 'axis' });
    for (const v of yTicks) {
      grid.append(s('line', { x1: M.left, x2: width - M.right, y1: y(v), y2: y(v) }));
      const text = s('text', { x: M.left - 8, y: y(v), 'text-anchor': 'end', 'dominant-baseline': 'middle' });
      text.textContent = formatAxis(v);
      axis.append(text);
    }

    // X labels: the data dates themselves when few, otherwise evenly spaced in time; never repeated.
    const xCount = Math.max(2, Math.floor(iw / 96));
    const fmt = t1 - t0 > 300 * 864e5 ? monthYear : shortDay;
    const xTicks = times.length <= xCount
      ? times
      : Array.from({ length: xCount }, (_, i) => t0 + ((t1 - t0) * i) / (xCount - 1));
    let lastLabel = null;
    xTicks.forEach((t, i) => {
      const label = fmt.format(new Date(t));
      if (label === lastLabel) return;
      lastLabel = label;
      const anchor = xTicks.length === 1 ? 'middle' : i === 0 ? 'start' : i === xTicks.length - 1 ? 'end' : 'middle';
      const text = s('text', { x: x(t), y: height - 8, 'text-anchor': anchor });
      text.textContent = label;
      axis.append(text);
    });
    svg.append(grid, axis);

    series.forEach((sr, i) => {
      if (!sr.points.length) return;
      const d = sr.points.map((p, j) => `${j ? 'L' : 'M'}${x(p.t).toFixed(1)},${y(p.v).toFixed(1)}`).join('');
      if (i === 0 && sr.points.length > 1) {
        const first = sr.points[0];
        const last = sr.points[sr.points.length - 1];
        svg.append(s('path', { class: 'area', style: `fill:${sr.color}`,
          d: `${d}L${x(last.t).toFixed(1)},${M.top + ih}L${x(first.t).toFixed(1)},${M.top + ih}Z` }));
      }
      svg.append(s('path', { class: 'line', style: `stroke:${sr.color}`, d }));
      if (sr.points.length === 1) {
        svg.append(s('circle', { class: 'dot', cx: x(sr.points[0].t), cy: y(sr.points[0].v), r: 4, style: `fill:${sr.color}` }));
      }
    });

    svg.append(s('g', { class: 'hover' }));
    if (active >= 0) show(active);
  }

  function show(index) {
    active = index;
    const hover = svg.querySelector('.hover');
    if (!geom || !hover) return;
    hover.replaceChildren();

    const t = times[index];
    const cx = geom.x(t);
    hover.append(s('line', { class: 'crosshair', x1: cx, x2: cx, y1: M.top, y2: M.top + geom.ih }));

    const rows = [];
    series.forEach((sr, i) => {
      const v = lookup[i].get(t);
      if (v == null) return;
      hover.append(s('circle', { class: 'dot', cx, cy: geom.y(v), r: 4.5, style: `fill:${sr.color}` }));
      rows.push(h('div', { class: 't-row' },
        h('i', { style: `background:${sr.color}` }),
        h('strong', null, formatValue(v)),
        h('span', null, sr.name)));
    });

    tooltip.replaceChildren(h('div', { class: 't-date' }, formatDate(t)), ...rows);
    tooltip.hidden = false;
    const tw = tooltip.offsetWidth;
    const left = cx + 14 + tw > geom.width ? cx - 14 - tw : cx + 14;
    tooltip.style.left = `${Math.max(0, left)}px`;
    tooltip.style.top = `${M.top}px`;
  }

  function hide() {
    active = -1;
    tooltip.hidden = true;
    svg.querySelector('.hover')?.replaceChildren();
  }

  function nearest(clientX) {
    const rect = svg.getBoundingClientRect();
    const px = clientX - rect.left;
    let best = 0;
    let bestDist = Infinity;
    // Binary search would be faster, but a few thousand points is cheap enough here.
    times.forEach((t, i) => {
      const d = Math.abs(geom.x(t) - px);
      if (d < bestDist) { bestDist = d; best = i; }
    });
    return best;
  }

  root.addEventListener('pointermove', e => { if (geom && times.length) show(nearest(e.clientX)); });
  root.addEventListener('pointerleave', hide);
  root.addEventListener('blur', hide);
  root.addEventListener('keydown', e => {
    if (!times.length) return;
    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      const start = active < 0 ? times.length - 1 : active + (e.key === 'ArrowLeft' ? -1 : 1);
      show(Math.min(times.length - 1, Math.max(0, start)));
    } else if (e.key === 'Escape') hide();
  });

  draw();
  const observer = new ResizeObserver(draw);
  observer.observe(root);
  return { destroy: () => observer.disconnect() };
}
