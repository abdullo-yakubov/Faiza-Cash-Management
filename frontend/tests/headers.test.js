// D-10: the SPA ships self-hosted assets only (docs/ASSETS.md), so the nginx CSP must not allow external font/style hosts.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const conf = fs.readFileSync(new URL('../nginx.conf', import.meta.url), 'utf8');
const csp = conf.match(/Content-Security-Policy "([^"]+)"/)?.[1];

test('nginx CSP is present and allows only same-origin fonts and styles', () => {
  assert.ok(csp, 'Content-Security-Policy header is missing');
  assert.doesNotMatch(csp, /fonts\.googleapis\.com|fonts\.gstatic\.com|https?:/, csp);
  const directives = Object.fromEntries(csp.split(';').map(x => x.trim().split(/\s+/)).map(([name, ...values]) => [name, values]));
  assert.deepEqual(directives['font-src'] ?? directives['default-src'], ["'self'"]);
  assert.equal(directives['frame-ancestors']?.[0], "'none'");
});

test('index.html and styles do not reference external font CDNs', () => {
  const sources = ['../index.html', '../src/tokens.css'].map(path => { try { return fs.readFileSync(new URL(path, import.meta.url), 'utf8'); } catch { return ''; } }).join('\n');
  assert.doesNotMatch(sources, /fonts\.googleapis\.com|fonts\.gstatic\.com/);
});
