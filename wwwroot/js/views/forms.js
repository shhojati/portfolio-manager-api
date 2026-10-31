// Create/edit/delete dialogs for assets and prices, shared by the list and detail views.

import * as api from '../api.js';
import { confirmDialog, field, formDialog, h, toast } from '../dom.js';
import { dayKey, getUnit, todayKey, unitLabel } from '../format.js';
import { KNOWN_TYPES, allAssets, removeAsset, upsertAsset } from '../store.js';

function typeOptions() {
  const types = new Set([...KNOWN_TYPES, ...allAssets().map(a => a.type).filter(Boolean)]);
  return h('datalist', { id: 'asset-types' }, [...types].sort().map(t => h('option', { value: t })));
}

/** Opens the asset form; resolves with the saved asset, or undefined if cancelled. */
export function assetDialog(asset) {
  const editing = Boolean(asset);
  return formDialog({
    title: editing ? `Edit ${asset.symbol}` : 'New asset',
    submitLabel: editing ? 'Save changes' : 'Create asset',
    fields: [
      h('div', { class: 'field-row' },
        field({ label: 'Symbol', name: 'symbol', value: asset?.symbol, required: true, attrs: { maxlength: 20 } }),
        field({ label: 'Type', name: 'type', value: asset?.type, required: true, list: 'asset-types', attrs: { maxlength: 50 } })),
      typeOptions(),
      field({ label: 'Identifier', name: 'identifier', value: asset?.identifier, required: true,
        hint: 'ISIN or another unique code. Symbol and identifier are stored in upper case.', attrs: { maxlength: 100 } }),
      field({ label: 'Name', name: 'name', value: asset?.name, required: true, attrs: { maxlength: 100, dir: 'auto' } }),
    ],
    async onSubmit(form) {
      const body = {
        symbol: val(form, 'symbol').trim(),
        identifier: val(form, 'identifier').trim(),
        name: val(form, 'name').trim(),
        type: val(form, 'type').trim(),
      };
      let saved;
      if (editing) {
        await api.assets.update(asset.id, body);
        saved = { ...asset, ...body, symbol: body.symbol.toUpperCase(), identifier: body.identifier.toUpperCase() };
      } else {
        saved = await api.assets.create(body);
      }
      upsertAsset(saved);
      toast(editing ? `${saved.symbol} updated` : `${saved.symbol} created`);
      return saved;
    },
  });
}

export async function deleteAsset(asset) {
  const ok = await confirmDialog({
    title: `Delete ${asset.symbol}?`,
    message: `This permanently deletes “${asset.name}” and all of its price history.`,
    confirmLabel: 'Delete asset',
    action: () => api.assets.remove(asset.id),
  });
  if (ok) {
    removeAsset(asset.id);
    toast(`${asset.symbol} deleted`);
  }
  return ok;
}

/** Opens the price form; values are entered in the current display unit and stored in Rial. */
export function priceDialog(asset, price) {
  const editing = Boolean(price);
  const factor = getUnit() === 'IRT' ? 10 : 1;
  const shown = v => (v == null ? '' : String(Number(v) / factor));
  const unit = unitLabel();

  return formDialog({
    title: editing ? `Edit ${asset.symbol} price` : `Add ${asset.symbol} price`,
    submitLabel: editing ? 'Save changes' : 'Add price',
    fields: [
      field({ label: 'Date', name: 'date', type: 'date', required: true, value: price ? dayKey(price.date) : todayKey() }),
      h('div', { class: 'field-row' },
        field({ label: `Price (${unit})`, name: 'value', type: 'number', required: true, value: shown(price?.value),
          attrs: { min: 0, step: 'any', inputmode: 'decimal' } }),
        field({ label: `NAV (${unit})`, name: 'nav', type: 'number', value: shown(price?.nav),
          hint: 'Net asset value per unit, for funds.', attrs: { min: 0, step: 'any', inputmode: 'decimal' } })),
    ],
    async onSubmit(form) {
      const body = {
        assetId: asset.id,
        value: Number(val(form, 'value')) * factor,
        date: `${val(form, 'date')}T00:00:00Z`,
        nav: val(form, 'nav') === '' ? null : Number(val(form, 'nav')) * factor,
      };
      let saved;
      if (editing) {
        await api.prices.update(price.id, body);
        saved = { ...price, ...body };
      } else {
        saved = await api.prices.create(body);
      }
      toast(editing ? 'Price updated' : 'Price added');
      return saved;
    },
  });
}

export function deletePrice(asset, price, label) {
  return confirmDialog({
    title: 'Delete price?',
    message: `This deletes the ${asset.symbol} price for ${label}.`,
    confirmLabel: 'Delete price',
    action: () => api.prices.remove(price.id),
  });
}

// Read controls through `elements`: names like "name" would otherwise hit the form's own properties.
const val = (form, name) => form.elements.namedItem(name).value;
