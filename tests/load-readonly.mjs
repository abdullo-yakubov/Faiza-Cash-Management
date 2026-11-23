// Read-only dashboard load probe. Configure API_URL and TOKEN; does not create data.
const base = new URL(process.env.API_URL || 'http://127.0.0.1:5085');
const token = process.env.TOKEN;
const requests = Number(process.env.REQUESTS || 10000);
const concurrency = Number(process.env.CONCURRENCY || 20);
const path = process.env.PATH_QUERY || '/api/dashboard';

if (!token) throw new Error('Set TOKEN to a short-lived test-user access token.');
if (!path.startsWith('/api/dashboard')) throw new Error('This probe is limited to the read-only /api/dashboard endpoint.');
if (!['http:', 'https:'].includes(base.protocol)) throw new Error('API_URL must use HTTP or HTTPS.');
if (!['localhost', '127.0.0.1', '::1'].includes(base.hostname) && process.env.LOAD_ALLOW_REMOTE !== '1') {
  throw new Error('Remote load is disabled. Set LOAD_ALLOW_REMOTE=1 only for an approved test environment.');
}
if (!Number.isSafeInteger(requests) || requests < 1 || requests > 1_000_000) throw new Error('REQUESTS must be between 1 and 1,000,000.');
if (!Number.isSafeInteger(concurrency) || concurrency < 1 || concurrency > 200) throw new Error('CONCURRENCY must be between 1 and 200.');

const latencies = new Array(requests);
const failures = [];
let next = 0;
let failureCount = 0;
const started = performance.now();
async function worker() {
  while (true) {
    const index = next++;
    if (index >= requests) return;
    const begin = performance.now();
    try {
      const response = await fetch(new URL(path, base), {
        headers: {Authorization: `Bearer ${token}`},
        signal: AbortSignal.timeout(30_000)
      });
      latencies[index] = performance.now() - begin;
      if (!response.ok) {
        failureCount++;
        if (failures.length < 20) failures.push({status: response.status});
        await response.body?.cancel();
      } else {
        await response.body?.cancel();
      }
    } catch (error) {
      latencies[index] = performance.now() - begin;
      failureCount++;
      if (failures.length < 20) failures.push({error: error.name});
    }
  }
}

await Promise.all(Array.from({length: Math.min(concurrency, requests)}, worker));
const durationMs = performance.now() - started;
const sorted = latencies.sort((a, b) => a - b);
const percentile = p => Math.round(sorted[Math.ceil(sorted.length * p) - 1] * 100) / 100;
process.stdout.write(JSON.stringify({
  endpoint: path,
  totalRequests: requests,
  concurrency,
  durationMs: Math.round(durationMs),
  throughputPerSecond: Math.round(requests / (durationMs / 1000) * 100) / 100,
  latencyMs: {p50: percentile(0.5), p95: percentile(0.95), p99: percentile(0.99)},
  failures: failureCount,
  failuresSample: failures,
  note: 'Read-only probe. Use a disposable database and approved test environment; these numbers are not an SLA verdict.'
}, null, 2) + '\n');
if (failures.length) process.exitCode = 1;
