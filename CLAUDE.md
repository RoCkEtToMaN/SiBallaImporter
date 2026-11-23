# SiBalla – note per Claude

Progetto "Dove siBalla stasera?": un importer C# raccoglie gli eventi swing nello Google Sheet, il sito
(https://siballa.pages.dev) e l'app SiBalla li mostrano. Si lavora in italiano.

## Struttura
- `SiBallaImportingData/`: importer .NET (cerca eventi online → Sheet → email → DB Appwrite). Opzioni da riga di
  comando in `Utils/AppSettings.cs`, ricette commentate in cima a `appsettings.json`.
- `sito/`: sito statico su Cloudflare Pages (+ `sito/functions/` per Bacheca, Clandestini, statistiche).
  `sito/esporta_sheet.py` trasforma lo Sheet nel JSON del sito.
- `.github/workflows/`: `importatore.yml` (giro completo orario, usa il codice di `master`), `sito.yml` (pubblica il sito).
- Liste di lavoro: `ToDo.txt` (importer), `sito/nicetohave.txt` (sito), `HOWTO.md` (istruzioni per l'utente).

## Git
- Il push in HTTPS da Windows richiede `git -c http.sslBackend=schannel push` (con openssl fallisce).
- Rami: `master` = sito pubblico + giro completo orario; `anteprima` = versione di prova su https://anteprima.siballa.pages.dev
  (login Cloudflare Access). Le novità si fanno su `anteprima`; su `master` solo quando l'utente lo chiede.
  Dopo il merge riallineare `anteprima` a `master`.

## Sito
- Pagine non ancora pubbliche: `<script src="riservata.js">` nel `<head>` (sul pubblico rimandano alla home).
  Per pubblicarle: togliere quella riga e mettere `pubblica: true` alla voce in `sito/menu.js`.
  In `esporta_sheet.py`, `NOVITA_PUBBLICHE` accende sul pubblico le parti nuove dell'export.
- A ogni rilascio su `master` con qualcosa di visibile: nuova voce IN CIMA a `NOVITA` in `sito/novita.js`.
  Versione numerica MAGGIORE.MINORE.CORREZIONE.BUILD (funzioni nuove → 1.2.0.0, ritocchi → 1.1.2.0).
  `sito.yml` legge la prima `versione:` di `novita.js` e la mostra nel piede (`sito/piede.js`).
- `sito.yml` copia solo `sito/*.html|css|js`, `logo-app.svg` e `og*.png`: altri file vanno aggiunti lì.
- Video how-to: pulsante "▶ Come funziona" da `sito/howto.js` (elenco `VIDEO`), file in `sito/video/howto-<pagina>.mp4|jpg`,
  rifatti con `sito/video/copioni.py` (istruzioni in cima). Se una pagina cambia molto, rifare il suo video.
  Reel per Instagram (1080×1920): `sito/video/social.py` → `sito/video/social/` (non vanno sul sito).
- Icona dell'app: Flaticon, licenza gratuita, autore Magnific → citazione in `ICONA` in `sito/piede.js`.

## Database Appwrite
- Resta (deciso il 14/11/2026: non sostituirlo con un JSON su Cloudflare). L'app non è ancora sugli store.
- Ogni collezione (`serate`, `workshop`) ha un solo documento, ID `corrente`, sovrascritto con `UpsertDocument`.
- Se nessuna riga dello Sheet è confermata, il DB non viene toccato (niente app vuota).

## Google Sheet
- ID `1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo`, schede `Serate` e `Workshop`: riga 2 = intestazioni, dati dalla 3.
- Colonna "Luogo verificato": vuoto/`ok` = confermato, `nuovo` = aggiunto e swing per Gemini, `gemini` = indirizzo
  trovato da Gemini, `da verificare` = non va su sito/DB (riga arancione), `no` = scartato,
  `workshop` = workshop/corso copiato nella scheda Workshop, `mercatino` = mercatino vintage: fuori da DB/calendario/email, il sito lo mette in Bacheca (`mercatini.json`). Lo scrive
  Gemini sulle righe nuove; a ogni giro con `--excel` diventano `mercatino` anche le "da verificare" con nota "Gemini: Mercatino...".
- Le intestazioni di C, D e degli stili (Serate I–L, Workshop I–P) sono `ARRAYFORMULA`: un valore scritto sotto
  le blocca (`#REF!`). Non incollare righe intere da una scheda all'altra (vedi `HOWTO.md`); i giri con `--excel`
  svuotano da soli i valori fissi (max 20 per colonna).
- Lo Sheet deve fare da storico: le righe passate restano (nessun codice le cancella). Contiene eventi solo
  dall'8/11/2026 perché quel giorno è stato fatto un reset: i mesi prima sono nel `backup-sheet-*.json` del PC di casa
  o nella cronologia versioni dello Sheet.
- `--reset-excel` (solo dal PC) prima copia Serate e Workshop nello Sheet come "Backup Serate AAAA-MM-GG hh.mm"
  (+ JSON locale), poi svuota solo quelle due schede. Il sito salta le schede "Backup ...".
- Lettura da qui: service account `googlesheet@siballa.iam.gserviceaccount.com`, chiave in
  `SiBallaImportingData/Resources/siballa-8c6d48649c13.json`. Python: `py` + venv nella scratchpad con
  `google-auth requests`, riusando le funzioni di `sito/esporta_sheet.py`. La Drive API non è abilitata: altri
  file si leggono solo se condivisi con il service account e passati per link.
- Non scrivere nello Sheet senza una richiesta esplicita dell'utente.

## Importer dal PC
- Su questo PC mancano le chiavi di Gemini e Appwrite (ci sono sul PC di casa e su GitHub).
- Gemini piano gratuito: quota GIORNALIERA per modello (23/11/2026: 500 richieste al giorno per gemini-flash-lite). Quando finisce,
  `GeminiHttp` non riprova più e segna il modello come esaurito per il resto del giro (`QuotaFinita`); l'agente si ferma (⏸️).
  `AGENTE_MODELLO` (Variable) darebbe all'agente un'altra quota, ma il 23/11/2026 non c'era un modello adatto: gemini-flash-latest
  troppo lento/sovraccarico (503, timeout 90 s), gemini-2.5-flash-lite ritirato (404). Quindi niente AGENTE_MODELLO e INDIRIZZI_AGENTE_MAX=5.
- Tavily (ricerca web dell'agente, secret `TAVILY_API_KEY`, piano gratuito 1000 crediti/mese senza carta): max 30 ricerche al giorno e
  900 al mese (`Import:TavilyMaxGiorno/Mese`), contate in `out/tavily-uso.json` che il giro orario conserva con la cache (`DATA_FILES`).
- `--no-push`: il dizionario dei luoghi torna nel repo senza commit né push. Senza, il push automatico del dizionario
  parte solo da `master` e solo se non ci sono altri commit non pubblicati.
- Memoria delle serie (`Service/MemoriaSerie.cs`): le decisioni dell'utente in "Luogo verificato" sulle altre date
  della stessa serie decidono le date nuove al posto di Gemini; impara solo dai valori scritti da lui (non dai `no`
  con nota "Gemini:"/"Memoria:"). Ogni giro con `--excel` la applica anche alle righe future; anteprima `--memoria-serie`.
  Chiave della serie: `MemoriaSerie.TitoloSerie` = NormalizeTitle senza mesi né ordinali (NormalizeTitle non va cambiato:
  serve agli id delle immagini e al dizionario). Gemini risponde con un solo `tipo`: serata / serata_e_workshop / workshop (corsi e anche weekend) / mercatino / altro.
  Per la serie (non per l'organizzatore) una conferma nella scheda Workshop vale come voto `workshop`.
  Per organizzatore: >= 2 date decise da lui tutte uguali (`no`/`mercatino`/`workshop`) e nessuna conferma -> vale per i suoi eventi nuovi.
  Per gli eventi che non decide, passa a Gemini la "storia organizzatore" (conteggi + titoli d'esempio) come indizio.
- Stato `workshop` (solo Serate): workshop/corso finito in Serate. Ogni giro con `--excel` lo copia in Workshop (`nuovo`, nota
  "Spostato da Serate (riga N)", che la memoria ignora); la riga in Serate resta come traccia, fuori da DB/sito/email.
- Stato `serata+workshop` (solo Serate, lo scrive solo l'utente): pratica/ripasso/lezione + serata. Vale come conferma
  (DB/sito/email) e la riga viene copiata in Workshop (nota "Copiata da Serate"). Gemini (tipo `serata_e_workshop`) e la
  memoria non lo scrivono: mettono `nuovo` in Serate + copia (`Event.AncheWorkshop`).
- Agente di verifica (`Service/AgenteVerifica.cs`, ricetta 13): Gemini con function calling (leggi_pagina, cerca_nello_sheet,
  proponi), con un tetto di passi e di pagine per riga. Propone lo stato di "Luogo verificato" e non scrive mai nello Sheet.
  `--agente-verifica` = righe future in attesa, `--agente-prova` = confronto con le righe già decise, `--agente-indirizzi-prova` =
  ritrova i luoghi già noti (km). Cerca anche l'indirizzo (verifica_indirizzo con Photon; cerca_web = ricerca Google di Gemini, solo con
  `Gemini:UseGoogleSearch`, oppure Tavily con `TAVILY_API_KEY`). DuckDuckGo e simili bloccano i bot: non usarli. `--agente-dizionario[-scrivi]`:
  luoghi mancanti -> voce dell'evento nel dizionario (non verificata, Source "agente") + Sheet "gemini"; con "ok" diventa verificata.
  Workflow "Agente verifica" (modo dizionario + scrivi: da lanciare su master, perché fa il commit del dizionario).
  Nel giro orario: variabile del repo `INDIRIZZI_AGENTE=true` -> l'agente sostituisce la ricerca semplice di `--indirizzi`
  (max `INDIRIZZI_AGENTE_MAX`, default 5, eventi per giro; `AGENTE_MODELLO` = altro modello Gemini con la sua quota gratuita);
  i luoghi già trovati dall'agente (`FindAgente`) si riusano senza Gemini, e prima dell'agente il locale scritto nel titolo ("@ X",
  "(X)", "presso X") si cerca sulla mappa senza Gemini (`LocaliNelTitolo`, controlli in `LocaleGiusto`).
  Poi `Service/RicercaLuogo.cs` (dal 23/11/2026, al posto dell'agente a più passi): pagina del link + Tavily -> UNA chiamata Gemini con
  locale, via e citazione testuale -> il codice controlla che la citazione sia nei testi e contenga la via, poi Photon. Mai la sede
  dell'organizzatore senza una fonte su quell'evento (caso GheswingerS: sede a 5 km dalla festa).
- Controllo qualità (`Application/ControlloQualita.cs`, `--agente-qualita`, workflow "Agente verifica" modo qualita, ogni notte alle 3:30
  da solo): doppioni (anche lettere "decorate"), festival in Serate e Workshop, date sospette, serie contraddittorie, dati mancanti.
  Solo controlli nel codice, niente Gemini, niente scritto nello Sheet. Lo Sheet non ha una colonna per l'orario.
- `EventIdentity.Same`: stesso giorno + stesso titolo + stesso link = stesso evento anche con città diversa (fino al 23/11/2026 il
  Gothenburg Rhythm Camp veniva riaggiunto a ogni giro perché la città nello Sheet era la regione).
- Comandi una tantum: `--pulisci-lezioni[-scrivi]`, `--riclassifica-sheet[-scrivi]`, `--riclassifica-storico[-scrivi]`
  (senza `-scrivi` = anteprima). Il terzo si lancia anche da GitHub: workflow "Riclassifica storico".

## Storico nello Sheet
- Il 14/11/2026 sono state rimesse 1.114 righe passate (1/8 – 7/11/2026) recuperate dalle 118 versioni dello Sheet
  (Drive API, ora abilitata): stato "storico" = mai controllate, "da verificare" = altri balli o non controllate allora.
- Righe passate: non vanno su DB, sito né email; "storico" non insegna niente al dizionario (luoghi e stili).
- Prima dell'agosto 2026 lo Sheet aveva poche righe a mano, e diverse con l'anno sbagliato (eventi 2027 segnati 2026).
