# SiBalla Importer — come funziona

Il repo contiene il programma che cerca gli eventi swing e le automazioni (workflow di
GitHub Actions) che lo fanno girare da sole. Si trovano in `.github/workflows/` e si
vedono nella tab **Actions** del repo su GitHub.

| Workflow | File | Quando parte | Cosa fa |
|---|---|---|---|
| **Importatore eventi** | `importatore.yml` | ogni ora al minuto 00 | cerca online, aggiunge i nuovi eventi allo Sheet, aggiorna Appwrite e avvia il sito; email solo con eventi nuovi |
| **Sito pubblico** | `sito.yml` | ogni ora al minuto 17, a fine importazione e su richiesta | legge lo Sheet e pubblica gli eventi sul sito, senza inviare email |
| **Appwrite keep-alive** | `appwrite-keepalive.yml` | ogni giorno alle 06:17 UTC | legge un documento dal database Appwrite per non farlo andare in pausa |

L'importatore avvia anche la pubblicazione del sito a fine giro. Il sito ha inoltre un proprio
avvio orario indipendente; il keep-alive resta separato.

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
   - **Email** (`EnableSending`): prende gli eventi dello Sheet dei prossimi `Giorni` e gli
     eventuali eventi online non ancora presenti nello Sheet. Gemini controlla solo questi ultimi;
     le righe dello Sheet già curate non vengono riclassificate per il bollettino. Invia con Mailjet.
     Con `EmailSoloSeCambia` (attivo nel giro orario) l'email parte solo se ci sono eventi che
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
- se non ti interessa, scrivi `no`: la riga resta nello Sheet ma non va da nessuna parte (app, sito,
  email, dizionario) e il giro dopo perde il colore. Meglio che cancellarla: una riga cancellata,
  se l'evento è ancora online, al giro dopo viene riaggiunta come `da verificare`.

Il colore lo toglie il programma a ogni giro, anche quando l'aggiunta allo Sheet è spenta. Tocca
solo le righe con quel colore preciso: gli altri colori che usi nello Sheet restano.

Valori di **Luogo verificato**, in sintesi:

| Valore | Chi lo scrive | Va su Appwrite? | Il dizionario dei luoghi ne impara l'indirizzo? |
|---|---|---|---|
| vuoto | tu | sì | sì |
| `ok` | tu, dopo il controllo | sì | sì (e non viene più sovrascritto) |
| `auto` | il programma, indirizzo copiato dal dizionario | sì | no |
| `nuovo` | il programma, evento swing per Gemini (o per la memoria delle serie, nota "Memoria: …") | sì | no, finché non scrivi `ok` |
| `gemini` | il programma, indirizzo trovato da Gemini (vedi sotto) | sì | no, finché non scrivi `ok` |
| `da verificare` | il programma, evento da controllare | **no** | no |
| `no` | tu, evento scartato (niente app, sito né email); oppure la memoria delle serie, con la nota "Memoria: …" | **no** | no |
| `mercatino` | tu o Gemini: mercatino vintage senza ballo, va solo nella Bacheca del sito | **no** | no |
| `workshop` | tu (scheda Serate): è un workshop o un corso. Il programma lo copia nella scheda Workshop come `nuovo` (nota "Spostato da Serate (riga N)…"); in Serate resta come traccia | **no** (va la copia in Workshop) | no |

Una riga `nuovo` senza indirizzo può prendere quello del dizionario (se lo stesso evento in un'altra
riga ha un indirizzo verificato): in quel caso diventa `auto`.

#### Memoria delle serie: il programma impara dalle tue decisioni

Le date di un evento che si ripete (stesso organizzatore, città e titolo, a parte anni, numeri, ordinali
come "4th" e nomi dei mesi)
vengono decise come hai deciso tu per le altre date, senza chiedere a Gemini:

- se hai scartato (`no`) **almeno 2 date** e non ne hai confermata nessuna, le date nuove arrivano
  già `no`, e anche le righe future ancora `da verificare`, `nuovo` o `gemini` diventano `no`;
