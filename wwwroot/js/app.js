// Entry point: hash router, shell controls (connection status, unit, theme) and live connection.

import * as api from './api.js';
import { formDialog, h, icon, toast } from './dom.js';
import { getUnit, onUnitChange, setUnit } from './format.js';
import { realtime } from './realtime.js';
import { loadSession, signOut } from './session.js';
import { loadAssets } from './store.js';
import { renderActivity } from './views/activity.js';
import { renderAsset } from './views/asset.js';
import { renderAssets } from './views/assets.js';
import { passwordField, renderUsers } from './views/users.js';

const routes = [
  { pattern: /^\/assets\/(?<id>\d+)$/, nav: 'assets', render: renderAsset, title: 'Asset' },
  { pattern: /^\/assets$/, nav: 'assets', render: renderAssets, title: 'Assets' },
  { pattern: /^\/activity$/, nav: 'activity', render: renderActivity, title: 'Live activity' },
  { pattern: /^\/users$/, nav: 'users', render: renderUsers, title: 'Users' },
];

const main = document.getElementById('view');
let cleanup = null;
let currentPath = null;

function route() {
  const [path, qs = ''] = location.hash.replace(/^#/, '').split('?');
  const match = routes.map(r => ({ r, m: r.pattern.exec(path) })).find(x => x.m);
  if (!match) {
    location.replace('#/assets');
    return;
  }

  cleanup?.();
  cleanup = match.r.render(main, match.m.groups ?? {}, new URLSearchParams(qs)) ?? null;
  document.title = `${match.r.title} · Portfolio Backoffice`;
  document.querySelectorAll('[data-nav]').forEach(a => a.classList.toggle('active', a.dataset.nav === match.r.nav));

  if (path !== currentPath) {
    window.scrollTo(0, 0);
    main.focus({ preventScroll: true });
  }
  currentPath = path;
}

window.addEventListener('hashchange', route);

// ---------- connection indicator ----------

const conn = document.getElementById('conn');
const CONN_LABELS = { open: 'Live', connecting: 'Connecting…', closed: 'Offline — retrying' };
let wasDisconnected = false;

realtime.addEventListener('status', e => {
  conn.dataset.state = e.detail;
  conn.querySelector('.conn-label').textContent = CONN_LABELS[e.detail];
  if (e.detail === 'closed') wasDisconnected = true;
  // After a reconnect, events may have been missed: reload the asset cache and the current view.
  if (e.detail === 'open' && wasDisconnected) {
    wasDisconnected = false;
    loadAssets({ force: true }).then(route, () => {});
  }
});

// ---------- unit toggle ----------

const unitButtons = document.querySelectorAll('[data-unit]');
const syncUnitButtons = () => unitButtons.forEach(b => b.setAttribute('aria-pressed', String(b.dataset.unit === getUnit())));
unitButtons.forEach(b => b.addEventListener('click', () => setUnit(b.dataset.unit)));
onUnitChange(syncUnitButtons);
syncUnitButtons();

// ---------- theme toggle ----------

const themeBtn = document.getElementById('theme-toggle');
const darkQuery = matchMedia('(prefers-color-scheme: dark)');
const isDark = () => (document.documentElement.dataset.theme ?? (darkQuery.matches ? 'dark' : 'light')) === 'dark';
const syncThemeButton = () => themeBtn.replaceChildren(icon(isDark() ? 'sun' : 'moon'));

themeBtn.addEventListener('click', () => {
  const next = isDark() ? 'light' : 'dark';
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem('pm.theme', next); } catch {}
  syncThemeButton();
});
darkQuery.addEventListener('change', syncThemeButton);
syncThemeButton();

// ---------- signed-in user ----------

function showUser(user) {
  document.getElementById('user-avatar').textContent = user.username.slice(0, 2);
  document.getElementById('user-name').textContent = user.username;
  document.getElementById('user-box').hidden = false;
}

const passwordBtn = document.getElementById('change-password');
passwordBtn.append(icon('key'));
passwordBtn.addEventListener('click', async () => {
  const val = (form, name) => form.elements.namedItem(name).value;
  const done = await formDialog({
    title: 'Change your password',
    submitLabel: 'Change password',
    fields: [
      passwordField({ label: 'Current password', name: 'current', autocomplete: 'current-password', generate: false }),
      passwordField({ label: 'New password', name: 'next' }),
      h('p', null, 'Your other sessions will be signed out.'),
    ],
    onSubmit: async form => { await api.auth.changePassword(val(form, 'current'), val(form, 'next')); return true; },
  });
  if (done) toast('Password changed');
});

const signOutBtn = document.getElementById('sign-out');
signOutBtn.append(icon('logout'));
signOutBtn.addEventListener('click', signOut);

// ---------- start ----------

// The server only serves this page to signed-in admins, but the session can lapse in between.
loadSession().then(
  user => {
    if (user.role !== 'Admin') return signOut();
    showUser(user);
    realtime.connect();
    route();
  },
  () => location.replace(`login.html?next=${encodeURIComponent(location.hash)}`));
