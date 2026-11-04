# SiBalla Importer — come funziona

Il repo contiene il programma che cerca gli eventi swing e due automazioni (workflow di
GitHub Actions) che lo fanno girare da sole. Si trovano in `.github/workflows/` e si
vedono nella tab **Actions** del repo su GitHub.

| Workflow | File | Quando parte | Cosa fa |
|---|---|---|---|
| **Importatore eventi** | `importatore.yml` | ogni lunedì alle 6:37 (ora italiana) | cerca gli eventi, manda l'email, opzionalmente aggiorna Sheet e Appwrite |
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
   - **Sheet** (`EnableImportingOnExcel`): aggiunge i nuovi eventi allo Sheet. Se l'evento ha
     un'immagine, prima la scarica e la carica nel bucket `immagini` di Appwrite Storage, poi scrive
     `sì` nella colonna **Immagine**. In **Luogo verificato** scrive `da verificare`: finché resta
     così la riga **non va su Appwrite** e il dizionario dei luoghi non ne impara l'indirizzo.
     Controllato l'indirizzo (e che l'evento ti interessi), scrivi `ok` o svuota la cella: al
     caricamento successivo su Appwrite la riga arriva nell'app, con la sua immagine.
   - **Coordinate** (`UpdateSheetCoordinates`): riscrive sullo Sheet le coordinate trovate.
   - **Appwrite** (`EnableImportingOnDB`): carica **tutto lo Sheet** sul database Appwrite, sostituendo
     le collezioni `serate` e `workshop`. È da qui che l'app SiBalla legge gli eventi. Per le righe
     con `sì` in **Immagine** mette `HasImage = true` e in `ImageUrl` il link del file su Storage.

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

### Impostazioni dell'esecuzione automatica del lunedì

| Impostazione | Default | Variable di GitHub per cambiarlo |
|---|---|---|
| Email | sì | `ENABLE_SENDING` |
| Aggiunta allo Sheet | no | `ENABLE_IMPORTING_ON_EXCEL` |
| Caricamento su Appwrite | no | `ENABLE_IMPORTING_ON_DB` |
| Coordinate sullo Sheet | no | `UPDATE_SHEET_COORDINATES` |
| Immagini per le righe dello Sheet | no | `CERCA_IMMAGINI_SHEET` |
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

Il segnale è che **il lunedì non arriva l'email**. Per riattivarli: Actions → seleziona il
workflow → **Enable workflow** (per tutti e due). Dopo, conviene controllare che il progetto
Appwrite non sia andato in pausa nel frattempo.

Il keep-alive sta in questo repo, e non in quello dell'app, proprio per questo: se si ferma, si
ferma anche l'email e ce ne si accorge.

---

## Lanciarlo dal PC

```
dotnet run --project SiBallaImportingData -- --send --giorni=7
```

Le opzioni si possono scrivere come flag (`--send`, `--excel`, `--db`, `--coords`, `--giorni=N`,
`--radius=N`) oppure in `appsettings.json`. Ci sono anche dei test che stampano i risultati senza
scrivere niente da nessuna parte:

- `--test-swing`: mostra quali eventi Gemini considera swing e quali no
- `--test-immagini`: conta gli eventi che hanno un'immagine
- `--test-indirizzi`: prova a trovare con Gemini l'indirizzo dei locali mancanti

Sul PC le chiavi si mettono con `dotnet user-secrets` (es. `Email:MailjetApiKey`,
`Gemini:ApiKey`). Il dizionario dei luoghi (`DizionarioEventiInfo.json`) sta nella cartella del
programma (`bin\...`), gli altri file di dati in `%LOCALAPPDATA%\SiBallaImporter`.

La copia di riferimento del dizionario è nel repo, in `data/DizionarioEventiInfo.json`:

- **all'avvio** il programma fa `git pull` e copia quel file in `bin`;
- **a fine giro** lo riporta in `data/` e, se è cambiato, fa commit (solo di quel file) e push.

Su GitHub succede lo stesso, ma commit e push li fa il workflow. Se il pull o il push non riescono il
programma lo scrive nel log e va avanti; il commit resta in locale e lo pubblichi tu con un push.
