// contatore visite anonimo, incluso in tutte le pagine (vedi functions/api/visita.js).
// Niente cookie: in localStorage restano solo la data dell'ultima visita (per contare i visitatori unici
// senza identificarli) e, sui dispositivi del gestore, il segno "non contare" (siballa_noconta).
// Le versioni di prova (es. anteprima.siballa.pages.dev) non contano.
(function () {
  if (location.protocol !== "https:" || /\.siballa\.pages\.dev$/.test(location.hostname)) return;
  function leggi(k) { try { return localStorage.getItem(k) || ""; } catch (e) { return ""; } }
  function scrivi(k, v) { try { if (v) localStorage.setItem(k, v); else localStorage.removeItem(k); } catch (e) {} }
  var par = new URLSearchParams(location.search);
  // ?noconta=1 esclude questo browser dal conteggio, ?noconta=0 lo riattiva
  if (par.has("noconta")) scrivi("siballa_noconta", par.get("noconta") === "0" ? "" : "1");
  if (leggi("siballa_noconta") === "1") return;

  function invia(dati) { try { navigator.sendBeacon("/api/visita", JSON.stringify(dati)); } catch (e) {} }
  var pagina = location.pathname.replace(/\/index(\.html)?$/, "/").replace(/\.html$/, "").replace(/(.)\/$/, "$1") || "/";
  var oggi = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Rome" }).format(new Date());
  var ultima = leggi("siballa_ultima");
  scrivi("siballa_ultima", oggi);
  var ref = "";
  try { if (document.referrer) { var h = new URL(document.referrer).hostname; if (h !== location.hostname) ref = h; } } catch (e) {}
  invia({
    t: "vista", pagina: pagina, fonte: par.get("utm_source") || ref,
    nuovoOggi: ultima !== oggi, nuovoMese: ultima.slice(0, 7) !== oggi.slice(0, 7), nuovo: !ultima
  });

  // click sui link esterni: per gli eventi il nome dell'evento + "Info"/"Mappa", altrove il testo del link
  document.addEventListener("click", function (e) {
    var a = e.target && e.target.closest && e.target.closest("a[href]");
    if (!a || a.host === location.host || !/^https?:$/.test(a.protocol)) return;
    var ev = a.closest(".ev"), h3 = ev && ev.querySelector("h3");
    var testo = (a.textContent || "").trim();
    invia(h3 ? { t: "click", cosa: h3.textContent, tipo: testo }
             : { t: "click", cosa: (testo || a.hostname) + " (" + pagina + ")", tipo: a.hostname.replace(/^www\./, "") });
  }, true);
})();
