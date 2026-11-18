// pulsante "▶ Come funziona" comune: apre il video how-to della pagina (sito/video/howto-<pagina>.mp4).
// I video si rifanno con sito/video/copioni.py; una pagina senza video in VIDEO non mostra il pulsante.
(function () {
  var VIDEO = ["home", "storia", "rubriche", "scuole", "musica", "2026", "app", "bacheca", "community", "novita"];
  var pagina = location.pathname.replace(/^.*\//, "").replace(/\.html$/, "") || "home";
  if (pagina === "index") pagina = "home";
  if (VIDEO.indexOf(pagina) < 0) return;

  var st = document.createElement("style");
  st.textContent =
    "#hwBtn{position:fixed;right:16px;bottom:16px;z-index:50;border:0;border-radius:999px;padding:11px 16px;" +
    "background:#ffd21f;color:#111;font:700 15px system-ui,-apple-system,Segoe UI,sans-serif;" +
    "box-shadow:0 6px 20px rgba(0,0,0,.4);cursor:pointer}" +
    "#hwDlg{border:0;padding:0;background:transparent;max-width:min(92vw,420px);overflow:visible}" +
    "#hwDlg::backdrop{background:rgba(0,0,0,.78)}" +
    "#hwDlg video{display:block;width:100%;max-height:85vh;border-radius:18px;background:#000}" +
    "#hwDlg .chiudi-hw{position:absolute;top:8px;right:8px;border:0;border-radius:50%;width:36px;height:36px;" +
    "background:rgba(0,0,0,.6);color:#fff;font-size:20px;line-height:36px;cursor:pointer}";
  document.head.appendChild(st);

  var btn = document.createElement("button");
  btn.type = "button";
  btn.id = "hwBtn";
  btn.innerHTML = "&#9654; Come funziona";
  var dlg = document.createElement("dialog");
  dlg.id = "hwDlg";
  dlg.setAttribute("aria-label", "Video: come funziona questa pagina");
  dlg.innerHTML = '<button type="button" class="chiudi-hw" aria-label="Chiudi">&times;</button>' +
    '<video src="video/howto-' + pagina + '.mp4" poster="video/howto-' + pagina + '.jpg" controls playsinline muted preload="none"></video>';
  document.body.appendChild(btn);
  document.body.appendChild(dlg);

  // statistiche (tabella "Come funziona" in /statistiche): aperture del video e visioni fino in fondo.
  // window.siballaConta esiste solo dove conta.js conta (non su anteprima né con ?noconta).
  function conta(cosa) { if (window.siballaConta) window.siballaConta({ t: "howto", pagina: pagina, cosa: cosa }); }
  var finito = false;

  var v = dlg.querySelector("video");
  btn.addEventListener("click", function () {
    dlg.showModal();
    v.currentTime = 0;
    v.play().catch(function () {});
    finito = false;
    conta("aperto");
  });
  v.addEventListener("ended", function () { if (!finito) { finito = true; conta("finito"); } });
  dlg.querySelector(".chiudi-hw").addEventListener("click", function () { dlg.close(); });
  dlg.addEventListener("click", function (e) { if (e.target === dlg) dlg.close(); });
  dlg.addEventListener("close", function () { v.pause(); });
})();
