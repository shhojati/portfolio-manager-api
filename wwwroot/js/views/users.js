// Users: who can sign in to the backoffice (Admin) or call the API (ApiClient).

import * as api from '../api.js';
import { confirmDialog, field, formDialog, h, icon, iconButton, toast } from '../dom.js';
import { dateTime } from '../format.js';
import { currentUser, generatePassword } from '../session.js';

const MIN_PASSWORD = 10;
const ROLES = [
  ['Admin', 'Admin — signs in to the backoffice, full access'],
  ['ApiClient', 'API client — bearer token, read-only'],
];

const val = (form, name) => form.elements.namedItem(name).value;

/** A password input with a "Generate" button that fills in (and reveals) a random password. */
export function passwordField({ label, name, autocomplete = 'new-password', generate = true }) {
  const id = `f-${name}-${Math.random().toString(36).slice(2, 7)}`;
  const input = h('input', { id, name, type: 'password', required: true, autocomplete, minlength: MIN_PASSWORD, maxlength: 128 });
  return h('div', { class: 'field' },
    h('label', { for: id }, label),
    h('div', { class: 'input-row' },
      input,
      generate ? h('button', {
        type: 'button', class: 'btn', title: 'Generate a random password',
        onClick: () => { input.type = 'text'; input.value = generatePassword(); input.select(); },
      }, icon('dice'), 'Generate') : null),
    generate ? h('div', { class: 'hint' }, `At least ${MIN_PASSWORD} characters. Copy it now; it can't be shown again.`) : null);
}

export function renderUsers(main) {
  const tableHost = h('div', { class: 'table-wrap' }, h('div', { class: 'skeleton' }, 'Loading users…'));
  let list = [];

  const origin = location.origin;
  main.replaceChildren(h('div', { class: 'page' },
    h('div', { class: 'page-head' },
      h('div', null,
        h('h1', null, 'Users'),
        h('p', { class: 'sub' }, 'Admins sign in here; API clients call the API with a bearer token.')),
      h('div', { class: 'page-actions' },
        h('button', { type: 'button', class: 'btn btn-primary', onClick: create }, icon('plus'), 'New user'))),
    h('div', { class: 'card' }, tableHost),
    h('div', { class: 'card' },
      h('div', { class: 'card-head' }, h('h2', null, 'Calling the API')),
      h('div', { class: 'card-body help' },
        h('p', null, 'Exchange an API client’s username and password for a token, then send it with every request:'),
        h('pre', { class: 'code' },
          `curl -X POST ${origin}/api/auth/token \\\n  -H "Content-Type: application/json" \\\n  -d '{"username":"api-client","password":"…"}'\n\n`
          + `curl "${origin}/api/prices/latest?identifiers=IRO1FOLD0001" \\\n  -H "Authorization: Bearer <accessToken>"`),
        h('p', null,
          'Access tokens last an hour; exchange the ', h('code', null, 'refreshToken'), ' at ', h('code', null, 'POST /api/auth/refresh'),
          ' for a new one. For the live feed, connect to ', h('code', null, '/ws?access_token=<accessToken>'),
          '. Requests are rate limited per user; over the limit the API answers 429 with a Retry-After header.')))));

  function render() {
    const me = currentUser();
    if (!list.length) {
      tableHost.replaceChildren(h('div', { class: 'empty' }, h('strong', null, 'No users')));
      return;
    }
    tableHost.replaceChildren(h('table', null,
      h('thead', null, h('tr', null,
        h('th', null, 'Username'), h('th', null, 'Role'),
        h('th', { class: 'hide-sm' }, 'Created'), h('th', null, 'Last sign-in'),
        h('th', null, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', null, list.map(u => h('tr', null,
        h('td', null, h('span', { class: 'sym' }, u.username), u.id === me?.id ? h('span', { class: 'faint' }, ' (you)') : null),
        h('td', null, h('span', { class: `badge ${u.role === 'Admin' ? 'ev-asset' : ''}` }, u.role === 'ApiClient' ? 'API client' : u.role)),
        h('td', { class: 'num muted hide-sm' }, dateTime(u.createdAt)),
        h('td', { class: 'num muted' }, u.lastLoginAt ? dateTime(u.lastLoginAt) : 'Never'),
        h('td', { class: 'actions' },
          iconButton('key', `Set password for ${u.username}`, () => resetPassword(u)),
          u.id === me?.id ? null : iconButton('trash', `Delete ${u.username}`, () => remove(u), 'danger')))))));
  }

  async function load() {
    try {
      list = await api.users.list();
      render();
    } catch (err) {
      tableHost.replaceChildren(h('div', { class: 'card-body' }, h('div', { class: 'error-box' }, `Could not load users: ${err.message}`)));
    }
  }

  async function create() {
    const roleId = `f-role-${Math.random().toString(36).slice(2, 7)}`;
    const user = await formDialog({
      title: 'New user',
      submitLabel: 'Create user',
      fields: [
        field({ label: 'Username', name: 'username', required: true, hint: 'Letters, digits, “.”, “_” and “-”.',
          attrs: { maxlength: 50, pattern: '[A-Za-z0-9._\\-]+', autocapitalize: 'none', spellcheck: 'false' } }),
        h('div', { class: 'field' },
          h('label', { for: roleId }, 'Role'),
          h('select', { id: roleId, name: 'role', required: true },
            ROLES.map(([value, label]) => h('option', { value }, label)))),
        passwordField({ label: 'Password', name: 'password' }),
      ],
      onSubmit: form => api.users.create({
        username: val(form, 'username').trim(),
        role: val(form, 'role'),
        password: val(form, 'password'),
      }),
    });
    if (user) {
      toast(`${user.username} created`);
      load();
    }
  }

  async function resetPassword(user) {
    const self = user.id === currentUser()?.id;
    const done = await formDialog({
      title: `Set password for ${user.username}`,
      submitLabel: 'Set password',
      fields: [
        h('p', null, self
          ? 'You will stay signed in here; your other sessions are signed out.'
          : 'Their current sessions and refresh tokens stop working.'),
        passwordField({ label: 'New password', name: 'password' }),
      ],
      onSubmit: async form => { await api.users.setPassword(user.id, val(form, 'password')); return true; },
    });
    if (done) {
      toast(`Password set for ${user.username}`);
      // Our own stamp changed, so this session is no longer valid: sign in again.
      if (self) location.replace('login.html');
    }
  }

  async function remove(user) {
    const ok = await confirmDialog({
      title: `Delete ${user.username}?`,
      message: user.role === 'ApiClient'
        ? 'Clients using this account can no longer get tokens. Tokens already issued keep working until they expire (up to an hour).'
        : 'They will be signed out and can no longer sign in.',
      confirmLabel: 'Delete user',
      action: () => api.users.remove(user.id),
    });
    if (ok) {
      toast(`${user.username} deleted`);
      load();
    }
  }

  load();
}