- se la serie è stata confermata e mai scartata, le date nuove arrivano `nuovo` (e le righe future
  `da verificare` diventano `nuovo`). Conta come conferma piena `ok`/cella vuota (o un altro valore
  scritto da te), come mezza conferma una riga `nuovo`/`gemini` arrivata alla sua data senza che tu
  l'abbia scartata: servono almeno 2 conferme piene, o 4 mezze;
- se hai segnato `mercatino` o `workshop` **almeno 1 data** e nient'altro, le date nuove arrivano
  già così (`workshop` = copiate nella scheda Workshop). Per la serie, una data confermata nella
  **scheda Workshop** vale come `workshop` (per l'organizzatore invece resta una conferma);
- storia mista (date confermate e date scartate) o troppo corta: si guarda l'organizzatore (sotto),
  altrimenti decide Gemini come prima.

**Organizzatore**: se di un organizzatore (in tutte le città e con tutti i titoli) hai deciso **almeno
2 date** e tutte allo stesso modo (tutte `no`, tutte `mercatino` o tutte `workshop`) senza confermarne
nessuna, anche i suoi eventi nuovi prendono quel valore (nota "Memoria: organizzatore con N date…").
Basta confermare (`ok`) un suo evento per fermare la regola.

Le righe decise così hanno la nota **"Memoria: …"** con il motivo. Se la memoria sbaglia, correggi
la riga: scrivere `ok` su una data di una serie scartata la rende "mista" e la memoria smette di
decidere da sola. I `no` scritti da Gemini (righe storico) o dalla memoria stessa non contano: impara
solo da te. Se nello stesso giorno della stessa serie c'è una riga `no` e un'altra che non lo è, la riga `no` è
un doppione tolto, non uno scarto.
Per vedere cosa cambierebbe sulle righe già nello Sheet: `--memoria-serie` (vedi `appsettings.json`).

Per gli eventi che la memoria non decide, Gemini riceve come **indizio** la storia dell'organizzatore
(in tutte le città): quante sue date hai confermato, quante sono passate senza obiezioni, quante hai
scartato, con fino a 3 titoli d'esempio di serie confermate e scartate. Non è una regola: Gemini
decide comunque guardando l'evento, e se la storia ha pesato lo scrive nel motivo.
Gemini sceglie per ogni evento **un solo tipo**: `serata` (anche pratiche e social practice) → `nuovo`;
`workshop` (lezioni, corsi, laboratori, stage, e anche weekend, festival ed exchange di stili swing) →
scheda Workshop; `mercatino` → Bacheca; `altro` (non swing, e concerti/festival rockabilly o
rock'n'roll senza social dance swing esplicita) → `da verificare`. Gli eventi con "corso" o "lezione"
nel titolo sono già esclusi dalla ricerca. Per misurare quanto Gemini è d'accordo con te:
profilo "Prova Gemini sullo Sheet" (`--test-gemini-sheet`). L'anteprima
`--memoria-serie` mostra anche cosa riceverebbe Gemini per le righe `da verificare`.

Gemini viene chiamato una volta sola per evento: la classificazione fatta per lo Sheet viene riusata
per l'email. Al primo giro con lo Sheet vuoto deve classificare tutti gli eventi trovati online (a
gruppi di 20), quindi il giro dura di più; dai giri successivi solo quelli nuovi.

### Spostare una riga da Serate a Workshop (o viceversa)

Nei due fogli alcune colonne non si scrivono a mano: le riempie una formula nella **riga 2**
(`ARRAYFORMULA`) per tutte le righe sotto.

| Foglio | Colonne calcolate |
|---|---|
| Serate | C (Giorni), D (N), I-L (Lindy, Blues, Boogie, Balboa) |
| Workshop | C (Giorni), D (N), I-P (Lindy, Blues, Boogie, Balboa, Shag, Jazz, Aerials, Afrodance) |

