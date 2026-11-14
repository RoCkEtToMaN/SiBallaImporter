// Riga in fondo a ogni pagina con la versione pubblicata, letta da versione.json
// (lo scrive il workflow "Sito pubblico" a ogni pubblicazione: data del commit, codice del commit, ramo).
// Sulle versioni di prova (<ramo>.siballa.pages.dev) aggiunge l'etichetta "versione di prova".
// Riporta anche la citazione dell'icona dell'app (Flaticon, licenza gratuita: va citato l'autore).
(function () {
  // Autore e pagina dell'icona su Flaticon: finché "autore" è vuoto la citazione non compare.
  var ICONA = { autore: "Magnific", pagina: "" };
  var prova = /\.siballa\.pages\.dev$/.test(location.hostname);
  var piede = document.createElement("footer");
  piede.className = "piede-versione";
  piede.style.cssText = "max-width:1200px;margin:0 auto;padding:18px 16px 28px;text-align:center;font-size:12px;" +
    "color:var(--muted,#9A9AA8);opacity:.8";
  fetch("versione.json", { cache: "no-cache" })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (v) {
      if (!v || !v.versione) return;
      piede.innerHTML = "siBalla · versione " + esc(v.versione) + (v.commit ? " · " + esc(v.commit) : "") +
        (prova ? ' · <span style="color:#FF9F43;font-weight:700">versione di prova' + (v.ramo ? " (" + esc(v.ramo) + ")" : "") + "</span>" : "") +
        (ICONA.autore ? ' · Icona di <a href="' + esc(ICONA.pagina || "https://www.flaticon.com") + '" target="_blank" rel="noopener" style="color:inherit">' +
          esc(ICONA.autore) + '</a> da <a href="https://www.flaticon.com" target="_blank" rel="noopener" style="color:inherit">Flaticon</a>' : "");
      if (v.pubblicata) piede.title = "Pubblicata il " + new Date(v.pubblicata).toLocaleString("it-IT");
      document.body.appendChild(piede);
    })
    .catch(function () {});
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return "&#" + c.charCodeAt(0) + ";"; }); }
})();
