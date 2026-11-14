using SiBalla.Service;
using SiBallaDataImporting.Utils;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;

namespace SiBallaImportingData.Application;

internal static class ImportRunner
{
    public static async Task RunAsync(AppSettings settings)
    {
        var opzioni = settings.Import;

        // RESET: svuota Serate e Workshop dalla riga 3 (intestazioni, formati e macro restano) dopo averle copiate nello Sheet ("Backup ...") e in un JSON, poi si ferma.
        // Su GitHub Actions no: un "ResetExcel": true finito per sbaglio nel repo svuoterebbe lo Sheet a ogni giro
        if (opzioni.ResetExcel && await ImportCommands.ResetSheetAsync())
            return;

        // dizionario dei luoghi: sta accanto all'eseguibile (bin), ma la copia buona è nel repo (data/):
        // la prendo da lì all'avvio e ce la rimetto con un commit dopo il salvataggio
        var dizionarioPath = Path.Combine(AppContext.BaseDirectory, "DizionarioEventiInfo.json");
        DictionaryRepoSync.PullIntoBin(dizionarioPath);
        var store = KnownVenueStore.Load(dizionarioPath);
        var resolver = new AddressResolver(store);
        await InizializzaGeografiaAsync();

        // TEST: conta le immagini trovate senza scrivere su foglio o DB
        // (Import:TestImmagini in appsettings.json, oppure dotnet run -- --test-immagini)
        if (opzioni.TestImmagini)
        {
            await ImportCommands.TestImmaginiAsync(settings, store, resolver);
            return;
        }

        // TEST: classifica con Gemini gli eventi dei prossimi Giorni (quelli dell'email) senza inviare nulla
        // (Import:TestSwing in appsettings.json, oppure dotnet run -- --test-swing)
        if (opzioni.TestSwing)
        {
            await ImportCommands.TestSwingAsync(settings, store, resolver);
            return;
        }

        var appWriteService = new AppWriteService(settings.Appwrite.ApiKey);

        var eventiOnline = new List<Event>();
        var importDataFromWeb = new ImportDataFromWeb();
        var eventiSheet = ImportData.LoadFile();
        var sheetEvents = eventiSheet.Values.SelectMany(x => x).ToList();

        // PULIZIA: righe future dello Sheet che sono lezioni e non eventi (stessa regola del filtro sugli eventi trovati online)
        // --pulisci-lezioni le elenca soltanto, --pulisci-lezioni-scrivi le cancella dallo Sheet. Poi il programma si ferma
        if (opzioni.PulisciLezioni || opzioni.PulisciLezioniScrivi)
        {
            await ImportCommands.PulisciLezioniAsync(opzioni, sheetEvents);
            return;
        }

        // RICLASSIFICA: vecchie righe future "da verificare" senza colore né nota -> Gemini (swing = "nuovo", altri = colorati con il motivo)
        // --riclassifica-sheet mostra soltanto, --riclassifica-sheet-scrivi scrive nello Sheet. Poi il programma si ferma
        if (opzioni.RiclassificaSheet || opzioni.RiclassificaSheetScrivi)
        {
            await ImportCommands.RiclassificaSheetAsync(settings, sheetEvents);
            return;
        }

        // formule dell'intestazione bloccate da valori scritti sotto (#REF!): li svuoto, solo nei giri che scrivono sullo Sheet
        if (opzioni.EnableImportingOnExcel)
        {
            try
            {
                await ImportData.SbloccaFormuleIntestazioneAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sheet] ⚠️ controllo delle formule dell'intestazione non riuscito: {ex.Message}");
            }
        }

        // 1. righe con indirizzo -> luoghi verificati
        store.ImportFromSheet(sheetEvents);
        //    colonna H -> stili di eventi e organizzatori (escluse le celle con lo stile dedotto non ancora confermato)
        if (await ImportData.LeggiStiliDedottiAsync(sheetEvents))
            store.ImparaStili(sheetEvents);

