// In-memory cache of all assets, shared by the views and kept current from CRUD calls and the live feed.

import * as api from './api.js';
import { realtime } from './realtime.js';

export const KNOWN_TYPES = ['Stock', 'ETF', 'Fund', 'Crypto', 'Currency', 'Gold', 'Coin', 'Metal'];

const byId = new Map();
let loading = null;
const listeners = new Set();

function emit() {
  listeners.forEach(fn => fn());
}

export function onAssetsChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function loadAssets({ force = false } = {}) {
  if (!loading || force) {
    loading = api.assets.list().then(list => {
      byId.clear();
      for (const a of list) byId.set(a.id, a);
      emit();
      return allAssets();
    });
    loading.catch(() => { loading = null; });
  }
  return loading;
}

export const allAssets = () => [...byId.values()];
export const assetById = id => byId.get(id);

export function upsertAsset(asset) {
  byId.set(asset.id, asset);
  emit();
}

export function removeAsset(id) {
  byId.delete(id);
  emit();
}

realtime.addEventListener('message', e => {
  if (e.detail.type === 'asset.created' && loading) upsertAsset(e.detail.data);
});
