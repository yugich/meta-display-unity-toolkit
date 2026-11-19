// Pretends to be the Web App on the glasses: says hello and answers every "play" with an "action".
const base = process.argv[2] || 'ws://localhost:8099';
const room = process.argv[3] || 'unitytest';
const log = (...a) => console.log(new Date().toISOString(), ...a);
function connect() {
  const ws = new WebSocket(`${base}?room=${room}&role=glasses`);
  ws.onopen = () => {
    log('open');
    ws.send(JSON.stringify({ type: 'hello', from: 'glasses', actions: ['idle', 'wave', 'happy'] }));
  };
  ws.onmessage = (e) => {
    log('recv', e.data);
    const m = JSON.parse(e.data);
    if (m.type === 'hello' && m.from === 'unity') ws.send(JSON.stringify({ type: 'hello', from: 'glasses', actions: ['idle', 'wave', 'happy'] }));
    if (m.type === 'play') ws.send(JSON.stringify({ type: 'action', name: m.name }));
  };
  ws.onclose = () => { log('closed, retry'); setTimeout(connect, 1000); };
  ws.onerror = () => {};
}
connect();
setTimeout(() => process.exit(0), 10 * 60 * 1000);
