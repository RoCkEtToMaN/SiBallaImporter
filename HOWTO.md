# SiBalla Importer — come funziona

Il repo contiene il programma che cerca gli eventi swing e due automazioni (workflow di
GitHub Actions) che lo fanno girare da sole. Si trovano in `.github/workflows/` e si
vedono nella tab **Actions** del repo su GitHub.

| Workflow | File | Quando parte | Cosa fa |
|---|---|---|---|
| **Importatore eventi** | `importatore.yml` | ogni notte, il primo tentativo riuscito tra le 0:01 e le 6:01 (ora italiana) | cerca gli eventi, manda l'email se ci sono eventi nuovi, opzionalmente aggiorna Sheet e Appwrite |
| **Appwrite keep-alive** | `appwrite-keepalive.yml` | ogni giorno alle 06:17 UTC | legge un documento dal database Appwrite per non farlo andare in pausa |

I due workflow sono indipendenti: se uno fallisce l'altro gira comunque.

---

## 1. Importatore eventi — cerca gli eventi

È il programma vero e proprio (`SiBallaImportingData`). A ogni giro:

1. **Legge il Google Sheet** con le serate e i workshop già noti.
2. **Aggiorna il dizionario dei luoghi** (`DizionarioEventiInfo.json`): impara gli indirizzi dalle
   righe dello Sheet e trova le coordinate mancanti con Photon (geocoding).
3. **Cerca gli eventi online** su e-swing e SwingPlanit, tenendo solo quelli entro il raggio
   impostato (`Radius`) da Genova.
