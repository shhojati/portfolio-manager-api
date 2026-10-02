// Login page: POST /api/auth/login sets the session cookie, then we go back to where the user was.

const form = document.getElementById('login-form');
const error = document.getElementById('login-error');
const submit = form.querySelector('button[type=submit]');

// Where to go after signing in: ?next= from the app, or the fragment the server's redirect carried over.
// Only same-page hash routes are followed, never arbitrary URLs.
const next = new URLSearchParams(location.search).get('next') || location.hash;
const target = next && /^#\/[\w/?=&%.-]*$/.test(next) ? `./${next}` : './';

// Already signed in? Skip the form.
fetch('api/auth/me', { headers: { Accept: 'application/json' } })
  .then(res => res.ok ? res.json() : null)
  .then(user => { if (user?.role === 'Admin') location.replace(target); })
  .catch(() => {});

form.addEventListener('submit', async e => {
  e.preventDefault();
  if (!form.reportValidity()) return;

  error.hidden = true;
  submit.disabled = true;
  submit.textContent = 'Signing in…';
  try {
    const res = await fetch('api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({
        username: form.elements.namedItem('username').value.trim(),
        password: form.elements.namedItem('password').value,
        rememberMe: form.elements.namedItem('remember').checked,
      }),
    });
    if (res.ok) {
      location.replace(target);
      return;
    }
    showError(await message(res));
  } catch {
    showError('Could not reach the server.');
  } finally {
    submit.disabled = false;
    submit.textContent = 'Sign in';
  }
});

function showError(text) {
  error.textContent = text;
  error.hidden = false;
  const password = form.elements.namedItem('password');
  password.value = '';
  password.focus();
}

async function message(res) {
  const text = await res.text().catch(() => '');
  try {
    const json = JSON.parse(text);
    if (typeof json === 'string') return json;
    if (json?.errors) return Object.values(json.errors).flat().join(' ');
    if (json?.title) return json.title;
  } catch {
    if (text) return text;
  }
  return `Sign-in failed (${res.status}).`;
}
