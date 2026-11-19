// Checks the relay: messages reach the other client of the same room and nobody in another room.
const base = process.argv[2] || 'ws://localhost:8099';
const open = (room, role) => new Promise((resolve, reject) => {
  const ws = new WebSocket(`${base}?room=${room}&role=${role}`);
  ws.received = [];
  ws.onmessage = (e) => ws.received.push(String(e.data));
  ws.onopen = () => resolve(ws);
  ws.onerror = (e) => reject(new Error('connect failed ' + role));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const unity = await open('t1', 'unity');
const glasses = await open('t1', 'glasses');
const other = await open('t2', 'glasses');
unity.send(JSON.stringify({ type: 'play', name: 'wave' }));
glasses.send(JSON.stringify({ type: 'action', name: 'happy' }));
await sleep(300);
const ok =
  glasses.received.length === 1 && JSON.parse(glasses.received[0]).name === 'wave' &&
  unity.received.length === 1 && JSON.parse(unity.received[0]).name === 'happy' &&
  other.received.length === 0;
console.log(JSON.stringify({ glasses: glasses.received, unity: unity.received, other: other.received }));
console.log(ok ? 'RELAY OK' : 'RELAY FAILED');
for (const ws of [unity, glasses, other]) ws.close();
process.exit(ok ? 0 : 1);
