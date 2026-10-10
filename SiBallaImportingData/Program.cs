// See https://aka.ms/new-console-template for more information

using Google.Apis.Sheets.v4.Data;
using SiBalla.Service;
using SiBallaDataImporting.Utils;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Drawing;
using System.Text.Json;

//using Bitmap bmp = new Bitmap(@"Resources\iconWhityLH.png");
//IntPtr hIcon = bmp.GetHicon();
//using Icon icon = Icon.FromHandle(hIcon);
//using var stream = File.Create(@"Resources\icona.ico");

//icon.Save(stream);

// configurazione: appsettings.json -> user-secrets -> variabili d'ambiente -> riga di comando
var settings = AppSettings.Load(args);
DataPaths.Initialize(settings.Paths.DataDirectory);
settings.Print();
var opzioni = settings.Import;

// nel punto di avvio dell'import (Program.cs o dove chiami il filtro)
var store =KnownVenueStore.Load(DataPaths.Get("DizionarioEventiInfo.json"));
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
    trovati.AddRange(await EventFilter.FilterESwingItEventsByDateRange(await web.GetESwing(), store, resolver, opzioni.Radius));
    trovati.AddRange(await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await web.GetSwingPlanit(), store, resolver, opzioni.Radius));

    var daClassificare = trovati.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni)).ToList();
    var (swing, altri) = await SwingEventClassifier.ClassifyAsync(daClassificare, settings.Gemini);

    Console.WriteLine($"\n===== Swing: {swing.Count}/{daClassificare.Count} =====");
    foreach (var ev in swing.OrderBy(x => x.DateStart))
        Console.WriteLine($"✅ {ev.DateStart:yyyy-MM-dd} {ev.City}: {ev.PartyName?.Trim()}");

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
var appWriteService = new AppWriteService();

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
Console.WriteLine($"Voci da compilare aggiunte: {store.AddPlaceholders(daCompilare)}");

// 3. voci verificate con indirizzo ma senza coordinate -> Photon
await resolver.GeocodePendingAsync();

// 4. coordinate migliori del JSON -> eventi dello sheet (in memoria sempre, sullo sheet solo se abilitato)
var aggiornati = store.ApplyToSheetEvents(sheetEvents);
Console.WriteLine($"Eventi dello sheet con coordinate aggiornate: {aggiornati.Count}");
if (opzioni.UpdateSheetCoordinates && aggiornati.Count > 0)
    await ImportData.UpdateSheetCoordinatesAsync(aggiornati);

try
{
    if (enableSearching)
    {
        eventsMerge.AddRange(await EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing(), store, resolver, opzioni.Radius));

        var undinfed = eventsMerge.Where(x => x.City == "undefined").ToList();
        eventsMerge.AddRange(await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await importDataFromWeb.GetSwingPlanit(), store, resolver, opzioni.Radius));
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
}

if (eventsMerge.Any(x => x.City == "undefined"))
    UndefinedCityJosn.ExportEventsToJson(eventsMerge.Where(x => x.City == "undefined").Select(x => x.EventDetail));


var eventsMergeUfficial = new List<Event>();

foreach (var eventToCheck in eventsMerge)
    if (!eventFromExcel[TipoEvento.Serate].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date) 
        && !eventFromExcel[TipoEvento.Workshop].Any(x => x.PartyName == eventToCheck.PartyName && x.DateStart.Date == eventToCheck.DateStart.Date))
        eventsMergeUfficial.Add(eventToCheck);

var countExcel = eventFromExcel[TipoEvento.Serate].Count() + eventFromExcel[TipoEvento.Workshop].Count();

// eventsMergeUfficial = eventi trovati online e non ancora presenti nello Sheet
if (enableImportingOnExcel && eventsMergeUfficial.Count > 0)
    await ImportData.PrepareToAppendToGoogleSheet(eventsMergeUfficial.OrderBy(x => x.DateStart).ToList(), eventFromExcel);

if (enableSending)
{
    var eventiEmail = eventsMerge.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni)).ToList();

    // controllo finale con l'AI: gli eventi non swing finiscono in una sezione separata dell'email
    var (eventiSwing, daVerificare) = await SwingEventClassifier.ClassifyAsync(eventiEmail, settings.Gemini);
    await EmailSender.SendWithMailjetAnonymous(eventiSwing, settings, daVerificare);
}

if (resetDb)
    await appWriteService.ResetDbAsync();

if (enableImportingOnDB)
{
    Console.WriteLine("Inizio Importazione");    
    
    var eventi = await appWriteService.GetEventsAsync(AppWriteService.SerateCollectionId);
    var workshop = await appWriteService.GetEventsAsync(AppWriteService.WorkshopsCollectionId);

    #region Firebase
    //firebaseDbService.InitializeDb();
    //if (eventFromExcel.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)  // QUI PASSO GLI EVENTI DEL EXCEL   
    //    await firebaseDbService.AddEventAsync(JsonSerializer.Serialize(serate));
    //if (eventFromExcel.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
    //    await firebaseDbService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));
    #endregion

    #region  AppWrite
    if (eventFromExcel.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0) 
        await appWriteService.SaveCollectionAsJsonAsync(AppWriteService.SerateCollectionId, serate);
    if (eventFromExcel.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
        await appWriteService.SaveCollectionAsJsonAsync(AppWriteService.WorkshopsCollectionId, workshops);
    #endregion
}