Se in una di queste celle c'è un valore incollato, la formula non può scriverci sopra e si blocca:
l'intestazione mostra `#REF!` e la colonna resta vuota in **tutto** il foglio. Copiando una riga
intera da un foglio all'altro i valori calcolati vengono incollati come testo, e succede proprio questo.

Le colonne poi non sono nella stessa posizione: Workshop ha quattro stili in più (M-P), quindi
Gratis, Riferimento, Immagine, Ordine e Luogo verificato in Workshop sono 4 colonne più a destra.

Come spostare una riga:
- copia a parte **A-B** (date), **E-H** (nome, luogo, organizzatore, stile) e le colonne **da Gratis in
  poi**, incollandole nelle colonne con la stessa intestazione;
- oppure incolla la riga intera e poi **svuota** C, D e le colonne degli stili (seleziona e Canc).

Se vedi `#REF!` nelle intestazioni: cerca nelle colonne calcolate le celle con un valore scritto e
svuotale, la formula riparte subito. Lo può fare anche Claude leggendo lo Sheet con il service
account del programma (`SiBallaImportingData/Resources/siballa-*.json`), che può anche svuotare
solo quelle celle.

### Indirizzi trovati da Gemini

Con `CercaIndirizzi` (flag `--indirizzi`, Variable di GitHub `CERCA_INDIRIZZI`) il programma cerca
il locale delle righe **future senza indirizzo** dopo la città nella colonna F:

1. le date dello stesso evento (stesso organizzatore, città e titolo) contano come una sola ricerca;
2. Gemini legge il sito in **Riferimento** e propone locale e indirizzo. Senza ricerca Google (cioè
   con `Gemini:UseGoogleSearch` = false) vengono saltate le righe senza sito e quelle con un link a
   Facebook, Instagram, WhatsApp o Telegram, che Gemini senza login non riesce a leggere: nel log
   compare quante sono. Non vengono segnate, quindi se cambi il link ripartono;
3. l'indirizzo vale solo se Gemini non ha confidenza "bassa" **e** Photon lo trova entro 10 km dalla città;
4. se Gemini conosce il nome del locale ma non la via (o Photon non trova la via), Photon cerca il
   **locale per nome** entro 10 km dalla città. Il risultato vale solo se il nome trovato ha almeno una
   parola in comune con quello indicato da Gemini (parole generiche come "club", "bar", "teatro" non
   contano); la via la prende dal geocoding inverso, se non c'è usa il nome del locale;
5. se vale, su tutte le date dell'evento scrive nella colonna F `Paese, Città, Indirizzo(lat;lng)`,
   con una **nota** sulla cella (locale, come è stato trovato, confidenza, motivo, fonte), e `gemini`
   in **Luogo verificato**.

Le righe `gemini` vanno su Appwrite già nello stesso giro. Il dizionario impara l'indirizzo solo dopo
che l'hai controllato e hai scritto `ok`. Se lo stesso evento ha già un indirizzo verificato da te in
un'altra riga, quello sostituisce l'indirizzo di Gemini e la riga diventa `auto`. Se l'indirizzo è
sbagliato, correggi la colonna F e scrivi `ok`.

Cosa resta fuori:
- le righe `da verificare`: prima decidi tu se l'evento ti interessa;
- gli eventi cercati senza risultato: il dizionario se lo segna (`geminiNonTrovato` con la data) e
  li ricerca solo dopo 30 giorni, perché nel frattempo il sito può essere aggiornato. Lo stesso vale
  quando Gemini rifiuta la richiesta per quell'evento (es. `400 INVALID_ARGUMENT`, di solito un link
  che non riesce ad aprire). Se invece l'errore è temporaneo (es. 503 anche dopo i nuovi tentativi) o
  riguarda la chiave, l'evento non viene segnato e si riprova al giro dopo. Per far ricercare subito
  tutto (es. dopo aver migliorato la ricerca) basta togliere le righe `geminiNonTrovato` dal
  dizionario in `data/`.

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

