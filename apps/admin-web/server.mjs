import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import next from 'next';
import { createProxyTrust, stampClientIp } from './lib/trusted-client-ip.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const hostname = option('--hostname', '0.0.0.0');
const port = Number(option('--port', option('-p', process.env.PORT || '3000')));
const trusted = createProxyTrust(process.env.QUEUEFLOW_TRUSTED_PROXIES);
// Ephemeral, process-local proof that the entry server validated the socket peer.
const key = randomBytes(32).toString('hex');
process.env.QUEUEFLOW_INTERNAL_IP_KEY = key;
const app = next({ dev: args.includes('--dev'), hostname, port });
await app.prepare();
const handle = app.getRequestHandler();
const server = createServer((request, response) => {
  stampClientIp(request, trusted, key);
  handle(request, response);
});
server.on('upgrade', app.getUpgradeHandler());
server.listen(port, hostname);
