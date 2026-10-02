// The signed-in admin, loaded once at startup.

import * as api from './api.js';

let current = null;

export const currentUser = () => current;

export async function loadSession() {
  current = await api.auth.me();
  return current;
}

export async function signOut() {
  try { await api.auth.logout(); } catch {}
  location.replace('login.html');
}

/** A random password that clears the minimum length comfortably. */
export function generatePassword() {
  const bytes = crypto.getRandomValues(new Uint8Array(18));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_');
}