Le righe che il programma aggiunge allo Sheet nello stesso giro arrivano sul DB subito: dopo
l'aggiunta lo Sheet viene riletto (nel log: `rileggo lo Sheet con le righe appena aggiunte`).

Con lo Sheet vuoto (lettura fallita o foglio svuotato) il DB non viene toccato. Se invece nel foglio ci
sono solo righe `da verificare`, sul DB va una lista vuota e l'app non mostra eventi.

### Immagini degli eventi

Il file su Storage si chiama `ev` + un hash di **data di inizio + titolo** della riga. Se cambi la
data o il titolo nello Sheet il collegamento si perde: al caricamento su Appwrite il log lo segnala
(`file ... non è su Storage`) e l'evento va sul DB senza immagine.

#### Mettere a mano l'immagine di un evento

Serve per gli eventi senza immagine (`no` nella colonna **Immagine**) o con un'immagine sbagliata, per
esempio quelli con un link Facebook in "Riferimento": senza login Facebook non dà l'anteprima e il
programma non riesce a prenderla. Nello Sheet non va cambiato niente.

1. **Scarica l'immagine**: apri l'evento nel browser (da loggato), clic destro sulla copertina →
   "Salva immagine con nome" (o "Apri immagine in una nuova scheda" e salvala da lì).
2. **Dagli il nome** `data_titolo`: data di inizio della riga (`aaaa-mm-gg`), `_`, titolo della riga,
   es. `2026-11-20_swing-sul-mare.jpg`. Maiuscole, accenti, spazi e numeri non contano; formati jpg,
   png o webp.
