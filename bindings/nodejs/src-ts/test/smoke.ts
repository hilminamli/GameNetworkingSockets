// Loopback smoke test: server listens, client connects, both exchange a message.
// Run: npm run build && node dist/test/smoke.js
import { GnsServer, GnsClient, SendType, shutdown } from '../index';

const PORT = 27600;

function main(): Promise<void> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('TIMEOUT — handshake/echo did not complete')), 5000);

    const server = new GnsServer(PORT);
    const client = new GnsClient();

    server.on('connect', (conn) => {
      console.log(`[server] client connected: conn=${conn}`);
    });
    server.on('message', (conn, data) => {
      console.log(`[server] recv "${data.toString()}" from conn=${conn} — echoing`);
      server.send(conn, Buffer.from(`echo:${data.toString()}`), SendType.Reliable);
    });
    server.on('disconnect', (conn, reason) => {
      console.log(`[server] disconnect conn=${conn} reason=${reason}`);
    });

    client.on('connect', () => {
      console.log('[client] connected — sending "ping"');
      client.send(Buffer.from('ping'), SendType.Reliable);
    });
    client.on('message', (_conn, data) => {
      console.log(`[client] recv "${data.toString()}"`);
      if (data.toString() === 'echo:ping') {
        console.log('PASS — round trip succeeded');
        clearTimeout(timeout);
        client.destroy();
        server.destroy();
        shutdown();
        resolve();
      }
    });

    console.log(`[client] dialing 127.0.0.1:${PORT}`);
    client.connect(`127.0.0.1:${PORT}`);
  });
}

main().then(
  () => process.exit(0),
  (err) => {
    console.error(err);
    process.exit(1);
  },
);
