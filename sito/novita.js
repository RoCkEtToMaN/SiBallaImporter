// Novità di siBalla: elenco delle versioni (la più recente in cima) + avviso "✨ Novità" che lampeggia nell'intestazione.
// L'avviso compare a chi non ha ancora visto l'ultima versione: sparisce quando apre /novita (ricordato in questo browser).
// Per annunciare una versione nuova basta aggiungere un elemento IN CIMA a NOVITA: l'avviso si riaccende per tutti.
// I testi possono contenere <b>…</b>: sono scritti da noi, non dagli utenti.
// Numero di versione: MAGGIORE.MINORE.CORREZIONE.BUILD (es. 1.2.0.0 = funzioni nuove, 1.1.1.0 = piccole correzioni).
// Il workflow del sito legge la prima "versione:" di questo file e la mostra in fondo a ogni pagina (piede.js).
(function () {
  var NOVITA = [
    {
      versione: "1.2.0.0",
      data: "Novembre 2026",
      titolo: "Le community della tua citt\u00e0",
      intro: "Scopri i gruppi swing per regione e citt\u00e0 e segnala la tua community.",
      voci: [{ tipo: "Nuovo", titolo: "Community WhatsApp", link: "/community", punti: [
        "Gruppi organizzati per regione e citt\u00e0.",
        "Proponi una community dal modulo: la valutiamo prima di pubblicarla. La tua email di contatto resta privata."
      ] }]
    },
    {
      versione: "1.1.2.0",
      data: "Novembre 2026",
      titolo: "Community siBalla",
      intro: "Il nome della community ora indica Genova e dintorni.",
      voci: []
    },
    {
      versione: "1.1.1.0",
      data: "Novembre 2026",
      titolo: "Piccole sistemazioni",
      intro: "Qualche ritocco per mettere in ordine le cose.",
      voci: [
        {
          tipo: "Migliorato", titolo: "Numeri di versione",
          punti: [
            "<b>Le versioni ora hanno un numero classico</b> (come 1.1.1.0), lo stesso che trovi in fondo a ogni pagina: così è più facile capire cosa è cambiato e quando."
          ]
        },
        {
          tipo: "Nuovo", titolo: "Crediti",
          punti: [
            "<b>In fondo a ogni pagina</b> trovi chi ha disegnato l'icona di siBalla."
          ]
        }
      ]
    },
    {
      versione: "1.1.0.0",
      data: "Novembre 2026",
      titolo: "Condividi la serata giusta",
      intro: "Il link che mandi agli amici ora porta proprio alla data che hai scelto.",
      voci: [
        {
          tipo: "Migliorato", titolo: "↗ Condividi",
          punti: [
            "<b>Solo l'evento che condividi:</b> chi apre il link vede quella serata, non tutte le date con lo stesso nome.",
            "<b>Mostra tutte le date:</b> se vuole, con un tocco vede anche le altre date dello stesso evento."
          ]
        },
        {
          tipo: "Nuovo", titolo: "Bacheca",
          punti: [
            "<b>↗ Condividi su ogni annuncio:</b> mandalo a chi potrebbe essere interessato, su WhatsApp arriva con l'anteprima dell'annuncio."
          ]
        }
      ]
    },
    {
      versione: "1.0.0.0",
      data: "Novembre 2026",
      titolo: "Bacheca, Clandestini e mappa",
      intro: "Una bacheca per organizzarsi tra ballerini, i clandestini proposti da voi, la mappa degli eventi e qualche comodità in più su ogni serata.",
      voci: [
        {
          tipo: "Nuovo", titolo: "Bacheca", link: "/bacheca",
          sotto: "Uno spazio per mettersi d'accordo prima di una serata o di un festival. La trovi nel menu ☰.",
          schede: [
            ["🚗 Passaggi", "Cerchi un passaggio o hai posti in auto"],
            ["💃 Partner", "Cerchi un leader o un follower"],
            ["🛏️ Alloggio", "Un posto per dormire al festival"],
            ["🏷️ Mercatino", "Scarpe, abiti vintage e altro"]
          ],
          punti: [
            "<b>Collegata agli eventi:</b> scegli la serata dall'elenco e data e destinazione si compilano da sole. Sulla scheda dell'evento compare quante persone cercano un passaggio o un partner.",
            "<b>Discussione sotto ogni annuncio:</b> si risponde direttamente lì, senza dover lasciare il proprio numero.",
            "<b>Avvisi via email, se vuoi:</b> lasci un indirizzo privato e ti scriviamo quando l'annuncio è online e quando qualcuno ti risponde. L'indirizzo non lo vede nessuno.",
            "<b>Il tuo annuncio lo togli tu</b>, dallo stesso telefono o computer con cui l'hai scritto. Dopo 30 giorni scade da solo.",
            "<b>Novità in evidenza:</b> un pallino sul menu ti avvisa quando ci sono annunci nuovi."
          ],
          nota: "Ogni annuncio viene controllato prima di comparire. I messaggi che non vanno bene si possono segnalare."
        },
        {
          tipo: "Nuovo", titolo: "Clandestini", link: "/",
          sotto: "Le serate improvvisate in piazza, al parco o sul lungomare hanno la loro scheda nella home, accanto a Serate e Workshop.",
          punti: [
            "<b>Aggiungi un clandestino</b> in un minuto: nome, giorno, città e luogo, più una foto o la locandina se ce l'hai.",
            "<b>Il luogo finisce sulla mappa</b> e in \"Vicino a me\", così chi è in zona lo trova.",
            "<b>\"Chiedi di toglierlo\":</b> se un clandestino è stato annullato o ha dati sbagliati, segnalacelo dalla sua scheda e lo sistemiamo.",
            "<b>Sempre aggiornati:</b> i clandestini nuovi compaiono anche se hai già la pagina aperta, senza ricaricare."
          ],
          nota: "Anche i clandestini vengono controllati prima di essere pubblicati, di solito in poche ore."
        },
        {
          tipo: "Nuovo", titolo: "Mappa e filtro per regione",
          sotto: "Per capire a colpo d'occhio dove si balla.",
          punti: [
            "<b>🗺️ Mappa:</b> tutti gli eventi su una mappa, con foto e indicazioni stradali in ogni segnaposto. Segue i filtri che hai scelto.",
            "<b>Filtro per regione:</b> vedi solo gli eventi della tua regione, o di quella dove vai in trasferta."
          ]
        },
        {
          tipo: "Migliorato", titolo: "Calendario e Condividi su ogni evento",
          punti: [
            "<b>📅 Calendario:</b> aggiungi la serata a Google Calendar, Outlook o al calendario di iPhone e Mac, con orario e luogo già compilati.",
            "<b>↗ Condividi:</b> manda l'evento su WhatsApp o copia il link per i tuoi amici."
          ]
        },
        {
          tipo: "Nuovo", titolo: "Menu ☰",
          punti: ["In alto a destra, per passare dagli <b>Eventi</b> alla <b>Bacheca</b> e a questa pagina delle <b>Novità</b>. Presto arriveranno altre sezioni."]
        }
      ]
    }
  ];

  var VISTA = "siballa-novita-vista";
  var ultima = NOVITA[0].versione;
  var qui = location.pathname.replace(/\.html$/, "").replace(/\/$/, "");
  function leggi() { try { return localStorage.getItem(VISTA) || ""; } catch (e) { return ""; } }
  function segnaVista() { try { localStorage.setItem(VISTA, ultima); } catch (e) {} }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return "&#" + c.charCodeAt(0) + ";"; }); }

  // pagina /novita: disegna l'elenco e segna l'ultima versione come vista
  var box = document.getElementById("novita");
  if (box) {
    segnaVista();
    box.innerHTML = NOVITA.map(function (v, i) {
      return '<article class="rilascio">' +
        '<div class="versione"><b>Versione ' + esc(v.versione) + "</b> " + esc(v.data) + (i === 0 ? ' <span class="ultima">ultima</span>' : "") + "</div>" +
        "<h2>" + esc(v.titolo) + "</h2>" + (v.intro ? '<p class="intro">' + esc(v.intro) + "</p>" : "") +
        (v.voci.length > 1 ? '<ul class="indice">' + v.voci.map(function (x, k) {
          return '<li><a href="#v' + i + "-" + k + '">' + esc(x.titolo) + "</a></li>";
        }).join("") + "</ul>" : "") +
        v.voci.map(function (x, k) {
          return '<section class="voce" id="v' + i + "-" + k + '"><div class="capo"><span class="tipo">' + esc(x.tipo) + "</span><h3>" +
            (x.link ? '<a href="' + esc(x.link) + '">' + esc(x.titolo) + "</a>" : esc(x.titolo)) + "</h3></div>" +
            (x.sotto ? '<p class="sotto">' + esc(x.sotto) + "</p>" : "") +
            (x.schede ? '<div class="schede">' + x.schede.map(function (s) {
              return '<div class="scheda"><strong>' + esc(s[0]) + "</strong><span>" + esc(s[1]) + "</span></div>";
            }).join("") + "</div>" : "") +
            '<ul class="punti">' + x.punti.map(function (p) { return "<li>" + p + "</li>"; }).join("") + "</ul>" +
            (x.nota ? '<p class="nota">' + esc(x.nota) + "</p>" : "") + "</section>";
        }).join("") + "</article>";
    }).join("");
    return;
  }

  // tutte le altre pagine: "✨ Novità" lampeggiante nell'intestazione finché l'ultima versione non è stata vista
  if (qui === "/novita" || leggi() === ultima) return;
  var hero = document.querySelector(".hero");
  if (!hero) return;
  var a = document.createElement("a");
  a.className = "novita-btn";
  a.href = "/novita";
  a.title = "Cosa c'è di nuovo su siBalla (versione " + ultima + ")";
  a.innerHTML = '<span aria-hidden="true">✨</span><span class="lbl">Novità</span>';
  a.addEventListener("click", segnaVista);
  var menu = hero.querySelector(".menu-btn");
  if (menu) hero.insertBefore(a, menu); else hero.appendChild(a);
})();
