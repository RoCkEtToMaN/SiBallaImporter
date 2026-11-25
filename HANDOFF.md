# HANDOFF – cronologia e passaggio di consegne

Per riprendere il lavoro da un'altra postazione: dopo il `git pull` dire a Claude
"leggi HANDOFF.md e CLAUDE.md e continuiamo". Le regole stabili del progetto sono in `CLAUDE.md`;
qui ci sono la cronologia, le decisioni e i lavori aperti. Aggiornato il **25/11/2026**.

## Dove eravamo rimasti (25/11/2026)
- Ramo `anteprima`, un commit in più di `master`:
  `4503f1b` Bacheca: "↗ Condividi" anche sui mercatini (link `/bacheca?mercatino=ID` con anteprima WhatsApp e foto).
  Da provare su https://anteprima.siballa.pages.dev, poi merge su `master` quando l'utente lo chiede
  (con eventuale voce nuova in `sito/novita.js`: oggi l'ultima è `1.3.0.0`, un ritocco sarebbe `1.3.1.0`).
  Dopo il merge riallineare `anteprima` a `master`.
- Tutto il resto è già su `master`.

## Cronologia
### 21/11/2026
- Novità 1.2.1.0: mercatini vintage in Bacheca (stato `mercatino` in "Luogo verificato" → `mercatini.json`).

### 22/11/2026
- Memoria per organizzatore e stato `workshop` in "Luogo verificato".
- Gemini risponde con un solo tipo (serata / workshop / mercatino / altro); conferme nella scheda Workshop = voto `workshop` per la serie.
- `--test-gemini-sheet`: confronta Gemini con le righe già decise dal curatore.
- Stato `serata+workshop` (pratica/ripasso/lezione + serata: in Serate e copiato in Workshop).
- Agente di verifica (`Service/AgenteVerifica.cs`) con function calling + workflow "Agente verifica".
- L'agente cerca anche gli indirizzi (Photon, Tavily) e riempie il dizionario dei luoghi; nel giro orario con `INDIRIZZI_AGENTE=true`.
- Locale scritto nel titolo cercato sulla mappa senza Gemini (`LocaliNelTitolo`).

### 23/11/2026
- Quota giornaliera di Gemini finita o modello ritirato (404) → stop immediato (`QuotaFinita`).
- Tavily limitato a 30 ricerche/giorno e 900/mese (contatore `out/tavily-uso.json`).
- Ricerca del luogo in tre passi fissi (`Service/RicercaLuogo.cs`): testi → una chiamata Gemini con citazione → controlli + Photon.
  Mai la sede dell'organizzatore senza una fonte sull'evento. Lette per intero le 2 pagine migliori (piè di pagina, "Contatti").
- Controllo qualità notturno (`Application/ControlloQualita.cs`): cancella i doppioni sicuri, segnala gli ambigui.
- `EventIdentity.Same`: stesso giorno + titolo + link = stesso evento anche con città diversa (caso Gothenburg Rhythm Camp).
- Festival/weekend con serate = `serata_e_workshop`, su entrambi i fogli (decisione del curatore).
- Stili da confermare (celle H lilla) di notte con le prove della pagina (`Service/ConfermaStili.cs`).
- Impostazioni ripulite da spazi/a capo/virgolette (`AppSettings.Load`).

### 24/11/2026
- Righe "da verificare" decise dall'agente: giro orario max 3 (`AGENTE_DA_VERIFICARE`), notte max 20 future + 20 passate;
  il resto resta al curatore con il parere "🤖 secondo l'agente" nell'email.
- Freno sul "no" (stili del calendario nel titolo, senza stili dedotti/lilla); note del curatore non più cancellate con l'arancione.
- Regole del curatore in `data/regole-curatore.md` (lette da classificazione e agente).
- Registro delle decisioni `data/registro-decisioni.json` + sezione ✍️ nell'email notturna.
- Nuovo stile Rhythm 'n' Blues (DB `StileEvento.RhythmNBlues = 11`, anche nell'app); Rock 'n' Roll non è uno stile.
- Regola: vendite di scarpe/abbigliamento da ballo = mercatino.
- Memoria: regole imparate dalle parole dei titoli (`MemoriaSerie.ImparaParole`, `data/regole-imparate.md`, righe 🧠 nell'email).
- Bot Telegram AgenteRust: progetto messo in pausa (l'importer continua a scrivere `eventi-bot.json`).

### 25/11/2026
- Bacheca: condivisione dei mercatini (su `anteprima`, vedi sopra).

## Decisioni importanti (da rispettare)
- **Meglio un evento sbagliato pubblicato che uno vero nascosto**: nel dubbio l'agente pubblica o lascia al curatore, mai "no" da solo se c'è dubbio.
- **Il curatore controlla solo ogni tanto**: nessun flusso deve aspettare il suo "ok"; l'agente non scrive mai "ok" al posto suo.
- **Obiettivo**: l'agente deve gestire da solo la validità dello Sheet; le regole le impara dai sì/no del curatore e gliele mostra
  (email notturna, `data/regole-imparate.md`), senza chiedere approvazione.
- Il DB Appwrite resta (deciso 14/11/2026).
- Versione del sito MAGGIORE.MINORE.CORREZIONE.BUILD, nuova voce in cima a `NOVITA` a ogni rilascio visibile.
- Novità su `anteprima`, su `master` solo quando lo chiede l'utente.
- Gemini gratuito: niente `AGENTE_MODELLO`, `INDIRIZZI_AGENTE_MAX=5`, ~300-370 richieste/giorno su 500.

## Da fare / idee aperte
1. Provare e pubblicare la condivisione dei mercatini (merge `anteprima` → `master`).
2. Controllare le email notturne "Agente verifica (qualita)": doppioni cancellati, stili confermati, righe decise, regole 🧠 imparate.
3. **Punto 3**: proposta settimanale di regole nuove dalle correzioni del curatore (dal registro decisioni) +
   conteggio delle righe toccate dal curatore per settimana; poi controllo delle righe `nuovo`/`gemini`.
4. **Orari**: lo Sheet non ha orari (solo date). Decidere se salvarli (nella cella della data o in una colonna) e farli cercare all'agente.
5. Scopritore di eventi (agente che trova fonti nuove).
6. Bot Telegram AgenteRust: in pausa; se si riprende chiedere dove sta il codice (fuori dal repo).
7. Vecchi punti di `ToDo.txt` "Da verificare" (qualità indirizzi `gemini`, colori righe, righe sul DB) e `sito/nicetohave.txt`.

## Note per l'altra postazione
- Su questo PC (lavoro) mancano le chiavi di Gemini e Appwrite: ci sono sul PC di casa e nei secret di GitHub.
- Push da Windows: `git -c http.sslBackend=schannel push`.
- La memoria locale di Claude non viaggia col repo: le decisioni sopra la sostituiscono.