3. **Caricala nella cartella `data/immagini`** del repo:
   - da GitHub: <https://github.com/RoCkEtToMaN/SiBallaImporter/tree/master/data/immagini> →
     **Add file → Upload files** → trascina le immagini (anche più insieme) → **Commit changes**;
   - oppure dal PC in `C:\Iroi\Fork\SiBallaImportingData\data\immagini\` (la cartella principale del
     repo, **non** `SiBallaImportingData\SiBallaImportingData\data`), poi commit e push.
4. **Nello Sheet: menu SiBalla → Carica immagini e aggiorna excel e sito.** Dopo 5-10 minuti nella colonna
   **Immagine** c'è `sì` e il sito mostra l'immagine. Senza questo passo l'immagine arriva con il prossimo giro
   orario.

Se la colonna resta `no`, nel log del giro cerca `===== Immagini messe a mano =====`: dice se il nome
non corrisponde a nessuna riga (`nessuna riga dello Sheet il ... con titolo ...`). Se poi cambi data o
titolo della riga, rinomina anche il file; passato l'evento il file si può cancellare. L'immagine
messa a mano ha la precedenza su quella trovata online e sul logo. Regole complete nel
[README della cartella](data/immagini/README.md).

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

### Impostazioni dell'esecuzione automatica oraria

| Impostazione | Default | Variable di GitHub per cambiarlo |
|---|---|---|
| Email | sì | `ENABLE_SENDING` |
| Aggiunta allo Sheet | **sì** | `ENABLE_IMPORTING_ON_EXCEL` |
| Caricamento su Appwrite | **sì** | `ENABLE_IMPORTING_ON_DB` |
| Coordinate sullo Sheet | no | `UPDATE_SHEET_COORDINATES` |
| Immagini per le righe dello Sheet | **sì** | `CERCA_IMMAGINI_SHEET` |
| Indirizzi con Gemini per le righe dello Sheet | **sì** | `CERCA_INDIRIZZI` (`false` per spegnerla; max per giro: `CERCA_INDIRIZZI_MAX`, default 20) |
| Giorni coperti dall'email | 7 | `GIORNI` |

Le Variables si impostano in **Settings → Secrets and variables → Actions → tab Variables**,
senza toccare il file del workflow.

### Lancio a mano

**Dallo Sheet: menu SiBalla → Carica immagini e aggiorna excel e sito.** Lancia il giro veloce ("solo
immagini", vedi sotto) senza entrare su GitHub: è il modo normale dopo aver aggiunto immagini in
`data/immagini` o modificato lo Sheet. Ci mette 5-10 minuti; un avviso in basso a destra conferma
l'avvio.

**Da GitHub: Actions → Importatore eventi → Run workflow**: compare un modulo con le stesse opzioni.
Serve per esempio a caricare lo Sheet su Appwrite dopo averlo modificato (metti "Carica gli eventi
dello Sheet su Appwrite" a `true` e, se non vuoi l'email, "Invia l'email" a `false`).

L'opzione **"Giro veloce: solo immagini"** (`solo_immagini`, quella usata dal menu dello Sheet) fa
solo immagini (data/immagini, siti, loghi) e caricamento sul DB: niente ricerca online, email degli
eventi e indirizzi con Gemini. Non sostituisce il giro completo pianificato ogni ora;
l'email del log arriva solo se va in errore.

A fine giro, veloce o completo, l'importatore avvia il workflow **Sito pubblico**: righe nuove e
immagini compaiono sul sito subito, senza aspettare il giro orario.

La voce di menu sta nello script dello Sheet ([sito/apps-script/AggiornaSito.gs](sito/apps-script/AggiornaSito.gs)),
che non si aggiorna da GitHub: quando cambia il file va ricopiato in **Estensioni → Apps Script**
(incolla tutto, salva, ricarica lo Sheet). Il token è lo stesso di "Aggiorna sito ora" (proprietà
script `GITHUB_TOKEN`, permesso **Actions: Read and write** sul repo).

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

Il segnale è che **nella tab Actions non compaiono più run orari** (l'email da sola non basta:
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


### Importazione oraria: fonti online, Sheet, email, app e sito

Con le impostazioni predefinite, ogni ora al minuto 00 parte il giro completo:

1. Cerca gli eventi sulle fonti online (e-swing e SwingPlanit).
2. Aggiunge allo Sheet gli eventi nuovi, dopo la classificazione; rilegge lo Sheet per includerli
   nel bollettino e nel DB dello stesso giro.
3. Invia l'email soltanto se ci sono eventi nuovi rispetto all'ultimo invio, nei filtri di zona e periodo.
4. Aggiorna il database Appwrite da cui legge l'app.
5. A fine giro avvia il workflow **Sito pubblico**, che legge lo Sheet aggiornato e ripubblica il sito.

Quindi le novità non arrivano soltanto per email: vengono aggiunte allo Sheet e, se pubblicabili,
arrivano anche nell'app e sul sito. Le righe **da verificare** e quelle scartate non vengono pubblicate;
gli eventi passati restano nello storico dello Sheet. Anche modifiche manuali allo Sheet vengono
recepite nei successivi aggiornamenti di app e sito.

Gli avvii automatici possono subire ritardi su GitHub Actions e le importazioni non si sovrappongono.
La pubblicazione oraria del sito al minuto 17 resta attiva anche indipendentemente dall'importatore;
questa non cerca online e non invia email. Le variabili o le opzioni di lancio riportate sopra
permettono di disabilitare separatamente email, scrittura nello Sheet o aggiornamento del DB.

Il bollettino viene inviato solo se ci sono eventi nuovi rispetto all'ultima email, con gli stessi
filtri di zona e periodo. Gli eventi restano raggruppati per città, con il simbolo 🆕 sulle aggiunte
e il riepilogo iniziale delle città interessate. Lo storico degli invii viene conservato; le anteprime
email locali non lo modificano. ENABLE_SENDING disabilita l'invio dei bollettini.
Nei giri automatici l'email tecnica con il log parte solo se l'importatore va in errore;
nei lanci manuali resta disponibile anche a giro riuscito.
