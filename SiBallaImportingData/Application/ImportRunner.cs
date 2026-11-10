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

        // RESET: svuota lo Sheet dalla riga 3 (intestazioni, formati e macro restano) dopo averne salvato una copia, poi si ferma.
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
            DictionaryRepoSync.PushFromBin(dizionarioPath);
        }

        if (eventiOnline.Any(x => x.City == "undefined"))
            UndefinedCityJosn.ExportEventsToJson(eventiOnline.Where(x => x.City == "undefined").Select(x => x.EventDetail));

        var nuoviEventi = new List<Event>();

        // senza città non vanno nello Sheet (la colonna F vuole "Paese, Città"): li vedi nell'email e li aggiungi tu
        foreach (var eventToCheck in eventiOnline.Where(x => !x.LuogoSconosciuto))
            if (!eventiSheet[TipoEvento.Serate].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date)
                && !eventiSheet[TipoEvento.Workshop].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date))
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

            // il DB è la copia dello Sheet letto all'inizio: rileggo, così le righe appena aggiunte arrivano sul DB
            // in questo giro e non in quello dopo. I luoghi del dizionario vanno riapplicati (in memoria, come al punto 4)
            if (opzioni.EnableImportingOnDB)
            {
                Console.WriteLine("[sheet] rileggo lo Sheet con le righe appena aggiunte, per il caricamento sul DB");
                eventiSheet = ImportData.LoadFile();
                sheetEvents = eventiSheet.Values.SelectMany(x => x).ToList();
                store.ApplyToSheetEvents(sheetEvents);
            }
        }

        if (opzioni.EnableSending)
            await InviaEmailAsync(settings, eventiOnline);

        if (opzioni.ResetDb)
            await appWriteService.ResetDbAsync();

        if (opzioni.EnableImportingOnDB)
            await SincronizzaDatabaseAsync(eventiSheet, sheetEvents, appWriteService);
    }

    private static async Task InviaEmailAsync(AppSettings settings, List<Event> eventiOnline)
    {
        var opzioni = settings.Import;
        var radiusEmail = opzioni.RadiusEmail ?? opzioni.Radius;
        var eventiEmail = eventiOnline
            .Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni))
            .Where(x => EventFilter.IsWithinEmailRadius(x, radiusEmail))
            .ToList();
        Console.WriteLine($"[email] {eventiEmail.Count} eventi entro {radiusEmail} km da Genova nei prossimi {opzioni.Giorni} giorni");

        // giro quotidiano: niente email (e niente Gemini) se non c'è nessun evento nuovo dall'ultima
        var nuovi = UltimaEmail.Nuovi(eventiEmail);
        if (opzioni.EmailSoloSeCambia && nuovi.Count == 0)
        {
            Console.WriteLine($"📭 Nessun evento nuovo rispetto all'ultima email ({eventiEmail.Count} eventi): email non inviata");
        }
        else
        {
            Console.WriteLine($"📬 Eventi nuovi rispetto all'ultima email: {nuovi.Count} su {eventiEmail.Count}");
            // controllo finale con l'AI: gli eventi non swing finiscono in una sezione separata dell'email
            var (eventiSwing, daVerificare) = await SwingEventClassifier.ClassifyAsync(eventiEmail, settings.Gemini);
            // senza città non so se sono vicini: sezione a parte invece del bollettino per città
            var inviata = await EmailSender.SendWithMailjetAnonymous(
                eventiSwing.Where(x => !x.LuogoSconosciuto).ToList(), settings, daVerificare,
                eventiSwing.Where(x => x.LuogoSconosciuto).ToList());
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
        // Le righe "da verificare" (aggiunte dal programma, indirizzo non controllato) restano fuori finché non scrivi "ok".
        // Con lo Sheet vuoto (lettura fallita) non tocco la collezione per non svuotarla
        List<Event> Confermati(List<Event> eventi)
        {
            var ok = eventi.Where(x => x.LuogoVerificato != ImportData.VerifiedPending).ToList();
            if (ok.Count < eventi.Count)
                Console.WriteLine($"⏳ {eventi.Count - ok.Count} righe '{ImportData.VerifiedPending}' non vanno sul DB");
            return ok;
        }
        if (eventiSheet.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)
            await appWriteService.ReplaceCollectionAsJsonAsync(AppWriteService.SerateCollectionId, Confermati(serate));
        if (eventiSheet.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
            await appWriteService.ReplaceCollectionAsJsonAsync(AppWriteService.WorkshopsCollectionId, Confermati(workshops));
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
            await EventImages.BackfillSheetAsync(sheetEvents, appWriteService, opzioni.CercaImmaginiSheetMax);

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
