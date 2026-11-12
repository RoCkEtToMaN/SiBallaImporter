const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const test = require('node:test');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '..', 'musica.js'), 'utf8');
class Element {
  constructor(tag = 'div') { this.tag = tag; this.children = []; this.dataset = {}; this.attrs = {}; this.style = { setProperty() {} }; this.hidden = false; }
  appendChild(n) { this.children.push(n); return n; }
  replaceChildren() { this.children = []; }
  setAttribute(k, v) { this.attrs[k] = v; }
  addEventListener(k, f) { this[k] = f; }
  remove() {}
  focus() {}
  scrollIntoView() {}
  querySelector(s) { return this.querySelectorAll(s)[0]; }
  querySelectorAll(s) {
    return this.children.flatMap(n => [
      ...((s === n.tag || (s === '[aria-pressed]' && 'aria-pressed' in n.attrs) || (s === '[data-categoria]' && n.dataset.categoria)) ? [n] : []),
      ...n.querySelectorAll(s)
    ]);
  }
}
const videoLink = id => 'https://youtu.be/' + id;
const row = (title, order, link = videoLink('aaaaaaaaaaa'), selection = 'one') => ({ tipo: 'brano', selezione: selection, titolo_brano: title, ordine: order, link_youtube: link });
const selection = key => ({ tipo: 'selezione', selezione: key, titolo_selezione: key });
async function setup(rows, lazy = false) {
  const ids = Object.fromEntries(['filtri-musica', 'selezioni-musica', 'stato-musica', 'lettore', 'video', 'titolo-brano', 'apri-youtube', 'stato-riproduzione'].map(id => [id, new Element()]));
  ids.lettore.appendChild(new Element('button'));
  const players = [];
  const YT = { Player: function (frame, options) {
    this.frame = frame; this.events = options.events; this.calls = []; this.playlist = []; this.playlistIndex = 0;
    this.destroy = () => { this.destroyed = true; };
    this.playVideo = () => this.calls.push(['play']);
    this.loadVideoById = id => this.calls.push(['video', id]);
    this.loadPlaylist = list => this.calls.push(['playlist', list.list]);
    this.getPlaylist = () => this.playlist;
    this.getPlaylistIndex = () => this.playlistIndex;
    this.emit = data => this.events.onStateChange({ target: this, data });
    players.push(this);
  } };
  const window = lazy ? {} : { YT };
  const document = { head: new Element('head'), createElement: t => new Element(t), getElementById: id => ids[id] };
  vm.runInNewContext(source, { document, window, URL, location: { protocol: 'https:', origin: 'https://anteprima.siballa.pages.dev' }, matchMedia: () => ({ matches: true }), fetch: () => Promise.resolve({ ok: true, json: () => Promise.resolve({ righe: rows }) }) });
  await new Promise(setImmediate);
  return { ids, players, window, document, YT, buttons: () => ids['selezioni-musica'].querySelectorAll('button'), tick: () => new Promise(setImmediate) };
}
test('segue Ordine, aggiorna titolo/evidenziazione, ignora duplicati ENDED e si ferma alla fine', async () => {
  const h = await setup([selection('one'), row('Secondo', 2, videoLink('bbbbbbbbbbb')), row('Primo', 1)]);
  assert.equal(h.players.length, 0);
  h.buttons()[0].click(); await h.tick();
  const p = h.players[0];
  assert.equal(new URL(p.frame.src).searchParams.get('origin'), 'https://anteprima.siballa.pages.dev');
  assert.match(p.frame.allow, /autoplay/);
  p.events.onReady({ target: p }); assert.deepEqual(p.calls, [['play']]);
  p.emit(1); p.emit(2); assert.equal(p.calls.length, 1);
  p.emit(0); assert.deepEqual(p.calls[1], ['video', 'bbbbbbbbbbb']);
  assert.equal(h.ids['titolo-brano'].textContent, 'Secondo');
  assert.equal(h.buttons()[1].attrs['aria-pressed'], 'true');
  assert.equal(h.buttons()[0].attrs['aria-pressed'], 'false');
  p.emit(0); assert.equal(p.calls.length, 2);
  p.emit(1); p.emit(0); assert.equal(p.calls.length, 2);
  assert.equal(h.ids['stato-riproduzione'].textContent, 'Selezione terminata.');
});
test('inizia dal brano scelto e non passa a un’altra selezione', async () => {
  const h = await setup([selection('one'), selection('two'), row('Primo', 1), row('Secondo', 2), row('Altro', 1, videoLink('ccccccccccc'), 'two')]);
  h.buttons()[1].click(); await h.tick();
  const p = h.players[0]; p.emit(1); p.emit(0);
  assert.equal(p.calls.length, 0);
  assert.equal(h.ids['titolo-brano'].textContent, 'Secondo');
});
test('completa i video interni delle playlist prima di passare alla riga successiva; esclude ricerche', async () => {
  const h = await setup([selection('one'), row('Video', 1), row('Ricerca', 2, 'https://www.youtube.com/results?search_query=swing'), row('Playlist', 3, 'https://www.youtube.com/playlist?list=PL1234567890'), row('Finale', 4, videoLink('bbbbbbbbbbb'))]);
  h.buttons()[0].click(); await h.tick(); const p = h.players[0];
  p.emit(1); p.emit(0); assert.deepEqual(p.calls[0], ['playlist', 'PL1234567890']);
  p.playlist = ['x', 'y']; p.playlistIndex = 0; p.emit(1); p.emit(0);
  assert.equal(p.calls.length, 1);
  p.playlistIndex = 1; p.emit(1); p.emit(0);
  assert.deepEqual(p.calls[1], ['video', 'bbbbbbbbbbb']);
});
test('chiusura, cambio filtro e nuova selezione disattivano tutti i vecchi eventi', async () => {
  const h = await setup([selection('one'), row('Primo', 1), row('Secondo', 2)]);
  h.buttons()[0].click(); await h.tick(); const first = h.players[0];
  h.buttons()[1].click(); await h.tick(); assert.equal(first.destroyed, true);
  first.emit(0); assert.equal(h.ids['titolo-brano'].textContent, 'Secondo');
  const second = h.players[1];
  h.ids.lettore.querySelector('button').click();
  assert.equal(second.destroyed, true); second.emit(0);
  assert.equal(h.ids.video.children.length, 0);
  h.buttons()[0].click(); await h.tick(); const third = h.players[2];
  h.ids['filtri-musica'].children[0].click(); third.emit(0);
  assert.equal(third.destroyed, true); assert.equal(h.ids.lettore.hidden, true);
});
test('il caricamento API ritardato non riapre un lettore chiuso e usa solo l’ultima scelta', async () => {
  const h = await setup([selection('one'), row('Primo', 1), row('Secondo', 2)], true);
  h.buttons()[0].click(); h.buttons()[1].click();
  assert.equal(h.document.head.children.length, 1);
  h.window.YT = h.YT; h.window.onYouTubeIframeAPIReady(); await h.tick();
  assert.equal(h.players.length, 1); assert.match(h.ids['titolo-brano'].textContent, /Secondo/);
  const closed = await setup([selection('one'), row('Primo', 1)], true);
  closed.buttons()[0].click(); closed.ids.lettore.querySelector('button').click();
  closed.window.YT = closed.YT; closed.window.onYouTubeIframeAPIReady(); await closed.tick();
  assert.equal(closed.players.length, 0); assert.equal(closed.ids.lettore.hidden, true);
});
test('segnala blocco autoplay, errore video e API non disponibile', async () => {
  const h = await setup([selection('one'), row('Primo', 1)]);
  h.buttons()[0].click(); await h.tick(); const p = h.players[0];
  p.events.onAutoplayBlocked(); assert.match(h.ids['stato-riproduzione'].textContent, /Premi Play/);
  p.events.onError(); assert.match(h.ids['stato-riproduzione'].textContent, /collegamento YouTube/);
  const failed = await setup([selection('one'), row('Primo', 1)], true);
  failed.buttons()[0].click(); failed.document.head.children[0].onerror(); await failed.tick();
  assert.match(failed.ids['stato-riproduzione'].textContent, /manualmente/);
  assert.equal(failed.ids.lettore.hidden, false);
});