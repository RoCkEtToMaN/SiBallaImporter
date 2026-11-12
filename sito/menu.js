// menu ☰ comune a tutte le pagine: aggiunge il pulsante nell'intestazione (.hero) e il pannello laterale.
// Per aggiungere/togliere una sezione basta modificare VOCI (pronta: false mostra l'etichetta "presto").
(function () {
  var VOCI = [
    { url: "/", t: "Eventi", d: "Serate e workshop", pronta: true, pubblica: true,
      ico: '<path d="M7 3v3M17 3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"/>' },
    { url: "/storia", t: "Storia del ballo", d: "Dal Savoy a oggi", pronta: true,
      ico: '<path d="M4 5a2 2 0 0 1 2-2h12v16H6a2 2 0 0 0-2 2zM4 21V5M8 7h6"/>' },
    { url: "/rubriche", t: "Rubriche", d: "La mossa della settimana e altro", pronta: true,
      ico: '<path d="M12 3l2.6 5.6 6 .7-4.5 4.1 1.2 6L12 16.4 6.7 19.4l1.2-6L3.4 9.3l6-.7z"/>' },
    { url: "/scuole", t: "Scuole", d: "Dove imparare, per regione", pronta: true,
      ico: '<path d="M3 9l9-5 9 5-9 5zM7 11v5c3 2 7 2 10 0v-5"/>' },
    { url: "/musica", t: "Musica", d: "Trova il ritmo per ballare", pronta: true,
      ico: '<circle cx="6" cy="18" r="3"/><path d="M9 18V5l12-2v13"/><circle cx="18" cy="16" r="3"/><path d="M9 9l12-2"/>' },
    { url: "/artisti", t: "Artisti", d: "Insegnanti, band e DJ", pronta: true,
      ico: '<circle cx="9" cy="18" r="2.5"/><circle cx="18" cy="16" r="2.5"/><path d="M11.5 18V5l9-2v13"/>' },
    { url: "/community", t: "Community WhatsApp", d: "I gruppi della tua città", pronta: true,
      ico: '<path d="M4 20l1.3-3.9A8 8 0 1 1 8 19z"/>' },
    { url: "/bacheca", t: "Bacheca", d: "Passaggi e partner di ballo", pronta: true, pubblica: true,
      ico: '<path d="M9 3h6l-1 6 4 4H6l4-4zM12 13v8"/>' },
    { url: "/novita", t: "Novità", d: "Cosa c'è di nuovo su siBalla", pronta: true, pubblica: true,
      ico: '<path d="M12 3l1.8 4.7L18.5 9.5l-4.7 1.8L12 16l-1.8-4.7L5.5 9.5l4.7-1.8zM19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"/>' },
    { url: "/app", t: "App mobile", d: "siBalla sul telefono", pronta: true,
      ico: '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M11 18.5h2"/>' }
  ];
  function svg(p, w) {
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="' + (w || 1.9) +
      '" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + p + "</svg>";
  }
  // Sul sito pubblico il menu mostra solo le voci con "pubblica: true" (per ora Eventi e Bacheca);
  // nelle versioni di prova (anteprima.siballa.pages.dev, file locali) si vedono tutte.
  // Per pubblicare un'altra sezione basta aggiungere "pubblica: true" alla sua voce;
  // con MENU_PUBBLICO = false il menu sparisce del tutto dal sito pubblico.
  var MENU_PUBBLICO = true;
  var prova = /\.siballa\.pages\.dev$/.test(location.hostname) || location.protocol === "file:" ||
    /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
  if (!MENU_PUBBLICO && !prova) return;
  if (!prova) VOCI = VOCI.filter(function (v) { return v.pubblica; });
  var hero = document.querySelector(".hero");
  if (!hero) return;
  var qui = location.pathname.replace(/\/index(\.html)?$/, "/").replace(/\.html$/, "").replace(/(.)\/$/, "$1") || "/";

  var btn = document.createElement("button");
  btn.type = "button";
  btn.className = "menu-btn";
  btn.setAttribute("aria-expanded", "false");
  btn.setAttribute("aria-controls", "pannello");
  btn.innerHTML = svg('<path d="M4 7h16M4 12h16M4 17h16"/>', 2.2) + '<span class="lbl">Menu</span>';
  hero.appendChild(btn);

  var velo = document.createElement("div");
  velo.className = "velo";
  var pan = document.createElement("aside");
  pan.className = "pannello";
  pan.id = "pannello";
  pan.setAttribute("aria-label", "Menu");
  pan.innerHTML = '<div class="pannello-top"><b>siBalla</b><button type="button" class="chiudi" aria-label="Chiudi menu">✕</button></div><nav>' +
    VOCI.map(function (v, i) {
      return (i === 1 ? '<div class="sep"></div>' : "") +
        '<a href="' + v.url + '"' + (v.url === qui ? ' aria-current="page"' : "") + '><span class="ico">' + svg(v.ico) + "</span>" +
        "<span>" + v.t + "<small>" + v.d + "</small></span>" + (v.pronta ? "" : '<span class="presto">presto</span>') + "</a>";
    }).join("") + "</nav>";
  document.body.appendChild(velo);
  document.body.appendChild(pan);

  function apri() {
    document.body.classList.add("menu-aperto");
    btn.setAttribute("aria-expanded", "true");
    var a = pan.querySelector("a");
    if (a) a.focus();
  }
  function chiudi() {
    if (!document.body.classList.contains("menu-aperto")) return;
    document.body.classList.remove("menu-aperto");
    btn.setAttribute("aria-expanded", "false");
    btn.focus();
  }
  btn.addEventListener("click", apri);
  velo.addEventListener("click", chiudi);
  pan.querySelector(".chiudi").addEventListener("click", chiudi);
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") chiudi(); });
  // la voce della pagina in cui sei già: chiude solo il menu
  pan.addEventListener("click", function (e) {
    var a = e.target.closest("a[aria-current]");
    if (a) { e.preventDefault(); chiudi(); }
  });

  // ---- novità della bacheca: pallino sul ☰, "N nuovi" accanto alla voce, invito nella home, riga sulle schede ----
  // Ogni segnale è evidenziato (giallo) finché non ci clicchi una volta, poi torna normale; si riaccende
  // solo quando arriva un annuncio nuovo. Tutto ricordato solo in questo browser (localStorage), per id dell'annuncio:
  //   VISTO          aprendo /bacheca (o cliccando la voce del menu)  -> toglie "N nuovi" dal menu
  //   MENU_APERTO    aprendo il menu ☰                                 -> toglie il pallino dal ☰
  //   INVITO_VISTO   cliccando il link dell'invito nella home          -> invito non più giallo
  //   INVITO_CHIUSO  cliccando ✕ sull'invito                           -> invito nascosto
  //   EVENTI         cliccando la riga su una scheda evento            -> quella riga non più gialla
  var VISTO = "siballa-bacheca-visto", MENU_APERTO = "siballa-bacheca-menu", INVITO_VISTO = "siballa-bacheca-invito-visto",
    INVITO_CHIUSO = "siballa-bacheca-invito-chiuso", EVENTI = "siballa-bacheca-eventi";
  function leggi(k) { try { return +localStorage.getItem(k) || 0; } catch (e) { return 0; } }
  function scrivi(k, v) { try { localStorage.setItem(k, String(v)); } catch (e) {} }
  function leggiEventi() { try { return JSON.parse(localStorage.getItem(EVENTI) || "{}") || {}; } catch (e) { return {}; } }
  function scriviEvento(k, id) {
    var m = leggiEventi();
    m[k] = id;
    try { localStorage.setItem(EVENTI, JSON.stringify(m)); } catch (e) {}
  }
  function quanti(n, uno, tanti) { return n + " " + (n === 1 ? uno : tanti); }
  function norm(s) { return String(s || "").normalize("NFKD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim(); }

  // sulla scheda di ogni evento della home: "🚗 2 cercano un passaggio · 💃 1 cerca partner" (link alla bacheca filtrata).
  // Un annuncio vale per l'evento se il nome scritto nel campo "Evento" coincide con il titolo o ne è una parte (min. 5 lettere).
  var FRASI = {
    "passaggio|offro": ["🚗", "offre un passaggio", "offrono un passaggio"],
    "passaggio|cerco": ["🚗", "cerca un passaggio", "cercano un passaggio"],
    "partner|cerco": ["💃", "cerca partner", "cercano partner"],
    "partner|offro": ["💃", "si propone come partner", "si propongono come partner"],
    "alloggio|cerco": ["🛏️", "cerca alloggio", "cercano alloggio"],
    "alloggio|offro": ["🛏️", "offre alloggio", "offrono alloggio"]
  };
  function segnaEventi(annunci) {
    var conEvento = annunci.filter(function (a) { return norm(a.evento).length >= 5; });
    var lista = document.getElementById("lista");
    if (!conEvento.length || !lista) return;
    function segna() {
      [].forEach.call(lista.querySelectorAll("article.ev"), function (art) {
        if (art.querySelector(".bacheca-ev")) return;
        var h = art.querySelector("h3"), corpo = art.querySelector(".corpo");
        if (!h || !corpo) return;
        var titolo = norm(h.textContent), conta = {}, nomeEvento = "", ultimoEv = 0;
        conEvento.forEach(function (a) {
          var e = norm(a.evento);
          if (e === titolo || titolo.indexOf(e) >= 0 || (e.indexOf(titolo) >= 0 && titolo.length >= 5)) {
            var k = a.categoria + "|" + a.tipo;
            if (FRASI[k]) { conta[k] = (conta[k] || 0) + 1; nomeEvento = nomeEvento || a.evento; ultimoEv = Math.max(ultimoEv, a.id || 0); }
          }
        });
        var parti = Object.keys(FRASI).filter(function (k) { return conta[k]; }).map(function (k) {
          return FRASI[k][0] + " " + quanti(conta[k], FRASI[k][1], FRASI[k][2]);
        });
        if (!parti.length) return;
        var link = document.createElement("a");
        link.className = "bacheca-ev" + (ultimoEv > (leggiEventi()[titolo] || 0) ? " evid" : "");
        link.href = "/bacheca?q=" + encodeURIComponent(nomeEvento);
        link.textContent = parti.join(" · ") + " →";
        link.addEventListener("click", function () { scriviEvento(titolo, ultimoEv); link.classList.remove("evid"); });
        corpo.appendChild(link);
      });
    }
    segna();
    // la lista si ridisegna a ogni filtro o ricerca: rimetto le etichette
    new MutationObserver(segna).observe(lista, { childList: true });
  }
  if (location.protocol === "file:") return;
  fetch("/api/bacheca")
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (d) {
      var annunci = (d && d.annunci) || [];
      var ultimo = annunci.reduce(function (m, a) { return Math.max(m, a.id || 0); }, 0);
      if (qui === "/bacheca") { scrivi(VISTO, ultimo); return; }
      var visto = leggi(VISTO);
      var nuovi = annunci.filter(function (a) { return (a.id || 0) > visto; }).length;
      // accanto alla voce: "N nuovi" in giallo se ci sono annunci non visti, altrimenti il totale in grigio
      var voce = pan.querySelector('a[href="/bacheca"]');
      if (nuovi) {
        // pallino sul ☰ finché non apri il menu una volta
        if (ultimo > leggi(MENU_APERTO)) {
          btn.classList.add("novita");
          btn.setAttribute("aria-label", "Menu: " + quanti(nuovi, "annuncio nuovo", "annunci nuovi") + " in bacheca");
          btn.addEventListener("click", function () {
            scrivi(MENU_APERTO, ultimo);
            btn.classList.remove("novita");
            btn.removeAttribute("aria-label");
          });
        }
        if (voce) {
          voce.insertAdjacentHTML("beforeend", '<span class="nuovo">' + quanti(nuovi, "nuovo", "nuovi") + "</span>");
          voce.addEventListener("click", function () { scrivi(VISTO, ultimo); });
        }
      } else if (voce && annunci.length) {
        voce.insertAdjacentHTML("beforeend", '<span class="totale" title="Annunci in bacheca">' + annunci.length + "</span>");
      }
      if (qui === "/") segnaEventi(annunci);
      // invito nella home, sotto la ricerca: si chiude con ✕ e torna solo quando arrivano annunci nuovi
      var bar = qui === "/" && document.querySelector(".bar");
      if (!bar || !annunci.length || leggi(INVITO_CHIUSO) >= ultimo) return;
      var inv = document.createElement("div");
      // giallo finché non clicchi il link una volta (o finché non arrivano annunci nuovi)
      inv.className = "invito-bacheca" + (ultimo > leggi(INVITO_VISTO) ? " evid" : "");
      inv.innerHTML = '<span class="pin" aria-hidden="true">📌</span><span class="testo">Cerchi un passaggio o un partner per un festival? ' +
        '<a href="/bacheca">' + quanti(annunci.length, "annuncio", "annunci") + " in bacheca →</a></span>" +
        (nuovi && ultimo > leggi(INVITO_VISTO) ? '<span class="nuovo">' + quanti(nuovi, "nuovo", "nuovi") + "</span>" : "") +
        '<button type="button" class="chiudi-invito" aria-label="Nascondi">✕</button>';
      bar.parentNode.insertBefore(inv, bar.nextSibling);
      inv.querySelector("a").addEventListener("click", function () {
        scrivi(INVITO_VISTO, ultimo);
        inv.classList.remove("evid");
        var n = inv.querySelector(".nuovo");
        if (n) n.remove();
      });
      inv.querySelector(".chiudi-invito").addEventListener("click", function () {
        scrivi(INVITO_CHIUSO, ultimo);
        inv.remove();
      });
    })
    .catch(function () {});
})();
