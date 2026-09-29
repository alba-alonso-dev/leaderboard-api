// Development helper for Swagger UI: signs game-server requests in the browser.
// Authorize "ApiKeyHmac" with `keyId:secret`. The secret stays in this browser tab; it is never sent to the API.
(function () {
  'use strict';

  const encoder = new TextEncoder();
  const toHex = (buffer) => Array.from(new Uint8Array(buffer)).map((b) => b.toString(16).padStart(2, '0')).join('');
  const toBase64 = (buffer) => btoa(String.fromCharCode(...new Uint8Array(buffer)));

  async function sign(request) {
    const headers = request.headers || {};
    const credential = headers['X-Api-Key'];
    if (!credential || !credential.includes(':')) {
      return request;
    }

    const separator = credential.indexOf(':');
    const keyId = credential.slice(0, separator);
    const secret = credential.slice(separator + 1);
    const body = typeof request.body === 'string' ? request.body : '';
    const path = new URL(request.url, window.location.origin).pathname;
    const timestamp = Math.floor(Date.now() / 1000).toString();
    const nonce = toHex(crypto.getRandomValues(new Uint8Array(16)));
    const bodyHash = toHex(await crypto.subtle.digest('SHA-256', encoder.encode(body)));
    const canonical = [request.method.toUpperCase(), path, timestamp, nonce, bodyHash].join('\n');

    const key = await crypto.subtle.importKey('raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    const signature = toBase64(await crypto.subtle.sign('HMAC', key, encoder.encode(canonical)));

    headers['X-Api-Key'] = keyId;
    headers['X-Timestamp'] = timestamp;
    headers['X-Nonce'] = nonce;
    headers['X-Signature'] = signature;
    request.headers = headers;
    return request;
  }

  window.leaderboardSignRequest = sign;
})();
