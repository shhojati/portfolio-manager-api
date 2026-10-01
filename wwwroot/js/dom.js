// Small DOM helpers. Text from the API is always inserted as text nodes, never as HTML.

/**
 * Creates an element. `props` keys: `class`, `dataset`, `style`, `on<Event>` handlers,
 * boolean/string attributes, or DOM properties such as `value`.
 */
export function h(tag, props, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props ?? {})) {
    if (value == null || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'dataset') Object.assign(el.dataset, value);
    else if (key === 'style') el.style.cssText = value;
    else if (key.startsWith('on')) el.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key in el && typeof value !== 'string') el[key] = value;
    else if (key === 'value') el.value = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const child of children) {
    if (child == null || child === false) continue;
    if (Array.isArray(child)) append(el, child);
    else el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

const ICONS = {
  plus: 'M12 5v14M5 12h14',
  edit: 'M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4',
  trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
  close: 'M6 6l12 12M18 6 6 18',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-4-4',
  back: 'M15 6l-6 6 6 6',
  next: 'M9 6l6 6-6 6',
  pause: 'M8 5v14M16 5v14',
  play: 'M7 5l12 7-12 7z',
  refresh: 'M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7',
  sun: 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10zM12 1v2M12 21v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M1 12h2M21 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4',
  moon: 'M12 3a9 9 0 1 0 9 9 7 7 0 0 1-9-9z',
  sort: 'M8 9l4-4 4 4M8 15l4 4 4-4',
  asc: 'M8 14l4-4 4 4',
  desc: 'M8 10l4 4 4-4',
};

export function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', ICONS[name]);
  svg.append(path);
  return svg;
}

export function iconButton(name, label, onClick, extraClass = '') {
  return h('button', { type: 'button', class: `icon-btn ${extraClass}`, 'aria-label': label, title: label, onClick }, icon(name));
}

// ---------- Toasts ----------

export function toast(message, kind = 'success') {
  const el = h('div', { class: `toast ${kind}` }, message);
  document.getElementById('toasts').append(el);
  setTimeout(() => el.remove(), kind === 'error' ? 6000 : 3500);
}

// ---------- Dialogs ----------

/**
 * Opens a modal form. `onSubmit(form)` may throw to keep the dialog open and show the error.
 * Resolves with onSubmit's result, or undefined if cancelled.
 */
export function formDialog({ title, fields, submitLabel = 'Save', danger = false, onSubmit }) {
  return new Promise(resolve => {
    const error = h('div', { class: 'error-box form-error', hidden: true, role: 'alert' });
    const submit = h('button', { type: 'submit', class: `btn ${danger ? 'btn-danger' : 'btn-primary'}` }, submitLabel);
    const form = h('form', { method: 'dialog', novalidate: true },
      h('div', { class: 'dialog-head' },
        h('h2', null, title),
        iconButton('close', 'Close', () => finish())),
      h('div', { class: 'dialog-body' }, fields, error),
      h('div', { class: 'dialog-foot' },
        h('button', { type: 'button', class: 'btn', onClick: () => finish() }, 'Cancel'),
        submit));
    const dialog = h('dialog', null, form);
    let result;

    form.addEventListener('submit', async e => {
      e.preventDefault();
      if (!form.reportValidity()) return;
      error.hidden = true;
      submit.disabled = true;
      try {
        result = await onSubmit(form);
        finish();
      } catch (err) {
        error.textContent = err.message || 'Something went wrong.';
        error.hidden = false;
      } finally {
        submit.disabled = false;
      }
    });
    // Settle directly rather than waiting for the (asynchronous) close event; it still covers Esc.
    let settled = false;
    function finish() {
      if (settled) return;
      settled = true;
      if (dialog.open) dialog.close();
      dialog.remove();
      resolve(result);
    }
    dialog.addEventListener('close', finish);

    document.body.append(dialog);
    dialog.showModal();
    form.querySelector('input:not([type=hidden])')?.focus();
  });
}

/** Asks for confirmation and runs `action`; resolves true if it ran successfully. */
export async function confirmDialog({ title, message, confirmLabel = 'Delete', action }) {
  const done = await formDialog({
    title,
    fields: h('p', null, message),
    submitLabel: confirmLabel,
    danger: true,
    onSubmit: async () => { await action(); return true; },
  });
  return done === true;
}

export function field({ label, name, type = 'text', value = '', required = false, hint, attrs = {}, list }) {
  const id = `f-${name}-${Math.random().toString(36).slice(2, 7)}`;
  return h('div', { class: 'field' },
    h('label', { for: id }, label, required ? null : h('span', { class: 'faint' }, ' (optional)')),
    h('input', { id, name, type, value, required, list, autocomplete: 'off', ...attrs }),
    hint ? h('div', { class: 'hint' }, hint) : null);
}

/** Coalesces bursts of calls (e.g. a sync job pushing hundreds of prices) into one call per `ms`. */
export function batched(fn, ms = 80) {
  let timer = null;
  return () => {
    if (timer) return;
    timer = setTimeout(() => { timer = null; fn(); }, ms);
  };
}
