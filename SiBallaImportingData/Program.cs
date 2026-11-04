// See https://aka.ms/new-console-template for more information

using Google.Apis.Sheets.v4.Data;
using SiBalla.Service;
using SiBallaDataImporting.Utils;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Drawing;
using System.Globalization;
using System.Text.Json;

// date e numeri dello Sheet sono in formato italiano (15/10/2026): stessa cultura su Windows, Mac e runner Linux
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("it-IT");
CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("it-IT");

// configurazione: appsettings.json -> user-secrets -> variabili d'ambiente -> riga di comando
var settings = AppSettings.Load(args);
DataPaths.Initialize(settings.Paths.DataDirectory);
settings.Print();
var opzioni = settings.Import;

// dizionario dei luoghi: sta accanto all'eseguibile (bin), ma la copia buona è nel repo (data/):
// la prendo da lì all'avvio e ce la rimetto con un commit dopo il salvataggio
var dizionarioPath = Path.Combine(AppContext.BaseDirectory, "DizionarioEventiInfo.json");
DictionaryRepoSync.PullIntoBin(dizionarioPath);
var store =KnownVenueStore.Load(dizionarioPath);
var resolver = new AddressResolver(store);
Console.WriteLine("Caricamento codici ISTAT in corso...");
Console.WriteLine("Inizializzazione dati geografici...");
await IstatDownloader.InitializeAsync();
//await IstatDownloader.SyncIstatFromOfficialCsvAsync(); // riempie IstatDic, che oggi nessuno legge
//await IstatDownloader.GetIstatCodesFromDBAsync();
await OfflineGeocoder.EnsureGeoNamesFileExistsAsync();
OfflineGeocoder.Initialize(DataPaths.Get("cities15000.txt"));

// TEST: conta le immagini trovate senza scrivere su foglio o DB
// (Import:TestImmagini in appsettings.json, oppure dotnet run -- --test-immagini)
if (opzioni.TestImmagini)
{
    var web = new ImportDataFromWeb();
    var testESwing = await EventFilter.FilterESwingItEventsByDateRange(await web.GetESwing(), store, resolver, opzioni.Radius);
    var testSwingPlanit = await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await web.GetSwingPlanit(), store, resolver, opzioni.Radius);

    void Report(string fonte, List<Event> eventi)
    {
        var conImmagine = eventi.Where(x => !string.IsNullOrEmpty(x.EventDetail?.ImageUrl)).ToList();
        Console.WriteLine($"\n===== {fonte}: {conImmagine.Count}/{eventi.Count} eventi con immagine =====");
        foreach (var ev in eventi)
            Console.WriteLine($"{(string.IsNullOrEmpty(ev.EventDetail?.ImageUrl) ? "❌" : "✅")} {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> {ev.EventDetail?.ImageUrl ?? ev.EventDetail?.WebSite}");
    }

    Report("e-swing", testESwing);
    Report("SwingPlanit", testSwingPlanit);
    return;
}

// TEST: classifica con Gemini gli eventi dei prossimi Giorni (quelli dell'email) senza inviare nulla
// (Import:TestSwing in appsettings.json, oppure dotnet run -- --test-swing)
if (opzioni.TestSwing)
{
    var web = new ImportDataFromWeb();
    var trovati = new List<Event>();
    // solo i prossimi Giorni: e-swing arriva a un anno avanti, e cercare immagini e indirizzi per tutti richiede ore
    var fine = DateTime.Today.AddDays(opzioni.Giorni + 1);
    trovati.AddRange(await EventFilter.FilterESwingItEventsByDateRange((await web.GetESwing()).Where(x => x.Start < fine).ToList(), store, resolver, opzioni.Radius));
    trovati.AddRange(await EventFilter.FilterSwingPlanitEventsByDateRangeAsync((await web.GetSwingPlanit()).Where(x => x.DateStart < fine).ToList(), store, resolver, opzioni.Radius));

    var daClassificare = trovati.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni)).ToList();
    var (swing, altri) = await SwingEventClassifier.ClassifyAsync(daClassificare, settings.Gemini);

    // stesse sezioni dell'email: bollettino (⚠️ = centro città) e luogo da verificare
    var bollettino = swing.Where(x => !x.LuogoSconosciuto).ToList();
    Console.WriteLine($"\n===== Swing: {bollettino.Count}/{daClassificare.Count} =====");
    foreach (var ev in bollettino.OrderBy(x => x.DateStart))
        Console.WriteLine($"{(ev.ProblemaLuogo is null ? "✅" : "⚠️")} {ev.DateStart:yyyy-MM-dd} {ev.City}: {ev.PartyName?.Trim()}{(ev.ProblemaLuogo is null ? "" : " -> " + ev.ProblemaLuogo)}");

    var senzaLuogo = swing.Where(x => x.LuogoSconosciuto).ToList();
    Console.WriteLine($"\n===== Luogo da verificare: {senzaLuogo.Count}/{daClassificare.Count} =====");
    foreach (var ev in senzaLuogo.OrderBy(x => x.DateStart))
        Console.WriteLine($"📍 {ev.DateStart:yyyy-MM-dd} {ev.Country}: {ev.PartyName?.Trim()} -> {ev.ProblemaLuogo} ({ev.EventDetail?.WebSite})");

    Console.WriteLine($"\n===== Da verificare: {altri.Count}/{daClassificare.Count} =====");
    foreach (var (ev, motivo) in altri.OrderBy(x => x.Evento.DateStart))
        
        Console.WriteLine($"❓ {ev.DateStart:yyyy-MM-dd} {ev.City}: {ev.PartyName?.Trim()} -> {motivo}");
    return;
}

