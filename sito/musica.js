(function () {
  var filtri = document.getElementById('filtri-musica');
  var elenco = document.getElementById('selezioni-musica');
  var stato = document.getElementById('stato-musica');
  var lettore = document.getElementById('lettore');
  var video = document.getElementById('video');
  var ultimoBrano;
  var tinte = ['#6FCF97', '#FFD400', '#F2994A', '#9B8CFF'];
  function nodo(tag, classe, testo) {
    var n = document.createElement(tag);
    if (classe) n.className = classe;
    if (testo) n.textContent = testo;
    return n;
  }
  function ferma() {
    video.replaceChildren();
    lettore.hidden = true;
    elenco.querySelectorAll('[aria-pressed]').forEach(function (b) { b.setAttribute('aria-pressed', 'false'); });
  }
  function destinazione(valore) {
    try {
      var u = new URL(valore);
      if (u.protocol !== 'https:' || u.username || u.password || u.port) return null;
      if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'youtu.be', 'www.youtube-nocookie.com'].indexOf(u.hostname) < 0) return null;
      var id = u.hostname === 'youtu.be' ? u.pathname.slice(1) : u.searchParams.get('v');
      var percorso = u.pathname.match(/^\/(?:embed|shorts)\/([A-Za-z0-9_-]{11})$/);
      if (percorso) id = percorso[1];
      if (/^[A-Za-z0-9_-]{11}$/.test(id || '')) return { url: u.href, embed: 'https://www.youtube-nocookie.com/embed/' + id };
      var lista = u.searchParams.get('list');
      if (['/playlist', '/watch', '/embed/videoseries'].indexOf(u.pathname) >= 0 && /^[A-Za-z0-9_-]{10,100}$/.test(lista || '')) {
        return { url: u.href, embed: 'https://www.youtube-nocookie.com/embed/videoseries?list=' + encodeURIComponent(lista) };
      }
      if (u.pathname === '/results' && (u.searchParams.get('search_query') || '').trim()) return { url: u.href };
    } catch (e) {}
    return null;
  }
  function ascolta(brano, link, pulsante) {
    ferma();
    ultimoBrano = pulsante;
    var titolo = brano.titolo_brano + (brano.artista ? ' · ' + brano.artista : '');
    document.getElementById('titolo-brano').textContent = titolo;
    document.getElementById('apri-youtube').href = link.url;
    var frame = document.createElement('iframe');
    frame.src = link.embed;
    frame.title = titolo;
    frame.allow = 'encrypted-media; picture-in-picture; fullscreen';
    frame.referrerPolicy = 'strict-origin-when-cross-origin';
    frame.allowFullscreen = true;
    video.appendChild(frame);
    lettore.hidden = false;
    pulsante.setAttribute('aria-pressed', 'true');
    lettore.querySelector('button').focus({ preventScroll: true });
    lettore.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
  }
  function ordine(a, b) {
    function numero(r) { return r.ordine !== '' && r.ordine != null && Number.isFinite(Number(r.ordine)) ? Number(r.ordine) : 999999; }
    return numero(a) - numero(b);
  }
  function filtro(titolo, selezione) {
    var b = nodo('button', '', titolo);
    b.type = 'button';
    b.setAttribute('aria-pressed', String(selezione === null));
    b.addEventListener('click', function () {
      filtri.querySelectorAll('button').forEach(function (f) { f.setAttribute('aria-pressed', String(f === b)); });
      elenco.querySelectorAll('[data-categoria]').forEach(function (s) { s.hidden = selezione !== null && s.dataset.categoria !== selezione; });
      ferma();
    });
    filtri.appendChild(b);
  }
  function disegna(righe) {
    var selezioni = righe.filter(function (r) { return r.tipo === 'selezione' && r.selezione && r.titolo_selezione; }).sort(ordine);
    var viste = new Set();
    selezioni = selezioni.filter(function (r) { if (viste.has(r.selezione)) return false; viste.add(r.selezione); return true; });
    if (!selezioni.length) { stato.textContent = 'Non ci sono ancora selezioni musicali disponibili.'; return; }
    filtro('Tutta la musica', null);
    selezioni.forEach(function (s, i) {
      filtro(s.titolo_selezione, s.selezione);
      var card = nodo('article', 'card selezione');
      card.dataset.categoria = s.selezione;
      card.style.setProperty('--tinta', tinte[i % tinte.length]);
      var disco = nodo('div', 'disco'); disco.setAttribute('aria-hidden', 'true');
      card.appendChild(disco);
      card.appendChild(nodo('div', 'occasione', s.sottotitolo));
      card.appendChild(nodo('h3', '', s.titolo_selezione));
      card.appendChild(nodo('p', 'descrizione', s.descrizione));
      var lista = nodo('ul', 'brani');
      righe.filter(function (r) { return r.tipo === 'brano' && r.selezione === s.selezione && r.titolo_brano; }).sort(ordine).forEach(function (brano) {
        var link = destinazione(brano.link_youtube);
        if (!link) return;
        var riga = nodo('li');
        var b = nodo(link.embed ? 'button' : 'a', 'brano');
        if (link.embed) {
          b.type = 'button'; b.setAttribute('aria-pressed', 'false');
          b.addEventListener('click', function () { ascolta(brano, link, b); });
        } else { b.href = link.url; b.target = '_blank'; b.rel = 'noopener noreferrer'; }
        var icona = nodo('span', 'play', link.embed ? '▶' : '↗'); icona.setAttribute('aria-hidden', 'true');
        b.appendChild(icona);
        var testo = nodo('span');
        testo.appendChild(nodo('strong', '', brano.titolo_brano));
        testo.appendChild(nodo('small', '', brano.artista));
        b.appendChild(testo);
        b.appendChild(nodo('span', 'invito', link.embed ? 'Ascolta' : 'Apri YouTube'));
        riga.appendChild(b); lista.appendChild(riga);
      });
      card.appendChild(lista);
      if (!lista.children.length) card.appendChild(nodo('p', 'nota', 'Nuovi brani in arrivo.'));
      if (s.selezione === 'italia') {
        var azioni = nodo('div', 'azioni');
        var artisti = nodo('a', '', 'Scopri band e DJ →'); artisti.href = '/artisti';
        azioni.appendChild(artisti); card.appendChild(azioni);
      }
      elenco.appendChild(card);
    });
    stato.hidden = true;
  }
  lettore.querySelector('button').addEventListener('click', function () {
    ferma();
    if (ultimoBrano) ultimoBrano.focus();
  });
  fetch('musica.json', { cache: 'no-cache' })
    .then(function (r) { if (!r.ok) throw new Error('Musica non disponibile'); return r.json(); })
    .then(function (d) { if (!d || !Array.isArray(d.righe)) throw new Error('Dati non validi'); disegna(d.righe); })
    .catch(function () { stato.textContent = 'Non riesco a caricare la musica. Riprova tra poco.'; });
})();