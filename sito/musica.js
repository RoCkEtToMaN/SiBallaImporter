(function () {
  var filtri = document.getElementById('filtri-musica');
  var elenco = document.getElementById('selezioni-musica');
  var stato = document.getElementById('stato-musica');
  var lettore = document.getElementById('lettore');
  var video = document.getElementById('video');
  var ultimoBrano;
  var statoRiproduzione = document.getElementById('stato-riproduzione');
  var player;
  var apiYouTube;
  var sessione = 0;
  var coda = [];
  var posizione = 0;
  var inTransizione = false;
  var tinte = ['#56CCF2', '#6FCF97', '#F2994A', '#C9A0FF', '#FFD400'];   // lento, medio, veloce, live, altro
  var tutte = [];   // tutti i brani riproducibili, per "Ascolta tutto in ordine casuale"
  function nodo(tag, classe, testo) {
    var n = document.createElement(tag);
    if (classe) n.className = classe;
    if (testo) n.textContent = testo;
    return n;
  }
  function ferma() {
    sessione++;
    coda = [];
    if (player) { player.destroy(); player = null; }
    statoRiproduzione.textContent = '';
    video.replaceChildren();
    lettore.hidden = true;
    elenco.querySelectorAll('[aria-pressed]').forEach(function (b) { b.setAttribute('aria-pressed', 'false'); });
    elenco.querySelectorAll('.pl.suona').forEach(function (c) { c.classList.remove('suona'); });
  }
  function destinazione(valore) {
    try {
      var u = new URL(valore);
      if (u.protocol !== 'https:' || u.username || u.password || u.port) return null;
      if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'youtu.be', 'www.youtube-nocookie.com'].indexOf(u.hostname) < 0) return null;
      var id = u.hostname === 'youtu.be' ? u.pathname.slice(1) : u.searchParams.get('v');
      var percorso = u.pathname.match(/^\/(?:embed|shorts)\/([A-Za-z0-9_-]{11})$/);
      if (percorso) id = percorso[1];
      if (/^[A-Za-z0-9_-]{11}$/.test(id || '')) return { url: u.href, id: id, embed: 'https://www.youtube-nocookie.com/embed/' + id };
      var lista = u.searchParams.get('list');
      if (['/playlist', '/watch', '/embed/videoseries'].indexOf(u.pathname) >= 0 && /^[A-Za-z0-9_-]{10,100}$/.test(lista || '')) {
        return { url: u.href, lista: lista, embed: 'https://www.youtube-nocookie.com/embed/videoseries?list=' + encodeURIComponent(lista) };
      }
      if (u.pathname === '/results' && (u.searchParams.get('search_query') || '').trim()) return { url: u.href };
    } catch (e) {}
    return null;
  }
  function caricaAPI() {
    if (window.YT && window.YT.Player) return Promise.resolve(window.YT);
    if (apiYouTube) return apiYouTube;
    apiYouTube = new Promise(function (resolve, reject) {
      var precedente = window.onYouTubeIframeAPIReady;
      window.onYouTubeIframeAPIReady = function () {
        try { if (precedente) precedente(); }
        finally { resolve(window.YT); }
      };
      var script = document.createElement('script');
      script.src = 'https://www.youtube.com/iframe_api';
      script.onerror = function () { apiYouTube = null; script.remove(); reject(new Error('YouTube non disponibile')); };
      document.head.appendChild(script);
    });
    return apiYouTube;
  }
  function aggiornaBrano() {
    var voce = coda[posizione];
    ultimoBrano = voce.pulsante;
    var titolo = voce.brano.titolo_brano + (voce.brano.artista ? ' \u00b7 ' + voce.brano.artista : '');
    document.getElementById('titolo-brano').textContent = titolo;
    document.getElementById('apri-youtube').href = voce.link.url;
    elenco.querySelectorAll('[aria-pressed]').forEach(function (b) { b.setAttribute('aria-pressed', String(b === voce.pulsante)); });
    // onde in movimento sul riquadro del brano che suona
    var card = voce.pulsante.closest('.pl');
    elenco.querySelectorAll('.pl.suona').forEach(function (c) { if (c !== card) c.classList.remove('suona'); });
    if (card) card.classList.add('suona');
    var frame = video.querySelector('iframe');
    if (frame) frame.title = titolo;
    statoRiproduzione.textContent = 'Ascolto ' + (posizione + 1) + ' di ' + coda.length + (coda.casuale
      ? '. Tutti i brani in ordine casuale, uno dopo l\'altro.' : '. I brani proseguono in ordine fino alla fine della selezione.');
  }
  function successivo() {
    if (posizione + 1 >= coda.length) {
      statoRiproduzione.textContent = 'Selezione terminata.';
      return;
    }
    posizione++;
    aggiornaBrano();
    var link = coda[posizione].link;
    if (link.lista) player.loadPlaylist({ list: link.lista, listType: 'playlist', index: 0 });
    else player.loadVideoById(link.id);
  }
  function ascolta(voci, indice) {
    ferma();
    var corrente = sessione;
    coda = voci;
    posizione = indice;
    inTransizione = false;
    var link = coda[posizione].link;
    var frame = document.createElement('iframe');
    var src = new URL(link.embed);
    src.searchParams.set('enablejsapi', '1');
    src.searchParams.set('playsinline', '1');
    if (/^https?:$/.test(location.protocol)) src.searchParams.set('origin', location.origin);
    frame.src = src.href;
    frame.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
    frame.referrerPolicy = 'strict-origin-when-cross-origin';
    frame.allowFullscreen = true;
    video.appendChild(frame);
    lettore.hidden = false;
    aggiornaBrano();
    statoRiproduzione.textContent = 'Preparazione della sequenza...';
    lettore.querySelector('button').focus({ preventScroll: true });
    lettore.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
    caricaAPI().then(function (YT) {
      if (corrente !== sessione) return;
      player = new YT.Player(frame, { events: {
        onReady: function (e) {
          if (corrente !== sessione) return;
          aggiornaBrano();
          e.target.playVideo();
        },
        onStateChange: function (e) {
          if (corrente !== sessione) return;
          if (e.data === 1) { inTransizione = false; aggiornaBrano(); }
          if (e.data !== 0 || inTransizione) return;
          // Una playlist YouTube gestisce da sola i suoi video interni.
          if (coda[posizione].link.lista) {
            var lista = e.target.getPlaylist();
            if (!lista || e.target.getPlaylistIndex() < lista.length - 1) return;
          }
          inTransizione = true;
          successivo();
        },
        onAutoplayBlocked: function () {
          if (corrente === sessione) statoRiproduzione.textContent = 'Premi Play nel video per continuare: il browser ha bloccato la riproduzione automatica.';
        },
        onError: function () {
          if (corrente === sessione) statoRiproduzione.textContent = 'Questo video non viene riprodotto. Prova il collegamento YouTube o scegli un altro brano.';
        }
      } });
    }).catch(function () {
      if (corrente === sessione) statoRiproduzione.textContent = 'Riproduzione automatica non disponibile. Puoi ascoltare il video e scegliere manualmente il brano successivo.';
    });
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
      // riquadro: copertina colorata con i BPM (colonna "BPM" dello Sheet; senza, il sottotitolo), poi titolo e brani
      var card = nodo('article', 'pl');
      card.dataset.categoria = s.selezione;
      card.style.setProperty('--tinta', tinte[i % tinte.length]);
      var copertina = nodo('div', 'copertina');
      var bpm = (s.bpm || '').trim();
      var numerico = !!bpm && /\d/.test(bpm);
      var etichetta = nodo('div', 'bpm' + (numerico ? '' : ' testo'), bpm || s.sottotitolo || '');
      if (numerico && !/bpm/i.test(bpm)) etichetta.appendChild(nodo('small', '', 'BPM'));
      copertina.appendChild(etichetta);
      var onde = nodo('div', 'onde'); onde.setAttribute('aria-hidden', 'true');
      [40, 80, 55, 100].forEach(function (h) { var o = nodo('i'); o.style.height = h + '%'; onde.appendChild(o); });
      copertina.appendChild(onde);
      card.appendChild(copertina);
      var corpo = nodo('div', 'corpo');
      if (bpm && s.sottotitolo) corpo.appendChild(nodo('div', 'occasione', s.sottotitolo));
      corpo.appendChild(nodo('h3', '', s.titolo_selezione));
      if (s.descrizione) corpo.appendChild(nodo('p', 'descrizione', s.descrizione));
      var lista = nodo('ul', 'brani');
      var sequenza = [];
      righe.filter(function (r) { return r.tipo === 'brano' && r.selezione === s.selezione && r.titolo_brano; }).sort(ordine).forEach(function (brano) {
        var link = destinazione(brano.link_youtube);
        if (!link) return;
        var riga = nodo('li');
        var b = nodo(link.embed ? 'button' : 'a', 'brano');
        if (link.embed) {
          b.type = 'button'; b.setAttribute('aria-pressed', 'false');
          var indice = sequenza.length;
          sequenza.push({ brano: brano, link: link, pulsante: b });
          tutte.push({ brano: brano, link: link, pulsante: b });
          b.addEventListener('click', function () { ascolta(sequenza, indice); });
        } else { b.href = link.url; b.target = '_blank'; b.rel = 'noopener noreferrer'; }
        var icona = nodo('span', 'play', link.embed ? '▶' : '↗'); icona.setAttribute('aria-hidden', 'true');
        b.appendChild(icona);
        var testo = nodo('span');
        testo.appendChild(nodo('strong', '', brano.titolo_brano));
        if (brano.artista) testo.appendChild(nodo('small', '', brano.artista));
        b.appendChild(testo);
        b.appendChild(nodo('span', 'invito', link.embed ? (link.lista ? 'Playlist' : '') : 'YouTube ↗'));
        riga.appendChild(b); lista.appendChild(riga);
      });
      var n = lista.children.length;
      corpo.appendChild(nodo('div', 'meta', n ? n + (n === 1 ? ' voce' : ' voci') + ' · YouTube' : 'Nuovi brani in arrivo'));
      if (sequenza.length) {
        // pulsante grande: ascolta la selezione dall'inizio
        var tutta = nodo('button', 'ascolta-tutta');
        tutta.type = 'button';
        var tondo = nodo('span', 'tondo', '▶'); tondo.setAttribute('aria-hidden', 'true');
        tutta.appendChild(tondo);
        tutta.appendChild(nodo('b', '', 'Ascolta la selezione'));
        tutta.addEventListener('click', function () { ascolta(sequenza, 0); });
        corpo.appendChild(tutta);
      }
      if (n) corpo.appendChild(lista);
      // stessa selezione su Spotify (colonna "Link Spotify"): si apre nell'app, anche a schermo spento
      if (/^https:\/\/open\.spotify\.com\//.test(s.link_spotify || '')) {
        var sp = nodo('a', 'spotify', 'Ascolta su Spotify ↗');
        sp.href = s.link_spotify; sp.target = '_blank'; sp.rel = 'noopener noreferrer';
        corpo.appendChild(sp);
      }
      if (s.selezione === 'italia') {
        var azioni = nodo('div', 'azioni');
        var artisti = nodo('a', '', 'Scopri band e DJ →'); artisti.href = '/artisti';
        azioni.appendChild(artisti); corpo.appendChild(azioni);
      }
      card.appendChild(corpo);
      elenco.appendChild(card);
    });
    stato.hidden = true;
    // "Ascolta tutto in ordine casuale": tutti i brani delle selezioni, mescolati
    var casuale = document.getElementById('tutto-casuale');
    if (casuale && tutte.length > 1) {
      casuale.hidden = false;
      casuale.addEventListener('click', function () {
        var mix = tutte.slice();
        for (var k = mix.length - 1; k > 0; k--) { var x = Math.floor(Math.random() * (k + 1)); var t = mix[k]; mix[k] = mix[x]; mix[x] = t; }
        mix.casuale = true;
        ascolta(mix, 0);
      });
    }
  }
  lettore.querySelector('button').addEventListener('click', function () {
    ferma();
    if (ultimoBrano) ultimoBrano.focus();
  });
  // "Proponi un brano": salvato e segnalato via email a chi gestisce il sito (functions/api/brani.js)
  var modulo = document.getElementById('proponi');
  if (modulo) {
    var antibot = window.siBallaAntibot ? window.siBallaAntibot.monta(document.getElementById('pAntibot')) : null;
    var esitoP = document.getElementById('pEsito');
    var esitoMsg = function (t, ok) { esitoP.textContent = t; esitoP.className = 'esito-proposta ' + (ok ? 'ok' : 'err'); };
    modulo.addEventListener('submit', function (e) {
      e.preventDefault();
      var titolo = document.getElementById('pTitolo');
      if (!titolo.value.trim()) { esitoMsg('Scrivi almeno il titolo del brano.', false); titolo.focus(); return; }
      if (antibot && antibot.inAttesa()) { esitoMsg('Un attimo: verifica anti-spam in corso, poi premi di nuovo Invia.', false); return; }
      var invia = document.getElementById('pInvia');
      invia.disabled = true;
      fetch('/api/brani', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
        titolo: titolo.value, artista: document.getElementById('pArtista').value, link: document.getElementById('pLink').value,
        velocita: document.getElementById('pVelocita').value, sito_web: document.getElementById('pSito').value,
        turnstile: antibot ? antibot.token() : ''
      }) })
        .then(function (r) { return r.json().catch(function () { return {}; }).then(function (j) { if (!r.ok) throw new Error(j.errore || 'Invio non riuscito'); }); })
        .then(function () { esitoMsg('Grazie! Il brano è arrivato: lo ascoltiamo presto.', true); modulo.reset(); })
        .catch(function (err) { esitoMsg(err.message || 'Invio non riuscito, riprova.', false); })
        .then(function () { invia.disabled = false; if (antibot) antibot.reset(); });
    });
  }

  fetch('musica.json', { cache: 'no-cache' })
    .then(function (r) { if (!r.ok) throw new Error('Musica non disponibile'); return r.json(); })
    .then(function (d) { if (!d || !Array.isArray(d.righe)) throw new Error('Dati non validi'); disegna(d.righe); })
    .catch(function () { stato.textContent = 'Non riesco a caricare la musica. Riprova tra poco.'; });
})();