4. **Confronta con lo Sheet**: gli eventi trovati online che nello Sheet non ci sono sono i "nuovi".
5. Poi, in base alle impostazioni:
   - **Email** (`EnableSending`): prende gli eventi dei prossimi `Giorni`, li fa controllare a
     Gemini (gli eventi non swing finiscono in una sezione "da verificare") e manda l'email con Mailjet.
     Con `EmailSoloSeCambia` (attivo nel giro notturno) l'email parte solo se ci sono eventi che
     l'ultima email inviata non aveva.
   - **Sheet** (`EnableImportingOnExcel`): aggiunge i nuovi eventi allo Sheet, dopo averli fatti
     classificare da Gemini (vedi [Righe nuove nello Sheet](#righe-nuove-nello-sheet-nuovo-e-da-verificare)).
     Se l'evento ha un'immagine, prima la scarica e la carica nel bucket `immagini` di Appwrite
     Storage, poi scrive `sì` nella colonna **Immagine**.
   - **Coordinate** (`UpdateSheetCoordinates`): riscrive sullo Sheet le coordinate trovate.
   - **Appwrite** (`EnableImportingOnDB`): carica lo Sheet sul database Appwrite, sostituendo
     le collezioni `serate` e `workshop` (vedi [Salvataggio su Appwrite](#salvataggio-su-appwrite)).
     È da qui che l'app SiBalla legge gli eventi. Per le righe con `sì` in **Immagine** mette
     `HasImage = true` e in `ImageUrl` il link del file su Storage.

### Righe nuove nello Sheet: `nuovo` e `da verificare`

Prima di aggiungere allo Sheet gli eventi trovati online, il programma li fa classificare da Gemini.
L'esito decide cosa scrive nella colonna **Luogo verificato**:

| Esito di Gemini | Nello Sheet | Va su Appwrite? |
|---|---|---|
| swing | riga normale, `nuovo` | **sì**, subito, anche con coordinate non controllate |
| non swing (WCS, lezioni, country, concerti…) | riga **arancione chiaro**, `da verificare`, motivo di Gemini nella **nota** della cella | **no** |
| Gemini non risponde (anche dopo i nuovi tentativi) o manca la chiave | come "non swing", nella nota "Gemini non ha classificato l'evento" | **no** |

Cosa fare con le righe arancioni: passa col mouse sulla cella **Luogo verificato** per leggere il
motivo, poi:

- se l'evento va bene, controlla indirizzo e coordinate e scrivi `ok` (o svuota la cella): al
  caricamento successivo su Appwrite arriva nell'app, e il giro dopo toglie da solo colore e nota;
- se non ti interessa, cancella la riga.

Il colore lo toglie il programma a ogni giro, anche quando l'aggiunta allo Sheet è spenta. Tocca
solo le righe con quel colore preciso: gli altri colori che usi nello Sheet restano.

Valori di **Luogo verificato**, in sintesi:

| Valore | Chi lo scrive | Va su Appwrite? | Il dizionario dei luoghi ne impara l'indirizzo? |
|---|---|---|---|
| vuoto | tu | sì | sì |
| `ok` | tu, dopo il controllo | sì | sì (e non viene più sovrascritto) |
| `auto` | il programma, indirizzo copiato dal dizionario | sì | no |
| `nuovo` | il programma, evento swing per Gemini | sì | no, finché non scrivi `ok` |
| `gemini` | il programma, indirizzo trovato da Gemini (vedi sotto) | sì | no, finché non scrivi `ok` |
| `da verificare` | il programma, evento da controllare | **no** | no |

Una riga `nuovo` senza indirizzo può prendere quello del dizionario (se lo stesso evento in un'altra
riga ha un indirizzo verificato): in quel caso diventa `auto`.

Gemini viene chiamato una volta sola per evento: la classificazione fatta per lo Sheet viene riusata
per l'email. Al primo giro con lo Sheet vuoto deve classificare tutti gli eventi trovati online (a
gruppi di 20), quindi il giro dura di più; dai giri successivi solo quelli nuovi.

### Indirizzi trovati da Gemini

Con `CercaIndirizzi` (flag `--indirizzi`, Variable di GitHub `CERCA_INDIRIZZI`) il programma cerca
il locale delle righe **future senza indirizzo** dopo la città nella colonna F:

1. le date dello stesso evento (stesso organizzatore, città e titolo) contano come una sola ricerca;
2. Gemini legge il sito in **Riferimento** e propone locale e indirizzo (senza ricerca Google, cioè
   con `Gemini:UseGoogleSearch` = false, le righe senza sito vengono saltate);
3. l'indirizzo vale solo se Gemini non ha confidenza "bassa" **e** Photon lo trova entro 10 km dalla città;
4. se vale, su tutte le date dell'evento scrive nella colonna F `Paese, Città, Indirizzo(lat;lng)`,
   con una **nota** sulla cella (locale, fonte, confidenza, motivo), e `gemini` in **Luogo verificato**.

Le righe `gemini` vanno su Appwrite già nello stesso giro. Il dizionario impara l'indirizzo solo dopo
che l'hai controllato e hai scritto `ok`. Se lo stesso evento ha già un indirizzo verificato da te in
un'altra riga, quello sostituisce l'indirizzo di Gemini e la riga diventa `auto`. Se l'indirizzo è
sbagliato, correggi la colonna F e scrivi `ok`.

Cosa resta fuori:
- le righe `da verificare`: prima decidi tu se l'evento ti interessa;
- gli eventi cercati senza risultato: il dizionario se lo segna (`geminiNonTrovato` con la data) e
  li ricerca solo dopo 30 giorni, perché nel frattempo il sito può essere aggiornato. Se Gemini va in
  errore (es. 503 anche dopo i nuovi tentativi), l'evento non viene segnato e si riprova al giro dopo.

Al massimo `CercaIndirizziMax` eventi per giro (default 20, Variable `CERCA_INDIRIZZI_MAX`), i più
vicini nel tempo per primi, con 4 s di pausa tra uno e l'altro per il piano gratuito di Gemini. Con 20
eventi il giro dura qualche minuto in più. Di default è **attivo** sia sul PC (`appsettings.json`)
sia su GitHub (si spegne con la Variable `CERCA_INDIRIZZI` = `false`). Su GitHub scrive sullo Sheet
anche se l'aggiunta dei nuovi eventi (`ENABLE_IMPORTING_ON_EXCEL`) è spenta.

`--test-indirizzi` resta per provare: stampa cosa troverebbe, senza scrivere niente.

### Gemini: errori temporanei

Se Gemini risponde con un errore temporaneo (`429` troppe richieste, `503` "high demand" e gli altri
`5xx`) la richiesta viene riprovata fino a 3 volte (sia la classificazione degli eventi sia la ricerca
degli indirizzi), aspettando 10 s, 30 s e 60 s (o il tempo indicato da
Gemini). Nel log: `[AI] Gemini 503, nuovo tentativo tra 10 s (1/3)`. Se fallisce anche il terzo
tentativo gli eventi di quel gruppo, per l'email, restano tra gli swing; per lo Sheet vanno
`da verificare`. Nel caso peggiore il giro si allunga di una decina di minuti.

Da Visual Studio il debugger può fermarsi su questa eccezione anche se è gestita: con F5 si va avanti.

### Salvataggio su Appwrite

Ogni collezione (`serate`, `workshop`) contiene **un solo documento**, con un campo `json` che
contiene tutte le righe del foglio (escluse quelle `da verificare`). A ogni caricamento:

1. il programma salva un documento nuovo con lo Sheet attuale;
2. solo se il salvataggio è riuscito cancella i documenti vecchi (normalmente uno, quello del giro prima).

Se il salvataggio fallisce restano i dati del giro precedente e il programma esce con errore (su
GitHub il run diventa rosso e arriva la mail). Se fallisce la cancellazione, i documenti vecchi li
toglie il giro dopo. Per qualche secondo, tra i due passaggi, ci sono due documenti: l'app deve
leggere il più recente, non unirli.

Con lo Sheet vuoto (lettura fallita o foglio svuotato) il DB non viene toccato. Se invece nel foglio ci
sono solo righe `da verificare`, sul DB va una lista vuota e l'app non mostra eventi.

### Immagini degli eventi

Il file su Storage si chiama `ev` + un hash di **data di inizio + titolo** della riga. Se cambi la
data o il titolo nello Sheet il collegamento si perde: al caricamento su Appwrite il log lo segnala
(`file ... non è su Storage`) e l'evento va sul DB senza immagine. Per un evento inserito a mano
puoi caricare tu il file nel bucket con quel nome e scrivere `sì` nella colonna.

Per le righe già nello Sheet c'è `CercaImmaginiSheet` (flag `--immagini-sheet`, Variable di GitHub
`CERCA_IMMAGINI_SHEET`): per le righe future con la colonna **Immagine** vuota legge il sito in
"Riferimento", prende l'immagine di anteprima che il sito dichiara (`og:image`), la carica e scrive
`sì`; se il sito non ne ha scrive `no`, così non la ricerca a ogni giro (cancella il `no` per
riprovare). Al massimo `CercaImmaginiSheetMax` righe per giro (default 100). Con le pagine Facebook
di solito non funziona: senza login non mostrano l'anteprima. Vengono scartati i link a WhatsApp,
Telegram e moduli Google, le immagini standard delle piattaforme (Fillout, DICE) e i formati che non
sono jpg, png, webp o gif.

Le righe rimaste a `no` prendono il **logo dell'organizzatore**, se c'è in `data/loghi/` (regole e
nomi dei file nel [README della cartella](data/loghi/README.md)): nella colonna diventa `logo`.

Configurazione su Appwrite (una volta sola):

1. **Overview → API Keys → Create API key** con gli scope di lettura e scrittura del database
   (documenti/righe) e di Storage: `buckets.read`, `buckets.write`, `files.read`, `files.write`.
   La chiave va nel secret `APPWRITE_API_KEY` (GitHub) e in
   `dotnet user-secrets set "Appwrite:ApiKey" "..."` (PC).
2. Il bucket `immagini` lo crea il programma la prima volta che serve, con lettura pubblica (ruolo
   **Any** → **Read**) così l'app vede le immagini senza login.
3. Nello Sheet, riga 2 di **Serate** e **Workshop**: intestazione `Immagine` dopo l'ultima colonna
   (entro la Z).

### Impostazioni dell'esecuzione automatica notturna

| Impostazione | Default | Variable di GitHub per cambiarlo |
|---|---|---|
| Email | sì | `ENABLE_SENDING` |
| Aggiunta allo Sheet | no | `ENABLE_IMPORTING_ON_EXCEL` |
| Caricamento su Appwrite | no | `ENABLE_IMPORTING_ON_DB` |
| Coordinate sullo Sheet | no | `UPDATE_SHEET_COORDINATES` |
| Immagini per le righe dello Sheet | no | `CERCA_IMMAGINI_SHEET` |
| Indirizzi con Gemini per le righe dello Sheet | **sì** | `CERCA_INDIRIZZI` (`false` per spegnerla; max per giro: `CERCA_INDIRIZZI_MAX`, default 20) |
| Giorni coperti dall'email | 7 | `GIORNI` |

Le Variables si impostano in **Settings → Secrets and variables → Actions → tab Variables**,
senza toccare il file del workflow.

### Lancio a mano

**Actions → Importatore eventi → Run workflow**: compare un modulo con le stesse opzioni. Serve per
esempio a caricare lo Sheet su Appwrite dopo averlo modificato (metti "Carica gli eventi dello Sheet
su Appwrite" a `true` e, se non vuoi l'email, "Invia l'email" a `false`).

### Secrets necessari

In **Settings → Secrets and variables → Actions → tab Secrets**:

- `MAILJET_API_KEY`, `MAILJET_API_SECRET`: invio dell'email
- `GOOGLE_SERVICE_ACCOUNT_JSON`: accesso al Google Sheet
- `GEMINI_API_KEY`: controllo degli eventi con l'AI
- `APPWRITE_API_KEY`: caricamento delle immagini su Appwrite Storage (senza, le righe vanno nello
  Sheet senza immagine)

### Dove guardare il risultato

Nel run, lo step **Esegui importatore** stampa all'inizio la configurazione usata e poi tutto il log.
In fondo al run, negli **Artifacts**, ci sono i file JSON prodotti (dizionario dei luoghi, eventi
senza città, …), conservati per 30 giorni.

---

## 2. Appwrite keep-alive — tiene attivo il database

Sul piano gratuito di Appwrite Cloud un progetto senza attività per qualche giorno viene **messo
in pausa**: l'app smette di ricevere gli eventi e anche l'importatore non riesce più a scrivere.

Il workflow ogni giorno fa una lettura vera (un documento da `serate` e uno da `workshop`), che
per Appwrite conta come attività. Non scrive niente e non richiede secrets.

- **Run verde**: il database ha risposto, tutto ok.
- **Run rosso** (arriva la mail di GitHub): il progetto è già in pausa. Nel log si vede
  `"type":"project_paused"`. Va riattivato **a mano dalla console di Appwrite**: il workflow
  non può farlo. Dal giorno dopo torna a tenerlo attivo.

Si può lanciare a mano da **Actions → Appwrite keep-alive → Run workflow**, per esempio subito
dopo aver riattivato il progetto, per controllare che risponda.

---

## Attenzione: GitHub spegne i workflow dopo 60 giorni

Se nel repo non arriva **nessun commit per 60 giorni**, GitHub disattiva tutti i workflow
pianificati, quindi sia l'importatore sia il keep-alive. I run automatici non contano come attività.

Il segnale è che **nella tab Actions non compaiono più run notturni** (l'email da sola non basta:
parte solo quando ci sono eventi nuovi). Per riattivarli: Actions → seleziona il
workflow → **Enable workflow** (per tutti e due). Dopo, conviene controllare che il progetto
Appwrite non sia andato in pausa nel frattempo.

Il keep-alive sta in questo repo, e non in quello dell'app, proprio per questo: se si ferma, si
ferma anche l'importatore e ce ne si accorge.

---

## Lanciarlo dal PC

```
dotnet run --project SiBallaImportingData -- --send --giorni=7
```

Le opzioni si possono scrivere come flag (`--send`, `--excel`, `--db`, `--coords`, `--giorni=N`,
`--radius=N`) oppure in `appsettings.json`. Attenzione: l'`appsettings.json` locale ha attivi sia
l'aggiunta allo Sheet sia il caricamento su Appwrite, al contrario del giro su GitHub. Per caricare
solo lo Sheet su Appwrite, senza ricerca né email:
`--search=false --send=false --excel=false --db=true`. Ci sono anche dei test che stampano i risultati senza
scrivere niente da nessuna parte:

- `--test-swing`: mostra quali eventi Gemini considera swing e quali no
- `--test-immagini`: conta gli eventi che hanno un'immagine
- `--test-indirizzi`: prova a trovare con Gemini l'indirizzo dei locali mancanti (per scriverli
  davvero nello Sheet c'è `--indirizzi`, vedi [Indirizzi trovati da Gemini](#indirizzi-trovati-da-gemini))

Sul PC le chiavi si mettono con `dotnet user-secrets` (es. `Email:MailjetApiKey`,
`Gemini:ApiKey`). Il dizionario dei luoghi (`DizionarioEventiInfo.json`) sta nella cartella del
programma (`bin\...`), gli altri file di dati in `%LOCALAPPDATA%\SiBallaImporter`.

La copia di riferimento del dizionario è nel repo, in `data/DizionarioEventiInfo.json`:

- **all'avvio** il programma fa `git pull` e copia quel file in `bin`;
- **a fine giro** lo riporta in `data/` e, se è cambiato, fa commit (solo di quel file) e push.

Su GitHub succede lo stesso, ma commit e push li fa il workflow. Se il pull o il push non riescono il
programma lo scrive nel log e va avanti; il commit resta in locale e lo pubblichi tu con un push.
