// Cross-language interop harness for the TS binding.
//   node dist/test/interop.js server <port>        → listen, echo "echo:<msg>" back
//   node dist/test/interop.js client <host:port>   → connect, send "ping", expect "echo:ping"
//
// Pairs against the C# binding (bindings/interop-test) running the opposite role.
import { GnsServer, GnsClient, SendType, shutdown } from '../index';

const mode = process.argv[2];
const target = process.argv[3];

if (!mode || !target) {
  console.error('usage: interop.js <server|client> <port|host:port>');
  process.exit(2);
}

if (mode === 'server') {
  const port = parseInt(target, 10);
  const server = new GnsServer(port);
  console.log(`[ts-server] listening on ${port}`);
  server.on('connect', (conn) => console.log(`[ts-server] client connected: ${conn}`));
  server.on('disconnect', (conn, reason) => console.log(`[ts-server] client ${conn} left reason=${reason}`));
  server.on('message', (conn, data) => {
    const msg = data.toString();
    console.log(`[ts-server] recv "${msg}" from ${conn} — echoing`);
    server.send(conn, Buffer.from('echo:' + msg), SendType.Reliable);
  });
  // Stay up for a fixed window; the client decides PASS/FAIL.
  setTimeout(() => {
    server.destroy();
    shutdown();
    process.exit(0);
  }, 20000);
} else if (mode === 'client') {
  const client = new GnsClient();
  const timeout = setTimeout(() => {
    console.error('[ts-client] TIMEOUT — no echo:ping');
    process.exit(1);
  }, 10000);
  client.on('connect', () => {
    console.log('[ts-client] connected — sending "ping"');
    client.send(Buffer.from('ping'), SendType.Reliable);
  });
  client.on('disconnect', (_c, reason) => console.log(`[ts-client] disconnected reason=${reason}`));
  client.on('message', (_conn, data) => {
    const msg = data.toString();
    console.log(`[ts-client] recv "${msg}"`);
    if (msg === 'echo:ping') {
      console.log('TS-CLIENT PASS');
      clearTimeout(timeout);
      client.destroy();
      shutdown();
      process.exit(0);
    }
  });
  console.log(`[ts-client] dialing ${target}`);
  client.connect(target);
} else {
  console.error(`unknown mode: ${mode}`);
  process.exit(2);
}