bool enableImportingOnDB = opzioni.EnableImportingOnDB;
bool enableImportingOnExcel = opzioni.EnableImportingOnExcel;
bool enableSearching = opzioni.EnableSearching;
bool enableSending = opzioni.EnableSending;
bool resetDb = opzioni.ResetDb;
//var firebaseDbService = new FirebaseDbService(databaseUrl);
var appWriteService = new AppWriteService(settings.Appwrite.ApiKey);

var eventiESwing = new List<Event>();
var swingPlanitEvent = new List<Event>();
var eventsMerge = new List<Event>();
var importDataFromWeb = new ImportDataFromWeb();
var eventFromExcel = ImportData.LoadFile();
var sheetEvents = eventFromExcel.Values.SelectMany(x => x).ToList();

// 1. righe con indirizzo -> luoghi verificati
store.ImportFromSheet(sheetEvents);

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
    var campione = daCompilare
        .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
        .GroupBy(e => KnownVenueStore.MakeKey(e.Organizer!, e.City!) + "|" + KnownVenueStore.NormalizeTitle(e.PartyName))
        .Select(g => g.OrderBy(e => e.DateStart).First())
        .Where(e => settings.Gemini.UseGoogleSearch || !string.IsNullOrWhiteSpace(e.EventDetail?.WebSite))   // senza ricerca Google serve il sito
        .OrderBy(e => e.DateStart)
        .Take(opzioni.TestIndirizziMax)
        .ToList();

    Console.WriteLine($"\n===== Ricerca indirizzi con Gemini: {campione.Count} eventi (su {daCompilare.Count} da compilare) =====");
    int trovati = 0, confermati = 0;
    foreach (var ev in campione)
    {
        Console.WriteLine($"\n🔎 {ev.DateStart:yyyy-MM-dd} {ev.City} | {ev.Organizer?.Trim()} | {ev.PartyName?.Trim()}");
        try
        {
            var r = await VenueAddressFinder.FindAsync(ev, settings.Gemini);
            if (r is null || !r.Found)
            {
                Console.WriteLine($"   ❌ Gemini: non trovato{(r?.Motivo is null ? "" : " - " + r.Motivo)}");
            }
            else
            {
                trovati++;
                Console.WriteLine($"   🤖 {r.VenueName}, {r.Address}, {r.City} (confidenza {r.Confidence})");
                Console.WriteLine($"      fonte: {r.SourceUrl} - {r.Motivo}");

                var coords = await resolver.GeocodeAsync(r.Address!, r.City ?? ev.City, ev.Country);
                if (coords is null)
                    Console.WriteLine("   ⚠️ Photon: indirizzo non trovato entro 10 km dalla città");
                else
                {
                    confermati++;
                    Console.WriteLine($"   ✅ Photon: {coords.Value.Lat:F7}, {coords.Value.Lng:F7}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❗ errore: {ex.Message}");
        }

        await Task.Delay(4000);   // piano gratuito: poche richieste al minuto
    }

    Console.WriteLine($"\n===== Gemini ha trovato {trovati}/{campione.Count}, confermati da Photon {confermati} =====");
    return;
}
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
if (opzioni.CercaImmaginiSheet)
{
    await EventImages.BackfillSheetAsync(sheetEvents, appWriteService, opzioni.CercaImmaginiSheetMax);
    // righe rimaste senza immagine ("no"): logo dell'organizzatore, se c'è in data/loghi
    await EventImages.ApplyLogosAsync(sheetEvents, appWriteService);
}

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

try
{
    if (enableSearching)
    {
        eventsMerge.AddRange(await DaFonte("e-swing", async () =>
            await EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing(), store, resolver, opzioni.Radius)));
        var undinfed = eventsMerge.Where(x => x.City == "undefined").ToList();
        eventsMerge.AddRange(await DaFonte("SwingPlanit", async () =>
            await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await importDataFromWeb.GetSwingPlanit(), store, resolver, opzioni.Radius)));
        undinfed = eventsMerge.Where(x => x.City == "undefined").ToList();


        //var daCompilare = eventsMerge.Where(e => store.Find(e) is null).ToList();
        //var n2 = store.AddPlaceholders(daCompilare);
        //Console.WriteLine($"Voci da compilare aggiunte al JSON: {n2}");
    }
}
finally
{
    // 3. scrivo il file, anche se l'import va in errore a metà
    store.Save();
    DictionaryRepoSync.PushFromBin(dizionarioPath);
}

