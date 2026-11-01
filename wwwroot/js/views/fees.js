// Fees: exchange fee rates by kind of asset, each from the day it took effect. The mobile app figures a trade's fee
// from the rate in effect on the trade's own day, so a new rate from some day on leaves earlier trades' fees alone.

import * as api from '../api.js';
import { confirmDialog, field, formDialog, h, iconButton, toast } from '../dom.js';
import { jalali, todayKey } from '../format.js';

const val = (form, name) => form.elements.namedItem(name).value;
const pct = n => `${Number(n).toLocaleString('en-US', { maximumFractionDigits: 4 })}%`;

export function renderFees(main) {
  const host = h('div', { style: 'display: flex; flex-direction: column; gap: 20px' },
    h('div', { class: 'card' }, h('div', { class: 'skeleton' }, 'Loading fee rates…')));
  let classes = [];

  main.replaceChildren(h('div', { class: 'page' },
    h('div', { class: 'page-head' },
      h('div', null,
        h('h1', null, 'Fees'),
        h('p', { class: 'sub' }, 'What brokers charge on a trade, as a percent of its value. A trade is charged the rate in effect on its day.'))),
    host,
    h('div', { class: 'card' },
      h('div', { class: 'card-head' }, h('h2', null, 'How rates apply')),
      h('div', { class: 'card-body help' },
        h('p', null, 'Each rate applies from its day until the next rate of the same kind. When fees change, add a rate from the day they change rather than editing the current one, so trades from before keep the old rate.'),
        h('p', null, 'Sell rates include the 0.5% tax on selling shares. Gold, coins, currency and crypto bought outside the exchange, issue-and-redeem funds, futures and users’ own assets have no exchange fee.'),
        h('p', null, 'The app fills a trade’s fee in from these; the user can still type another.')))));

  function render() {
    host.replaceChildren(...classes.map(c => h('div', { class: 'card' },
      h('div', { class: 'card-head' },
        h('h2', { dir: 'auto' }, c.label, h('span', { class: 'faint' }, ` · ${c.key}`)),
        h('button', { type: 'button', class: 'btn', onClick: () => edit(c) }, 'New rate')),
      c.rates.length
        ? h('div', { class: 'table-wrap' }, h('table', null,
          h('thead', null, h('tr', null,
            h('th', null, 'From'), h('th', null, 'Until'), h('th', { class: 'num' }, 'Buy'), h('th', { class: 'num' }, 'Sell'),
            h('th', null, h('span', { class: 'visually-hidden' }, 'Actions')))),
          h('tbody', null, c.rates.map((r, i) => {
            const next = c.rates[i + 1];
            return h('tr', null,
              h('td', { class: 'num' }, r.effectiveFrom, h('span', { class: 'faint' }, ` · ${jalali(r.effectiveFrom)}`)),
              h('td', { class: 'num muted' }, next ? next.effectiveFrom : 'Now'),
              h('td', { class: 'num' }, pct(r.buyPercent)),
              h('td', { class: 'num' }, pct(r.sellPercent)),
              h('td', { class: 'actions' },
                iconButton('edit', `Edit the rate from ${r.effectiveFrom}`, () => edit(c, r)),
                iconButton('trash', `Delete the rate from ${r.effectiveFrom}`, () => remove(c, r), 'danger')));
          }))))
        : h('div', { class: 'empty' }, h('strong', null, 'No rates'), 'Trades of this kind get no fee filled in.'))));
  }

  async function load() {
    try {
      classes = await api.fees.list();
      render();
    } catch (err) {
      host.replaceChildren(h('div', { class: 'card' }, h('div', { class: 'card-body' },
        h('div', { class: 'error-box' }, `Could not load fee rates: ${err.message}`))));
    }
  }

  async function edit(feeClass, rate) {
    const latest = feeClass.rates.at(-1);
    const saved = await formDialog({
      title: rate ? `Edit ${feeClass.key} from ${rate.effectiveFrom}` : `New ${feeClass.key} rate`,
      submitLabel: rate ? 'Save changes' : 'Add rate',
      fields: [
        field({ label: 'From', name: 'from', type: 'date', value: rate?.effectiveFrom ?? todayKey(), required: true,
          hint: 'Trades on this day and after, until the next rate, are charged this rate.' }),
        h('div', { class: 'field-row' },
          field({ label: 'Buy (%)', name: 'buy', type: 'number', value: String(rate?.buyPercent ?? latest?.buyPercent ?? ''), required: true,
            attrs: { min: 0, max: 99.9999, step: 'any' } }),
          field({ label: 'Sell (%)', name: 'sell', type: 'number', value: String(rate?.sellPercent ?? latest?.sellPercent ?? ''), required: true,
            attrs: { min: 0, max: 99.9999, step: 'any' }, hint: 'With the sell tax.' })),
      ],
      onSubmit: form => {
        const body = {
          class: feeClass.key,
          effectiveFrom: val(form, 'from'),
          buyPercent: Number(val(form, 'buy')),
          sellPercent: Number(val(form, 'sell')),
        };
        return rate ? api.fees.update(rate.id, body) : api.fees.create(body);
      },
    });
    if (saved) {
      toast(rate ? 'Rate updated' : 'Rate added');
      load();
    }
  }

  async function remove(feeClass, rate) {
    const ok = await confirmDialog({
      title: `Delete the ${feeClass.key} rate from ${rate.effectiveFrom}?`,
      message: 'Trades from that day are charged the rate before it instead, or none if it was the first.',
      confirmLabel: 'Delete rate',
      action: () => api.fees.remove(rate.id),
    });
    if (ok) {
      toast('Rate deleted');
      load();
    }
  }

  load();
}
