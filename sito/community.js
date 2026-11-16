(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var dialog = $('communityDialog'), form = $('communityForm'), invia = $('inviaCommunity');
  var antibot = window.siBallaAntibot.monta($('cAntibot'));
  function esito(testo, stato) { $('cEsito').textContent = testo; $('cEsito').dataset.stato = stato; }
  $('segnalaCommunity').addEventListener('click', function () { dialog.showModal(); });
  $('chiudiCommunity').addEventListener('click', function () { dialog.close(); });
  dialog.addEventListener('close', function () { $('segnalaCommunity').focus(); });
  form.addEventListener('submit', async function (e) {
    e.preventDefault();
    if (invia.disabled || !form.reportValidity()) return;
    if (antibot.inAttesa()) { esito('Verifica anti-spam in corso: attendi un attimo e riprova.', 'errore'); return; }
    invia.disabled = true;
    esito('Invio in corso...', 'attesa');
    try {
      var dati = Object.fromEntries(new FormData(form));
      dati.turnstile = antibot.token();
      var r = await fetch('/api/community', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(dati) });
      var risposta = await r.json();
      if (!r.ok || !risposta.ok) throw new Error(risposta.errore || 'Invio non riuscito. Riprova.');
      form.reset();
      esito('Grazie! La richiesta ? stata ricevuta. La valuteremo e potremo contattarti via email.', 'ok');
    } catch (err) {
      esito(err.message || 'Invio non riuscito. Riprova pi? tardi.', 'errore');
    } finally { invia.disabled = false; antibot.reset(); }
  });
})();
