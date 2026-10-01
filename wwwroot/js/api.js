// Thin wrapper over the REST API. The UI is served by the API itself, so paths are same-origin.

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

async function request(method, path, body) {
  const init = { method, headers: { Accept: 'application/json' } };
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }

  let res;
  try {
    res = await fetch(path, init);
  } catch {
    throw new ApiError(0, 'Could not reach the server.');
  }

  if (!res.ok) throw new ApiError(res.status, await errorMessage(res));
  if (res.status === 204) return null;

  const type = res.headers.get('content-type') ?? '';
  return type.includes('json') ? res.json() : res.text();
}

// Errors come back either as a plain string (Conflict/BadRequest with a message) or as ProblemDetails.
async function errorMessage(res) {
  const text = await res.text().catch(() => '');
  try {
    const json = JSON.parse(text);
    if (typeof json === 'string') return json;
    if (json?.errors) return Object.values(json.errors).flat().join(' ');
    if (json?.title) return json.title;
  } catch {
    if (text) return text;
  }
  return res.status === 404 ? 'Not found.' : `Request failed (${res.status}).`;
}

export const assets = {
  list: () => request('GET', 'api/assets'),
  get: id => request('GET', `api/assets/${id}`),
  prices: id => request('GET', `api/assets/${id}/prices`),
  create: asset => request('POST', 'api/assets', asset),
  update: (id, asset) => request('PUT', `api/assets/${id}`, asset),
  remove: id => request('DELETE', `api/assets/${id}`),
};

export const prices = {
  latest(identifiers) {
    const qs = new URLSearchParams();
    for (const id of identifiers) qs.append('identifiers', id);
    return request('GET', `api/prices/latest?${qs}`);
  },
  create: price => request('POST', 'api/prices', price),
  update: (id, price) => request('PUT', `api/prices/${id}`, price),
  remove: id => request('DELETE', `api/prices/${id}`),
};
