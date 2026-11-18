const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

// functions/api/visita.js senza import/export, con preparaTabella e giornoRoma finti
const source = fs.readFileSync('sito/functions/api/visita.js', 'utf8').replace(/^import .*;\r?\n/gm, '').replace(/export /g, '');
const context = vm.createContext({ Response, console, preparaTabella: async () => {}, giornoRoma: () => '2026-11-18' });
vm.runInContext(source, context);

function db() {
  const scritture = [];
  return {
    scritture,
    prepare(sql) { return { bind(...args) { return { sql, args }; } }; },
    async batch(op) { scritture.push(...op); },
  };
}
async function invia(dati, cf = {}) {
  const database = db();
  const request = {
    headers: new Headers({ 'user-agent': 'Mozilla/5.0 (iPhone)' }),
    cf,
    text: async () => JSON.stringify(dati),
  };
  const r = await context.onRequestPost({ request, env: { VISITE: database } });
  assert.equal(r.status, 204);
  return database.scritture.map((o) => o.args.slice(1, 3).join(':'));
}

test('apertura del video "Come funziona" contata per pagina', async () => {
  assert.deepEqual(await invia({ t: 'howto', pagina: 'bacheca', cosa: 'aperto' }), ['howto:bacheca|aperto']);
  assert.deepEqual(await invia({ t: 'howto', pagina: 'home', cosa: 'finito' }), ['howto:home|finito']);
});

test('valori strani ignorati', async () => {
  assert.deepEqual(await invia({ t: 'howto', pagina: 'bacheca', cosa: 'altro' }), []);
  assert.deepEqual(await invia({ t: 'howto', pagina: '<script>', cosa: 'aperto' }), []);
  assert.deepEqual(await invia({ t: 'howto', cosa: 'aperto' }), []);
});

test('il video non conta come visita della pagina', async () => {
  const s = await invia({ t: 'howto', pagina: 'community', cosa: 'aperto' });
  assert.ok(!s.some((x) => x.startsWith('pagina:') || x.startsWith('visitatori:')));
});

test('da un data center non conta e non finisce tra i bot', async () => {
  assert.deepEqual(await invia({ t: 'howto', pagina: 'home', cosa: 'aperto' }, { asOrganization: 'Amazon AWS' }), []);
});
