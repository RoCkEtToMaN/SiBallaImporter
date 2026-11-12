// Riquadro anti-bot Turnstile (Cloudflare) nei moduli: clandestini, annunci della bacheca, messaggi.
// Si attiva solo se /api/antibot risponde con una Site key (cioè se su Cloudflare ci sono le due chiavi):
// altrimenti non carica niente e i moduli funzionano come sempre.
// Uso: var ab = siBallaAntibot.monta(elemento); ... { turnstile: ab.token() } ... ab.reset() dopo ogni invio.
(function () {
  var pronto = fetch("/api/antibot", { cache: "no-cache" })
    .then(function (r) { return r.ok ? r.json() : {}; })
    .then(function (d) {
      if (!d || !d.sitekey) return null;
      return new Promise(function (ok) {
        window.siBallaTurnstilePronto = function () { ok(d.sitekey); };
        var s = document.createElement("script");
        s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?onload=siBallaTurnstilePronto&render=explicit";
        s.async = true;
        s.onerror = function () { ok(null); };
        document.head.appendChild(s);
      });
    })
    .catch(function () { return null; });

  function monta(el) {
    var stato = { token: "", id: null, attivo: false };
    pronto.then(function (sitekey) {
      if (!sitekey || !el || !el.isConnected || !window.turnstile) return;
      stato.attivo = true;
      // "interaction-only": di solito invisibile; chiede un clic solo se Cloudflare ha dei dubbi
      stato.id = window.turnstile.render(el, {
        sitekey: sitekey, theme: "dark", size: "flexible", appearance: "interaction-only", language: "it",
        callback: function (t) { stato.token = t; },
        "expired-callback": function () { stato.token = ""; },
        "error-callback": function () { stato.token = ""; }
      });
    });
    return {
      token: function () { return stato.token; },
      // true se il controllo è acceso ma la verifica non è ancora finita
      inAttesa: function () { return stato.attivo && !stato.token; },
      reset: function () {
        stato.token = "";
        if (stato.id != null && window.turnstile) { try { window.turnstile.reset(stato.id); } catch (e) {} }
      }
    };
  }
  window.siBallaAntibot = { monta: monta };
})();
