// WebSocket client for /ws. Emits "message" ({ type, data }) and "status" events and reconnects with backoff.

class Realtime extends EventTarget {
  status = 'connecting';
  #socket = null;
  #retry = 0;

  connect() {
    const url = new URL('ws', location.href);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';

    this.#setStatus('connecting');
    const socket = new WebSocket(url);
    this.#socket = socket;

    socket.addEventListener('open', () => {
      this.#retry = 0;
      this.#setStatus('open');
    });
    socket.addEventListener('message', e => {
      let msg;
      try { msg = JSON.parse(e.data); } catch { return; }
      if (msg?.type) this.dispatchEvent(new CustomEvent('message', { detail: msg }));
    });
    socket.addEventListener('close', () => {
      if (this.#socket !== socket) return;
      this.#setStatus('closed');
      const delay = Math.min(30_000, 1000 * 2 ** this.#retry++);
      setTimeout(() => this.connect(), delay);
    });
  }

  #setStatus(status) {
    this.status = status;
    this.dispatchEvent(new CustomEvent('status', { detail: status }));
  }
}

export const realtime = new Realtime();
