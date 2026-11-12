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
})();