        // 2. righe future senza indirizzo e senza luogo noto -> voci da compilare
        var daCompilare = sheetEvents
            .Where(e => e.DateStart.Date >= DateTime.Today)
            .Where(e => string.IsNullOrWhiteSpace(e.EventDetail?.Address))
            .Where(e => store.Find(e) is null)
            .ToList();

        // TEST: cerca con Gemini il locale degli eventi da compilare e lo verifica con Photon, senza scrivere su JSON/foglio/DB
        // (Import:TestIndirizzi in appsettings.json, oppure dotnet run -- --test-indirizzi)
        if (opzioni.TestIndirizzi)
        {
            await ImportCommands.TestIndirizziAsync(settings, daCompilare, resolver);
            return;
        }

        await AggiornaLuoghiEImmaginiAsync(settings, store, resolver, sheetEvents, appWriteService);

        StampaIndirizziMancanti(sheetEvents);

        try
        {
            if (opzioni.EnableSearching)
            {
                eventiOnline.AddRange(await DaFonte("e-swing", async () =>
                    await EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing(), store, resolver, opzioni.Radius)));
                eventiOnline.AddRange(await DaFonte("SwingPlanit", async () =>
                    await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await importDataFromWeb.GetSwingPlanit(), store, resolver, opzioni.Radius)));

            }
        }
        finally
        {
            // 3. scrivo il file, anche se l'import va in errore a metà
            store.Save();
            DictionaryRepoSync.PushFromBin(dizionarioPath, opzioni.NoPush);
        }

        if (eventiOnline.Any(x => x.City == "undefined"))
            UndefinedCityJosn.ExportEventsToJson(eventiOnline.Where(x => x.City == "undefined").Select(x => x.EventDetail));

        var nuoviEventi = new List<Event>();

        // senza città non vanno nello Sheet (la colonna F vuole "Paese, Città"): li vedi nell'email e li aggiungi tu
        foreach (var eventToCheck in eventiOnline.Where(x => !x.LuogoSconosciuto))
            if (!NelloSheet(eventiSheet, eventToCheck))
                nuoviEventi.Add(eventToCheck);

        // per il bot Telegram (AgenteRust, comando /importa): il calendario dello Sheet, nello stesso formato
        // che va su Appwrite, più gli eventi trovati online e non ancora nello Sheet. Lo scrivo sempre, anche
        // senza import su DB: su GitHub finisce negli artifact del run (out/*.json) e il bot lo scarica da lì
        EventiPerBot.Salva(DataPaths.Get("eventi-bot.json"), eventiSheet, nuoviEventi);

        // nuoviEventi = eventi trovati online e non ancora presenti nello Sheet
        if (opzioni.EnableImportingOnExcel && nuoviEventi.Count > 0)
        {
            // Gemini decide lo stato delle righe: swing -> "nuovo" (va sul DB), gli altri -> "da verificare" colorati.
            // La classificazione resta in memoria e l'email più sotto la riusa
            await SwingEventClassifier.SegnaPerSheetAsync(nuoviEventi, settings.Gemini);
            // senza stile dalla fonte: dizionario, Gemini (stessa risposta di sopra) o parole chiave -> cella H lilla da confermare
            StiliEvento.Completa(nuoviEventi, store);
            // immagine su Storage prima di scrivere la riga: il link online (es. Facebook) scade, la copia no
            await EventImages.UploadAsync(nuoviEventi, appWriteService);
            await ImportData.PrepareToAppendToGoogleSheet([.. nuoviEventi.OrderBy(x => x.DateStart)], eventiSheet);

            // DB ed email partono dallo Sheet letto all'inizio: rileggo, così le righe appena aggiunte arrivano
            // in questo giro e non in quello dopo. I luoghi del dizionario vanno riapplicati (in memoria, come al punto 4)
            if (opzioni.EnableImportingOnDB || opzioni.EnableSending)
            {
                Console.WriteLine("[sheet] rileggo lo Sheet con le righe appena aggiunte, per DB ed email");
                eventiSheet = ImportData.LoadFile();
                sheetEvents = eventiSheet.Values.SelectMany(x => x).ToList();
                store.ApplyToSheetEvents(sheetEvents);
            }
        }

        if (opzioni.EnableSending)
            await InviaEmailAsync(settings, eventiSheet, sheetEvents, eventiOnline);

        if (opzioni.ResetDb)
            await appWriteService.ResetDbAsync();

        if (opzioni.EnableImportingOnDB)
            await SincronizzaDatabaseAsync(eventiSheet, sheetEvents, appWriteService);
    }

    // stesso confronto dell'import: nome della serata + giorno
    private static bool NelloSheet(Dictionary<TipoEvento, List<Event>> eventiSheet, Event ev) =>
        eventiSheet.Values.Any(lista => lista.Any(x => x.PartyName == ev.PartyName && x.DateStart.Date == ev.DateStart.Date));

    /// <summary>
    /// Il bollettino viene dallo Sheet (il calendario curato: indirizzi corretti, righe aggiunte a mano).
    /// Dagli eventi online prendo solo quelli che nello Sheet non ci sono: senza città (non ci vanno mai)
    /// o trovati con l'import sullo Sheet spento
    /// </summary>
    private static async Task InviaEmailAsync(AppSettings settings, Dictionary<TipoEvento, List<Event>> eventiSheet,
        List<Event> sheetEvents, List<Event> eventiOnline)
    {
        var opzioni = settings.Import;
        var radiusEmail = opzioni.RadiusEmail ?? opzioni.Radius;
        bool NelPeriodo(Event x) => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni);

        // Sheet: le righe "da verificare" (non ancora controllate da te) vanno nella sezione a parte, non nel bollettino;
        // le righe "no" (scartate da te) non compaiono
        var righeSheet = sheetEvents
            .Where(x => !ImportData.Scartato(x))
            .Where(NelPeriodo)
            .Where(x => EventFilter.IsWithinEmailRadius(x, radiusEmail))
            .ToList();
        var bollettino = righeSheet.Where(x => x.LuogoVerificato != ImportData.VerifiedPending).ToList();
        var daVerificare = righeSheet
            .Where(x => x.LuogoVerificato == ImportData.VerifiedPending)
            .Select(x => (Evento: x, Motivo: x.MotivoVerifica ?? $"riga '{ImportData.VerifiedPending}' nello Sheet"))
            .ToList();

        // online e non nello Sheet
        var fuoriSheet = eventiOnline.Where(NelPeriodo).Where(x => !NelloSheet(eventiSheet, x)).ToList();
        // senza città non so se sono vicini: niente filtro sul raggio, sezione a parte invece del bollettino per città
        var luogoSconosciuto = fuoriSheet.Where(x => x.LuogoSconosciuto).ToList();
        var altriOnline = fuoriSheet
            .Where(x => !x.LuogoSconosciuto)
            .Where(x => EventFilter.IsWithinEmailRadius(x, radiusEmail))
            .ToList();

        var eventiEmail = bollettino.Concat(daVerificare.Select(x => x.Evento)).Concat(altriOnline).Concat(luogoSconosciuto).ToList();
        Console.WriteLine($"[email] entro {radiusEmail} km da Genova nei prossimi {opzioni.Giorni} giorni: " +
            $"{bollettino.Count} dallo Sheet, {daVerificare.Count} da verificare nello Sheet, " +
            $"{altriOnline.Count} online non nello Sheet, {luogoSconosciuto.Count} con luogo sconosciuto");

        // giro quotidiano: niente email (e niente Gemini) se non c'è nessun evento nuovo dall'ultima
        var nuovi = UltimaEmail.Nuovi(eventiEmail);
        if (opzioni.EmailSoloSeCambia && nuovi.Count == 0)
        {
            Console.WriteLine($"📭 Nessun evento nuovo rispetto all'ultima email ({eventiEmail.Count} eventi): email non inviata");
        }
        else
        {
            Console.WriteLine($"📬 Eventi nuovi rispetto all'ultima email: {nuovi.Count} su {eventiEmail.Count}");
            // le righe dello Sheet le hai già curate tu; l'AI controlla solo gli eventi online rimasti fuori
            if (altriOnline.Count > 0)
            {
                var (swingOnline, nonSwingOnline) = await SwingEventClassifier.ClassifyAsync(altriOnline, settings.Gemini);
                bollettino.AddRange(swingOnline);
                daVerificare.AddRange(nonSwingOnline);
            }
            var inviata = await EmailSender.SendWithMailjetAnonymous(bollettino, settings, daVerificare, luogoSconosciuto);
            // se non è partita gli eventi restano "nuovi" e il programma esce con errore:
            // su GitHub il run fallisce (arriva la mail) e il tentativo dell'ora dopo la rimanda
            if (inviata)
                UltimaEmail.Ricorda(eventiEmail);
            else
                Environment.ExitCode = 1;
        }
    }

    private static async Task SincronizzaDatabaseAsync(Dictionary<TipoEvento, List<Event>> eventiSheet, List<Event> sheetEvents, AppWriteService appWriteService)
    {
        Console.WriteLine("Inizio Importazione");

        // "sì" nella colonna Immagine -> link di Storage in EventDetail.ImageUrl
        await EventImages.ApplyToSheetEventsAsync(sheetEvents, appWriteService);

        // il DB è la copia dello Sheet: sostituisco tutto, così arrivano anche modifiche e cancellazioni.
        // Le righe "da verificare" (aggiunte dal programma, indirizzo non controllato) restano fuori finché non scrivi "ok",
        // le righe "no" (scartate da te) sempre.
        // Con lo Sheet vuoto (lettura fallita) non tocco la collezione per non svuotarla
        List<Event> Confermati(List<Event> eventi)
        {
            // lo Sheet tiene anche lo storico: all'app servono solo gli eventi non ancora finiti
            bool Finito(Event x) => (x.DateFinish ?? x.DateStart).Date < DateTime.Today;
            int passati = eventi.Count(Finito);
            if (passati > 0)
                Console.WriteLine($"📜 {passati} righe di eventi già finiti restano nello Sheet ma non vanno sul DB");
            eventi = eventi.Where(x => !Finito(x)).ToList();
            int inAttesa = eventi.Count(x => x.LuogoVerificato == ImportData.VerifiedPending);
            int scartati = eventi.Count(ImportData.Scartato);
            var ok = eventi.Where(x => x.LuogoVerificato != ImportData.VerifiedPending && !ImportData.Scartato(x)).ToList();
            if (inAttesa > 0)
                Console.WriteLine($"⏳ {inAttesa} righe '{ImportData.VerifiedPending}' non vanno sul DB");
            if (scartati > 0)
                Console.WriteLine($"🚫 {scartati} righe '{ImportData.VerifiedNo}' (scartate) non vanno sul DB");
            return ok;
        }
        if (eventiSheet.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)
            await SostituisciSeNonVuota(AppWriteService.SerateCollectionId, Confermati(serate));
        if (eventiSheet.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
            await SostituisciSeNonVuota(AppWriteService.WorkshopsCollectionId, Confermati(workshops));

        // Sheet pieno ma nessuna riga confermata (tutte "da verificare" o "no"): una lista vuota sul DB
        // lascerebbe l'app senza eventi, quindi tengo i dati del giro prima
        async Task SostituisciSeNonVuota(string collectionId, List<Event> confermati)
        {
            if (confermati.Count == 0)
            {
                Console.WriteLine($"⚠️ '{collectionId}': nessuna riga confermata nello Sheet, il DB non viene toccato (restano i dati precedenti)");
                return;
            }
            await appWriteService.ReplaceCollectionAsJsonAsync(collectionId, confermati);
        }
    }

    // una fonte che non risponde (sito giù, timeout) non deve fermare il giro: si va avanti con le altre
    private static async Task<List<Event>> DaFonte(string nome, Func<Task<List<Event>>> leggi)
    {
        try
        {
            return await leggi();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ {nome}: {ex.GetType().Name}: {ex.Message} -> eventi {nome} saltati");
            return [];
        }
    }

    private static async Task InizializzaGeografiaAsync()
    {
        Console.WriteLine("Caricamento codici ISTAT in corso...");
        Console.WriteLine("Inizializzazione dati geografici...");
        await IstatDownloader.InitializeAsync();
        await OfflineGeocoder.EnsureGeoNamesFileExistsAsync();
        OfflineGeocoder.Initialize(DataPaths.Get("cities15000.txt"));
    }

    private static async Task AggiornaLuoghiEImmaginiAsync(AppSettings settings, KnownVenueStore store, AddressResolver resolver, List<Event> sheetEvents, AppWriteService appWriteService)
    {
        var opzioni = settings.Import;
        // 3. indirizzi nuovi o cambiati -> Photon
        await resolver.GeocodePendingAsync();
        //    default con le coordinate iniziali -> via di quelle coordinate (Photon reverse)
        await resolver.ReverseGeocodeDefaultsAsync();

        // 4. luoghi del JSON -> eventi dello sheet: indirizzo copiato dalle altre date dello stesso evento e coordinate di Photon
        //    (in memoria sempre, così vanno sul DB; sullo sheet solo se abilitato, altrimenti è un'anteprima)
        var aggiornati = store.ApplyToSheetEvents(sheetEvents);
        Console.WriteLine($"\nEventi dello sheet aggiornati dal dizionario: {aggiornati.Count} (indirizzo copiato: {aggiornati.Count(x => x.IndirizzoDalDizionario)})");
        if (aggiornati.Count > 0)
            await ImportData.UpdateSheetLocationsAsync(aggiornati, write: opzioni.UpdateSheetCoordinates);

        // righe dello sheet senza immagine: la cerco sul sito della riga (così arriva sul DB già in questo giro)
        // righe future senza indirizzo: locale cercato da Gemini e confermato da Photon, scritto nello Sheet come "gemini"
        if (opzioni.CercaIndirizzi)
            await IndirizziGemini.CercaAsync(sheetEvents, store, resolver, settings.Gemini, opzioni.CercaIndirizziMax);

        if (opzioni.CercaImmaginiSheet)
        {
            // prima quelle messe a mano (data/immagini): la riga diventa "sì" e la ricerca sul sito la salta
            await EventImages.ApplyManualImagesAsync(sheetEvents, appWriteService);
            await EventImages.BackfillSheetAsync(sheetEvents, appWriteService, opzioni.CercaImmaginiSheetMax);
            // righe rimaste senza immagine ("no"): logo dell'organizzatore, se c'è in data/loghi
            await EventImages.ApplyLogosAsync(sheetEvents, appWriteService);
        }

        // righe colorate "da verificare" che hai controllato (scritto "ok" o svuotato la cella): via colore e nota
        try
        {
            await ImportData.TogliEvidenziazioniAsync(sheetEvents);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sheet] ⚠️ colore delle righe controllate non tolto: {ex.Message}");
        }
    }

    private static void StampaIndirizziMancanti(List<Event> sheetEvents)
    {
        // 5. report: eventi futuri che si ripetono e non hanno ancora un indirizzo, i più frequenti prima
        //    (basta scriverlo in una riga e il prossimo giro lo copia sulle altre)
        var ricorrentiSenzaIndirizzo = sheetEvents
            .Where(e => e.DateStart.Date >= DateTime.Today && string.IsNullOrWhiteSpace(e.EventDetail?.Address))
            .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
            .GroupBy(e => KnownVenueStore.MakeKey(e.Organizer!, e.City!) + "|" + KnownVenueStore.NormalizeTitle(e.PartyName))
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToList();
        Console.WriteLine($"\n===== Eventi ricorrenti senza indirizzo: {ricorrentiSenzaIndirizzo.Count} (coprono {ricorrentiSenzaIndirizzo.Sum(g => g.Count())} date future) =====");
        foreach (var g in ricorrentiSenzaIndirizzo.Take(20))
        {
            var first = g.OrderBy(e => e.DateStart).First();
            Console.WriteLine($"  {g.Count(),3} date | {first.SheetName} riga {first.SheetRow} | {first.Organizer?.Trim()} ({first.City}) | {first.PartyName?.Trim()}");
        }
    }
}
