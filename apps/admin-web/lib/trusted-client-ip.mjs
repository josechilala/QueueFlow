import { createHmac, timingSafeEqual } from 'node:crypto';
import { BlockList, isIP } from 'node:net';

const ipHeader = 'x-queueflow-client-ip';
const signatureHeader = 'x-queueflow-client-ip-signature';

export function createProxyTrust(value = '') {
  const list = new BlockList();
  for (const entry of value.split(',').map(value => value.trim()).filter(Boolean)) {
    const [address, prefix, extra] = entry.split('/');
    const version = isIP(address);
    if (!version || extra !== undefined) throw new Error('Invalid QUEUEFLOW_TRUSTED_PROXIES entry');
    const family = version === 4 ? 'ipv4' : 'ipv6';
    if (prefix === undefined) list.addAddress(address, family);
    else {
      const bits = Number(prefix);
      if (!/^\d+$/.test(prefix) || bits < 1 || bits > (version === 4 ? 32 : 128)) throw new Error('Invalid trusted proxy prefix');
      list.addSubnet(address, bits, family);
    }
  }
  return address => !!isIP(address) && list.check(address, isIP(address) === 4 ? 'ipv4' : 'ipv6');
}

function signature(ip, key) {
  return createHmac('sha256', key).update(ip).digest('hex');
}

// Called before Next sees the request. Never accept internal headers from the network.
export function stampClientIp(request, trusted, key) {
  delete request.headers[ipHeader];
  delete request.headers[signatureHeader];
  let ip = request.socket.remoteAddress;
  if (!ip || !isIP(ip)) return;
  const forwarded = request.headers['x-forwarded-for'];
  if (typeof forwarded === 'string') {
    const chain = forwarded.split(',').slice(-32).map(value => value.trim());
    // Ignore the untrusted prefix, including malformed values supplied by a client.
    for (let index = chain.length - 1; index >= 0 && trusted(ip); index--) {
      if (!isIP(chain[index])) break;
      ip = chain[index];
    }
  }
  request.headers[ipHeader] = ip;
  request.headers[signatureHeader] = signature(ip, key);
}

export function clientIpHeaders(headers) {
  const key = process.env.QUEUEFLOW_INTERNAL_IP_KEY;
  const ip = headers.get(ipHeader);
  const supplied = headers.get(signatureHeader);
  if (!key || !ip || !isIP(ip) || !supplied || !/^[a-f0-9]{64}$/.test(supplied)) return {};
  if (!timingSafeEqual(Buffer.from(supplied, 'hex'), Buffer.from(signature(ip, key), 'hex'))) return {};
  return { 'X-Forwarded-For': ip };
}
