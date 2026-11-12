// menu ☰ comune a tutte le pagine: aggiunge il pulsante nell'intestazione (.hero) e il pannello laterale.
// Per aggiungere/togliere una sezione basta modificare VOCI (pronta: false mostra l'etichetta "presto").
(function () {
  var VOCI = [
    { url: "/", t: "Eventi", d: "Serate e workshop", pronta: true,
      ico: '<path d="M7 3v3M17 3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"/>' },
    { url: "/storia", t: "Storia del ballo", d: "Dal Savoy a oggi", pronta: true,
      ico: '<path d="M4 5a2 2 0 0 1 2-2h12v16H6a2 2 0 0 0-2 2zM4 21V5M8 7h6"/>' },
    { url: "/scuole", t: "Scuole", d: "Dove imparare, per regione", pronta: true,
      ico: '<path d="M3 9l9-5 9 5-9 5zM7 11v5c3 2 7 2 10 0v-5"/>' },
    { url: "/artisti", t: "Artisti", d: "Insegnanti, band e DJ", pronta: true,
      ico: '<circle cx="9" cy="18" r="2.5"/><circle cx="18" cy="16" r="2.5"/><path d="M11.5 18V5l9-2v13"/>' },
    { url: "/community", t: "Community WhatsApp", d: "I gruppi della tua città", pronta: true,
      ico: '<path d="M4 20l1.3-3.9A8 8 0 1 1 8 19z"/>' },
    { url: "/bacheca", t: "Bacheca", d: "Passaggi e partner di ballo", pronta: true,
      ico: '<path d="M9 3h6l-1 6 4 4H6l4-4zM12 13v8"/>' },
    { url: "/app", t: "App mobile", d: "siBalla sul telefono", pronta: true,
      ico: '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M11 18.5h2"/>' }
  ];
  function svg(p, w) {
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="' + (w || 1.9) +
      '" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + p + "</svg>";
  }
  // per ora il menu si vede solo nelle versioni di prova (anteprima.siballa.pages.dev, file locali):
  // sul sito pubblico resta nascosto. Per pubblicarlo basta mettere MENU_PUBBLICO = true.
  var MENU_PUBBLICO = false;
  var prova = /\.siballa\.pages\.dev$/.test(location.hostname) || location.protocol === "file:" ||
    /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
  if (!MENU_PUBBLICO && !prova) return;
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

  // ---- novità della bacheca: pallino sul ☰, "N nuovi" accanto alla voce, invito nella home ----
  // "nuovo" = annuncio con id più alto dell'ultimo visto aprendo /bacheca (ricordato solo in questo browser).
  var VISTO = "siballa-bacheca-visto", INVITO_CHIUSO = "siballa-bacheca-invito-chiuso";
  function leggi(k) { try { return +localStorage.getItem(k) || 0; } catch (e) { return 0; } }
  function scrivi(k, v) { try { localStorage.setItem(k, String(v)); } catch (e) {} }
  function quanti(n, uno, tanti) { return n + " " + (n === 1 ? uno : tanti); }
  function norm(s) { return String(s || "").normalize("NFKD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim(); }

  // sulla scheda di ogni evento della home: "🚗 2 cercano un passaggio · 💃 1 cerca partner" (link alla bacheca filtrata).
  // Un annuncio vale per l'evento se il nome scritto nel campo "Evento" coincide con il titolo o ne è una parte (min. 5 lettere).
  var FRASI = {
    "passaggio|offro": ["🚗", "offre un passaggio", "offrono un passaggio"],
    "passaggio|cerco": ["🚗", "cerca un passaggio", "cercano un passaggio"],
    "partner|cerco": ["💃", "cerca partner", "cercano partner"],
    "partner|offro": ["💃", "si propone come partner", "si propongono come partner"]
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
        var titolo = norm(h.textContent), conta = {}, nomeEvento = "";
        conEvento.forEach(function (a) {
          var e = norm(a.evento);
          if (e === titolo || titolo.indexOf(e) >= 0 || (e.indexOf(titolo) >= 0 && titolo.length >= 5)) {
            var k = a.categoria + "|" + a.tipo;
            if (FRASI[k]) { conta[k] = (conta[k] || 0) + 1; nomeEvento = nomeEvento || a.evento; }
          }
        });
        var parti = Object.keys(FRASI).filter(function (k) { return conta[k]; }).map(function (k) {
          return FRASI[k][0] + " " + quanti(conta[k], FRASI[k][1], FRASI[k][2]);
        });
        if (!parti.length) return;
        var link = document.createElement("a");
        link.className = "bacheca-ev";
        link.href = "/bacheca?q=" + encodeURIComponent(nomeEvento);
        link.textContent = parti.join(" · ") + " →";
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
        btn.classList.add("novita");
        btn.setAttribute("aria-label", "Menu: " + quanti(nuovi, "annuncio nuovo", "annunci nuovi") + " in bacheca");
        if (voce) voce.insertAdjacentHTML("beforeend", '<span class="nuovo">' + quanti(nuovi, "nuovo", "nuovi") + "</span>");
      } else if (voce && annunci.length) {
        voce.insertAdjacentHTML("beforeend", '<span class="totale" title="Annunci in bacheca">' + annunci.length + "</span>");
      }
      if (qui === "/") segnaEventi(annunci);
      // invito nella home, sotto la ricerca: si chiude con ✕ e torna solo quando arrivano annunci nuovi
      var bar = qui === "/" && document.querySelector(".bar");
      if (!bar || !annunci.length || leggi(INVITO_CHIUSO) >= ultimo) return;
      var inv = document.createElement("div");
      inv.className = "invito-bacheca";
      inv.innerHTML = '<span class="pin" aria-hidden="true">📌</span><span class="testo">Cerchi un passaggio o un partner per un festival? ' +
        '<a href="/bacheca">' + quanti(annunci.length, "annuncio", "annunci") + " in bacheca →</a></span>" +
        (nuovi ? '<span class="nuovo">' + quanti(nuovi, "nuovo", "nuovi") + "</span>" : "") +
        '<button type="button" class="chiudi-invito" aria-label="Nascondi">✕</button>';
      bar.parentNode.insertBefore(inv, bar.nextSibling);
      inv.querySelector(".chiudi-invito").addEventListener("click", function () {
        scrivi(INVITO_CHIUSO, ultimo);
        inv.remove();
      });
    })
    .catch(function () {});
})();
