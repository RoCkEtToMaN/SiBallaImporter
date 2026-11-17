// Pagine Scuole, Artisti, Community: leggono <elenco>.json (prodotto da esporta_sheet.py dalle schede
// dello Sheet con lo stesso nome, o dalla bozza sito/elenchi/<elenco>.csv) e lo mostrano in #contenuto.
// Colonne riconosciute (intestazioni in riga 2, maiuscole e accenti non contano):
//   scuole:    Nome, Città, Regione, Stili, Sito, Instagram, Facebook
//   artisti:   Nome, Tipo (Insegnante / Band / DJ), Città, Stili, Descrizione, Sito, Instagram, Facebook, Consenso
//   community: Nome, Città, Regione, Tipo (Canale / Community), Descrizione, Link
(function () {
  var elenco = document.body.getAttribute("data-elenco");
  var box = document.getElementById("contenuto");
  if (!elenco || !box) return;

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function norm(s) {
    return String(s || "").normalize("NFKD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
  }
  function link(u) {
    u = String(u || "").trim();
    if (!u) return "";
    if (/^www\./i.test(u)) u = "https://" + u;
    if (/^@?[\w.]+$/.test(u) && u.indexOf(".") < 0) u = "https://instagram.com/" + u.replace(/^@/, "");
    return /^https?:\/\//i.test(u) ? u : "";
  }
  function bottone(u, testo, cls) {
    var h = link(u);
    return h ? '<a' + (cls ? ' class="' + cls + '"' : "") + ' href="' + esc(h) + '" target="_blank" rel="noopener">' + testo + "</a>" : "";
  }
  function iniziali(n) {
    var p = String(n).replace(/\(.*?\)/g, "").split(/[\s\-–&]+/).filter(function (w) { return /^[A-Za-zÀ-ÿ0-9]/.test(w) && !/^(e|di|del|della|the|a|asd|aps)$/i.test(w); });
    return (p.slice(0, 2).map(function (w) { return w[0]; }).join("") || "?").toUpperCase();
  }
  function colore(n) { var h = 0; for (var i = 0; i < n.length; i++) h = (h * 31 + n.charCodeAt(i)) >>> 0; return "c" + (h % 5); }
  function tag(s) {
    var a = String(s || "").split(/[,;\/|]/).map(function (x) { return x.trim(); }).filter(Boolean);
    return a.length ? '<div class="tag">' + a.map(function (x) { return "<span>" + esc(x) + "</span>"; }).join("") + "</div>" : "";
  }
  function chips(lista, scelto, attr) {
    return '<div class="chips" role="group">' + lista.map(function (c) {
      return '<button type="button" ' + attr + '="' + esc(c[0]) + '" aria-pressed="' + (c[0] === scelto) + '">' + esc(c[0]) +
        '<span class="n">' + c[1] + "</span></button>";
    }).join("") + "</div>";
  }
  function conta(righe, campo, tutte) {
    var c = {};
    righe.forEach(function (r) { var k = r[campo] || "Altro"; c[k] = (c[k] || 0) + 1; });
    var out = Object.keys(c).sort(function (a, b) { return a === "Altro" ? 1 : b === "Altro" ? -1 : a.localeCompare(b, "it"); })
      .map(function (k) { return [k, c[k]]; });
    return tutte ? [[tutte, righe.length]].concat(out) : out;
  }

  // prossimi eventi per organizzatore (per il link "N prossimi eventi")
  var organizzatori = [];
  function prossimi(nome) {
    var n = norm(nome);
    if (n.length < 4) return 0;
    return organizzatori.filter(function (o) { return o.indexOf(n) >= 0; }).length;
  }

  var dati = [], fonte = "", filtro = "", scelta = "";

  function scuole() {
    var q = norm(filtro);
    var lista = dati.filter(function (r) {
      return (!scelta || scelta === "Tutte" || (r.regione || "Altro") === scelta) &&
        (!q || norm([r.nome, r.citta, r.regione, r.stili].join(" ")).indexOf(q) >= 0);
    });
    return '<div class="cerca"><input type="search" id="filtro" placeholder="Cerca per nome, città, stile…" aria-label="Cerca" value="' + esc(filtro) + '"></div>' +
      chips(conta(dati, "regione", "Tutte"), scelta || "Tutte", "data-scelta") +
      (lista.length ? '<div class="griglia">' + lista.map(function (r) {
        var n = prossimi(r.nome);
        return '<article class="card"><div class="riga"><div class="avatar ' + colore(r.nome) + '">' + esc(iniziali(r.nome)) + "</div><div><h3>" + esc(r.nome) +
          '</h3><div class="meta">' + esc([r.citta, r.regione].filter(Boolean).join(" · ")) + "</div>" + tag(r.stili) + "</div></div>" +
          '<div class="azioni">' + bottone(r.sito, "Sito", "pri") + bottone(r.instagram, "Instagram") + bottone(r.facebook, "Facebook") +
          (n ? '<a href="/?q=' + encodeURIComponent(r.nome) + '">' + n + (n === 1 ? " prossimo evento" : " prossimi eventi") + "</a>" : "") +
          "</div></article>";
      }).join("") + "</div>" : '<p class="vuoto">Nessuna scuola trovata.</p>');
  }

  function artisti() {
    var tipi = conta(dati, "tipo");
    var t = scelta || (tipi[0] && tipi[0][0]);
    var lista = dati.filter(function (r) { return (r.tipo || "Altro") === t; });
    return chips(tipi, t, "data-scelta") + '<div class="griglia">' + lista.map(function (r) {
      var n = prossimi(r.nome);
      return '<article class="card"><div class="riga"><div class="avatar tondo ' + colore(r.nome) + '">' + esc(iniziali(r.nome)) + "</div><div><h3>" + esc(r.nome) +
        '</h3><div class="meta">' + esc(r.citta || "") + '</div><div class="meta2">' + esc(r.descrizione || "") + "</div>" + tag(r.stili) + "</div></div>" +
        '<div class="azioni">' + bottone(r.sito, "Sito", "pri") + bottone(r.instagram, "Instagram") + bottone(r.facebook, "Facebook") +
        (n ? '<a href="/?q=' + encodeURIComponent(r.nome) + '">Dove suona / insegna</a>' : "") + "</div></article>";
    }).join("") + "</div>";
  }

  var WA = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 20l1.3-3.9A8 8 0 1 1 8 19z"/></svg>';
  var PIN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21z"/><circle cx="12" cy="9.5" r="2.5"/></svg>';
  // tipi di gruppo: colore, etichetta del pulsante e spiegazione per la legenda
  var TIPI = {
    canale: { nome: "Canale", azione: "Segui il canale", spiega: "solo annunci, entri subito" },
    community: { nome: "Community", azione: "Chiedi di entrare", spiega: "un amministratore approva chi entra" },
    gruppo: { nome: "Gruppo", azione: "Entra nel gruppo", spiega: "chat aperta tra ballerini" }
  };
  function tipoDi(r) { var t = norm(r.tipo); return /canal/.test(t) ? "canale" : /community/.test(t) || !t ? "community" : "gruppo"; }
  // "savona" -> "Savona" (solo se scritto tutto minuscolo, per non rovinare sigle come "siBalla")
  function maiuscola(s) {
    s = String(s || "").trim();
    return s && s === s.toLowerCase() ? s.replace(/(^|[\s'\-])([a-z\u00e0-\u00ff])/g, function (m, a, b) { return a + b.toUpperCase(); }) : s;
  }
  function ufficiale(r) { return norm(r.nome).indexOf("siballa") === 0; }
  // argomenti: colonna "Argomenti" (o "Tag") se c'\u00e8, pi\u00f9 quelli riconosciuti da nome e descrizione
  var ARGOMENTI = [
    [/lindy/, "Lindy Hop"], [/balboa/, "Balboa"], [/blues/, "Blues"], [/charleston/, "Charleston"], [/boogie/, "Boogie Woogie"],
    [/\bshag\b/, "Shag"], [/partner|compagn[oaie] di ballo/, "Cerco partner"], [/mercatin|vend[oie]|scarpe|abiti|vintage/, "Mercatino"],
    [/passagg|carpool|auto condivis/, "Passaggi"], [/festival|exchange|weekend/, "Festival"], [/\bjam\b|social/, "Social"]
  ];
  function argomenti(r) {
    var a = String(r.argomenti || r.tag || "").split(/[,;\/|]/).map(function (x) { return x.trim(); }).filter(Boolean);
    var testo = norm(r.nome + " " + r.descrizione);
    ARGOMENTI.forEach(function (x) { if (x[0].test(testo) && a.indexOf(x[1]) < 0) a.push(x[1]); });
    return a;
  }
  // eventi dei prossimi 7 giorni per citt\u00e0 (dalla colonna F "Paese, Citt\u00e0, Indirizzo" di eventi.json)
  var luoghiEventi = [];
  function eventiSettimana(citta) {
    var c = " " + norm(citta) + " ";
    if (c.length < 5) return 0;
    function iso(d) { return d.getFullYear() + "-" + ("0" + (d.getMonth() + 1)).slice(-2) + "-" + ("0" + d.getDate()).slice(-2); }
    var oggi = new Date(), da = iso(oggi), a = iso(new Date(oggi.getFullYear(), oggi.getMonth(), oggi.getDate() + 7));
    return luoghiEventi.filter(function (e) { return e.giorno >= da && e.giorno <= a && e.luogo.indexOf(c) >= 0; }).length;
  }
  var QR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><path d="M14 14h3v3M21 14v.01M17 21h4v-4M14 18v3"/></svg>';
  var desktop = window.matchMedia && matchMedia("(hover: hover) and (pointer: fine)").matches;
  function cardGruppo(r, grande) {
    var t = tipoDi(r), desc = norm(r.descrizione) === norm(r.nome) || norm(r.descrizione) === norm(r.citta) ? "" : r.descrizione;
    var arg = argomenti(r), n = r.citta ? eventiSettimana(r.citta) : 0, h = link(r.link);
    return '<article class="card cm-card cm-' + t + (grande ? " cm-grande" : "") + '">' +
      (grande ? '<span class="cm-badge">\u2605 Ufficiale siBalla</span>' : "") +
      '<div class="riga"><div class="wa-ico">' + WA + "</div><div><h4>" + esc(maiuscola(r.nome)) + "</h4>" +
      (r.citta ? '<div class="cm-luogo">' + PIN + esc(maiuscola(r.citta)) + "</div>" : "") + "</div></div>" +
      (desc ? '<p class="cm-desc">' + esc(desc) + "</p>" : "") +
      (arg.length ? '<div class="tag">' + arg.map(function (x) { return "<span>" + esc(x) + "</span>"; }).join("") + "</div>" : "") +
      (n ? '<a class="cm-eventi" href="/?q=' + encodeURIComponent(maiuscola(r.citta)) + '">\u266a ' + n + (n === 1 ? " evento" : " eventi") +
        " a " + esc(maiuscola(r.citta)) + " nei prossimi 7 giorni \u2192</a>" : "") +
      '<div class="cm-piede"><span class="cm-tipo">' + TIPI[t].nome + '</span><span class="cm-bottoni">' +
      (desktop && h ? '<button type="button" class="cm-qr" data-qr="' + esc(h) + '" data-nome="' + esc(maiuscola(r.nome)) +
        '" aria-label="Mostra il QR code per entrare dal telefono" title="Inquadra con il telefono">' + QR + "</button>" : "") +
      bottone(r.link, TIPI[t].azione, "cm-entra") + "</span></div></article>";
  }
  function community() {
    var q = norm(filtro), sc = scelta && scelta !== "Tutte" ? scelta : "";
    var visibili = dati.filter(function (r) {
      return (!sc || maiuscola(r.citta || "Altro") === sc) &&
        (!q || norm([r.nome, r.citta, r.regione, r.descrizione, r.tipo, argomenti(r).join(" ")].join(" ")).indexOf(q) >= 0);
    });
    var perCitta = dati.map(function (r) { return { citta: maiuscola(r.citta || "Altro") }; });
    var ricerca = '<div class="cerca cm-cerca"><input type="search" id="filtro" placeholder="Cerca la tua citt\u00e0, uno stile, un argomento\u2026" aria-label="Cerca" value="' +
      esc(filtro) + '"></div>' + chips(conta(perCitta, "citta", "Tutte"), sc || "Tutte", "data-scelta");
    var citta = {}, regioni = {}, presenti = {};
    dati.forEach(function (r) {
      if (r.citta) citta[norm(r.citta)] = 1;
      if (r.regione) regioni[norm(r.regione)] = 1;
      presenti[tipoDi(r)] = 1;
    });
    function numero(n, uno, tanti) { return "<div><b>" + n + "</b><span>" + (n === 1 ? uno : tanti) + "</span></div>"; }
    var numeri = '<div class="cm-numeri">' + numero(dati.length, "gruppo", "gruppi") +
      numero(Object.keys(citta).length, "citt\u00e0", "citt\u00e0") + numero(Object.keys(regioni).length, "regione", "regioni") + "</div>";
    var legenda = '<ul class="cm-legenda">' + Object.keys(TIPI).filter(function (t) { return presenti[t]; }).map(function (t) {
      return '<li class="cm-' + t + '"><span class="cm-tipo">' + TIPI[t].nome + "</span> " + TIPI[t].spiega + "</li>";
    }).join("") + "</ul>";
    if (!visibili.length) return numeri + legenda + ricerca + '<p class="vuoto">Nessun gruppo trovato' + (q ? " per “" + esc(filtro) + "”" : "") +
      '. <a href="#segnala">La tua città non c\'è? Fondala tu</a></p>';
    var top = visibili.filter(ufficiale), altri = visibili.filter(function (r) { return !ufficiale(r); });
    var evidenza = top.length ? '<section class="sezione"><h2>In evidenza</h2><div class="griglia cm-top">' +
      top.map(function (r) { return cardGruppo(r, true); }).join("") + "</div></section>" : "";
    var perRegione = conta(altri, "regione").map(function (g) {
      var righe = altri.filter(function (r) { return (r.regione || "Altro") === g[0]; }).sort(function (a, b) {
        return norm(a.citta).localeCompare(norm(b.citta), "it") || String(a.nome).localeCompare(String(b.nome), "it");
      });
      return '<section class="sezione"><h2>' + esc(g[0] === "Altro" ? "Altre zone" : g[0]) + ' <span class="c">' + g[1] + "</span></h2>" +
        '<div class="griglia">' + righe.map(function (r) { return cardGruppo(r, false); }).join("") + "</div></section>";
    }).join("");
    return numeri + legenda + ricerca + evidenza + perRegione;
  }

  // QR code (solo su computer): la libreria si carica al primo clic
  var qrLib = null;
  function caricaQr() {
    return qrLib || (qrLib = new Promise(function (ok, ko) {
      var s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/qrcode-generator/1.4.4/qrcode.min.js";
      s.onload = ok; s.onerror = function () { qrLib = null; ko(); };
      document.head.appendChild(s);
    }));
  }
  function mostraQr(url, nome) {
    var d = document.getElementById("cmQr");
    if (!d) {
      d = document.createElement("dialog");
      d.id = "cmQr"; d.className = "cm-qr-dialog";
      d.innerHTML = '<button type="button" class="cm-qr-chiudi" aria-label="Chiudi">&times;</button><h2></h2>' +
        '<div class="cm-qr-img"></div><p>Inquadra il codice con la fotocamera del telefono per aprire WhatsApp.</p><a target="_blank" rel="noopener">Oppure apri il link</a>';
      d.addEventListener("click", function (e) { if (e.target === d || e.target.closest(".cm-qr-chiudi")) d.close(); });
      document.body.appendChild(d);
    }
    d.querySelector("h2").textContent = nome;
    d.querySelector("a").href = url;
    var img = d.querySelector(".cm-qr-img");
    img.textContent = "Caricamento…";
    d.showModal();
    caricaQr().then(function () {
      var qr = window.qrcode(0, "M"); qr.addData(url); qr.make();
      img.innerHTML = qr.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
    }).catch(function () { img.textContent = "QR code non disponibile: usa il link qui sotto."; });
  }
  box.addEventListener("click", function (e) {
    var b = e.target.closest("button[data-qr]");
    if (b) mostraQr(b.getAttribute("data-qr"), b.getAttribute("data-nome"));
  });

  var VUOTO = {
    scuole: "Ancora nessuna scuola. Si aggiungono nella scheda <b>Scuole</b> dello Google Sheet.",
    artisti: "Ancora nessun artista. Si aggiungono nella scheda <b>Artisti</b> dello Google Sheet, solo con il loro consenso (colonna Consenso = sì).",
    community: "Ancora nessun gruppo. Si aggiungono nella scheda <b>Community</b> dello Google Sheet."
  };
  var DISEGNA = { scuole: scuole, artisti: artisti, community: community };

  function disegna() {
    // solo la bozza delle scuole viene dagli organizzatori degli eventi
    var avviso = elenco === "scuole" && fonte === "bozza" && dati.length
      ? '<p class="avviso">Lista provvisoria, ricavata dagli organizzatori degli eventi: da controllare.</p>' : "";
    box.innerHTML = avviso + (dati.length ? DISEGNA[elenco]() : '<p class="box">' + VUOTO[elenco] + "</p>");
  }

  box.addEventListener("click", function (e) {
    var b = e.target.closest("button[data-scelta]");
    if (!b) return;
    scelta = b.getAttribute("data-scelta");
    disegna();
  });
  box.addEventListener("input", function (e) {
    if (e.target.id !== "filtro") return;
    filtro = e.target.value;
    var pos = e.target.selectionStart;
    disegna();
    var f = document.getElementById("filtro");
    if (f) { f.focus(); try { f.setSelectionRange(pos, pos); } catch (x) {} }
  });

  function json(url) { return fetch(url, { cache: "no-cache" }).then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); }); }
  var eventi = json("eventi.json").catch(function () { return null; });
  function caricaElenco() {
    if (elenco !== 'community') return json(elenco + '.json');
    return Promise.all([json('community.json'), json('/api/community')]).then(function (risultati) {
      var base = risultati[0], aggiornamenti = risultati[1];
      var esclusi = new Set(aggiornamenti.esclusi || []), visti = new Set();
      base.righe = (base.righe || []).concat(aggiornamenti.righe || []).filter(function (r) {
        if (esclusi.has(r.link) || visti.has(r.link)) return false;
        visti.add(r.link); return true;
      });
      return base;
    });
  }
  caricaElenco()
    .then(function (d) {
      dati = (d.righe || []).slice().sort(function (a, b) { return String(a.nome).localeCompare(String(b.nome), "it"); });
      fonte = d.fonte || "";
      return eventi;
    })
    .then(function (ev) {
      if (ev && ev.fogli) ev.fogli.forEach(function (f) {
        f.eventi.forEach(function (e) {
          organizzatori.push(norm((e.celle[6] || "") + " " + (e.celle[4] || "")));
          luoghiEventi.push({ giorno: String(e.inizio || "").slice(0, 10), luogo: " " + norm(e.celle[5]) + " " });
        });
      });
      disegna();
    })
    .catch(function () { box.innerHTML = '<p class="vuoto">Impossibile caricare i dati, riprova più tardi.</p>'; });
})();
