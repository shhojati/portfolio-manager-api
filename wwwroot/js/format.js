// Number and date formatting. Prices are stored in Rial; the user can choose to view them in Toman.

const UNIT_KEY = 'pm.unit';
let unit = 'IRR';
try { if (localStorage.getItem(UNIT_KEY) === 'IRT') unit = 'IRT'; } catch {}

const unitListeners = new Set();

export const getUnit = () => unit;
export const unitLabel = () => (unit === 'IRT' ? 'Toman' : 'Rial');

export function setUnit(next) {
  if (next === unit) return;
  unit = next;
  try { localStorage.setItem(UNIT_KEY, unit); } catch {}
  unitListeners.forEach(fn => fn(unit));
}

export function onUnitChange(fn) {
  unitListeners.add(fn);
  return () => unitListeners.delete(fn);
}

const toUnit = rial => (unit === 'IRT' ? rial / 10 : rial);

function decimalsFor(n) {
  const abs = Math.abs(n);
  if (abs === 0 || abs >= 1000) return 0;
  if (abs >= 1) return 2;
  return 6;
}

/** A price in the current unit, with thousands separators. */
export function money(rial) {
  if (rial == null) return '—';
  const n = toUnit(Number(rial));
  return n.toLocaleString('en-US', { maximumFractionDigits: decimalsFor(n) });
}

const compactFmt = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 2 });

/** A short price in the current unit for axis labels, e.g. 1.25M. */
export function moneyCompact(rial) {
  const n = toUnit(Number(rial));
  return Math.abs(n) < 1000 ? money(rial) : compactFmt.format(n);
}

export function percent(fraction, { sign = true } = {}) {
  if (fraction == null || !Number.isFinite(fraction)) return '—';
  const s = (fraction * 100).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return sign && fraction > 0 ? `+${s}%` : `${s}%`;
}

// Price dates are calendar days (Tehran) stored as midnight UTC; the date part is all that matters.
export const dayKey = value => String(value).slice(0, 10);
export const dayToTime = key => Date.parse(`${key}T00:00:00Z`);

const jalaliFmt = new Intl.DateTimeFormat('fa-IR-u-ca-persian-nu-latn', {
  year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'UTC',
});
const longDayFmt = new Intl.DateTimeFormat('en-US', {
  weekday: 'short', year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC',
});

/** Solar Hijri (Jalali) date, e.g. 1405/07/09. */
export function jalali(key) {
  const parts = jalaliFmt.formatToParts(new Date(dayToTime(key)));
  const get = type => parts.find(p => p.type === type)?.value ?? '';
  return `${get('year')}/${get('month')}/${get('day')}`;
}

export const longDay = key => longDayFmt.format(new Date(dayToTime(key)));

// Timestamps (e.g. CreatedAt) are UTC but may be serialized without a zone designator.
export function parseUtc(value) {
  const s = String(value);
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : `${s}Z`);
}

const dateTimeFmt = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
const timeFmt = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

export const dateTime = value => dateTimeFmt.format(parseUtc(value));
export const clock = date => timeFmt.format(date);

/** Today's date in Tehran as yyyy-MM-dd (the day the sync jobs would stamp). */
export function todayKey() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Tehran' }).format(new Date());
}

/** Lower-cases and folds Arabic/Persian letter variants so search matches either keyboard. */
export function fold(text) {
  return String(text ?? '')
    .toLowerCase()
    .replace(/[يى]/g, 'ی')
    .replace(/ك/g, 'ک')
    .replace(/[ً-ٟ‌]/g, '');
}