if (eventsMerge.Any(x => x.City == "undefined"))
    UndefinedCityJosn.ExportEventsToJson(eventsMerge.Where(x => x.City == "undefined").Select(x => x.EventDetail));


var eventsMergeUfficial = new List<Event>();

// senza città non vanno nello Sheet (la colonna F vuole "Paese, Città"): li vedi nell'email e li aggiungi tu
foreach (var eventToCheck in eventsMerge.Where(x => !x.LuogoSconosciuto))
    if (!eventFromExcel[TipoEvento.Serate].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date) 
        && !eventFromExcel[TipoEvento.Workshop].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date))
        eventsMergeUfficial.Add(eventToCheck);

var countExcel = eventFromExcel[TipoEvento.Serate].Count() + eventFromExcel[TipoEvento.Workshop].Count();

// per il bot Telegram (AgenteRust, comando /importa): il calendario dello Sheet, nello stesso formato
// che va su Appwrite, più gli eventi trovati online e non ancora nello Sheet. Lo scrivo sempre, anche
// senza import su DB: su GitHub finisce negli artifact del run (out/*.json) e il bot lo scarica da lì
EventiPerBot.Salva(DataPaths.Get("eventi-bot.json"), eventFromExcel, eventsMergeUfficial);

// eventsMergeUfficial = eventi trovati online e non ancora presenti nello Sheet
if (enableImportingOnExcel && eventsMergeUfficial.Count > 0)
{
    // immagine su Storage prima di scrivere la riga: il link online (es. Facebook) scade, la copia no
    await EventImages.UploadAsync(eventsMergeUfficial, appWriteService);
    await ImportData.PrepareToAppendToGoogleSheet([.. eventsMergeUfficial.OrderBy(x => x.DateStart)], eventFromExcel);
}

if (enableSending)
{
    var eventiEmail = eventsMerge.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni)).ToList();

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

if (resetDb)
    await appWriteService.ResetDbAsync();

if (enableImportingOnDB)
{
    Console.WriteLine("Inizio Importazione");

    #region Firebase
    //firebaseDbService.InitializeDb();
    //if (eventFromExcel.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)  // QUI PASSO GLI EVENTI DEL EXCEL   
    //    await firebaseDbService.AddEventAsync(JsonSerializer.Serialize(serate));
    //if (eventFromExcel.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
    //    await firebaseDbService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));
    #endregion

    #region  AppWrite
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
    if (eventFromExcel.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)
        await appWriteService.ReplaceCollectionAsJsonAsync(AppWriteService.SerateCollectionId, Confermati(serate));
    if (eventFromExcel.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
        await appWriteService.ReplaceCollectionAsJsonAsync(AppWriteService.WorkshopsCollectionId, Confermati(workshops));
    #endregion
}








// una fonte che non risponde (sito giù, timeout) non deve fermare il giro: si va avanti con le altre
static async Task<List<Event>> DaFonte(string nome, Func<Task<List<Event>>> leggi)
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
