// pagine non ancora pubbliche: sul sito pubblico (siballa.pages.dev) rimandano agli Eventi,
// nelle versioni di prova (anteprima.siballa.pages.dev, file locali) si aprono normalmente.
// Per pubblicare una pagina basta togliere dal suo <head> la riga <script src="riservata.js"></script>
// (e aggiungere "pubblica: true" alla sua voce in menu.js).
(function () {
  var prova = /\.siballa\.pages\.dev$/.test(location.hostname) || location.protocol === "file:" ||
    /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
  if (!prova) location.replace("/");
})();
