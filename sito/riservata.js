// pagine non ancora pubbliche: sul sito pubblico (siballa.pages.dev) rimandano agli Eventi,
// nelle versioni di prova (anteprima.siballa.pages.dev, file locali) si aprono normalmente.
// Per pubblicare una pagina basta togliere dal suo <head> la riga <script src="riservata.js"></script>
// (e aggiungere "pubblica: true" alla sua voce in menu.js).
// Pubblicazione programmata: <script src="riservata.js" data-dal="2026-12-10"></script> = riservata fino al
// giorno prima, pubblica da quel giorno (data italiana); in menu.js la voce avrà pubblica: "2026-12-10".
(function () {
  var prova = /\.siballa\.pages\.dev$/.test(location.hostname) || location.protocol === "file:" ||
    /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
  if (prova) return;
  var dal = document.currentScript && document.currentScript.getAttribute("data-dal");
  if (dal) {
    var oggi = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Rome" }).format(new Date());
    if (oggi >= dal) return;
  }
  location.replace("/");
})();
