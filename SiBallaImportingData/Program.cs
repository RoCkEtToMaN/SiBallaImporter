// See https://aka.ms/new-console-template for more information

using Google.Apis.Sheets.v4.Data;
using SiBalla.Service;
using SiBallaDataImporting.Utils;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Configuration; // Aggiungi questa direttiva using
using System.Drawing;
using System.Text.Json;

//using Bitmap bmp = new Bitmap(@"Resources\iconWhityLH.png");
//IntPtr hIcon = bmp.GetHicon();
//using Icon icon = Icon.FromHandle(hIcon);
//using var stream = File.Create(@"Resources\icona.ico");

//icon.Save(stream);

// nel punto di avvio dell'import (Program.cs o dove chiami il filtro)
var store = KnownVenueStore.Load(Path.Combine(AppContext.BaseDirectory, "DizionarioEventiInfo.json"));
var resolver = new AddressResolver(store);
Console.WriteLine("Caricamento codici ISTAT in corso...");
Console.WriteLine("Inizializzazione dati geografici...");
await IstatDownloader.InitializeAsync();
//await IstatDownloader.SyncIstatFromOfficialCsvAsync(); // riempie IstatDic, che oggi nessuno legge
//await IstatDownloader.GetIstatCodesFromDBAsync();
await OfflineGeocoder.EnsureGeoNamesFileExistsAsync();
OfflineGeocoder.Initialize("cities15000.txt");

bool enableImportingOnDB = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImportingOnDB"]);
bool enableImportingOnExcel = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImportingOnExcel"]);
bool enableSearching = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSearching"]);
bool enableSending = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSending"]);
bool resetDb = Convert.ToBoolean(ConfigurationManager.AppSettings["ResetDb"]);
//bool clearExcel = Convert.ToBoolean(ConfigurationManager.AppSettings["ClearExcel"]); // flag non più usato
//string databaseUrl = ConfigurationManager.AppSettings["DatabaseUrl"];
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
bool updateSheetCoordinates = Convert.ToBoolean(ConfigurationManager.AppSettings["UpdateSheetCoordinates"]);
if (updateSheetCoordinates && aggiornati.Count > 0)
    await ImportData.UpdateSheetCoordinatesAsync(aggiornati);

try
{
    if (enableSearching)
    {
        eventsMerge.AddRange(await EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing(), store, resolver));

        var undinfed = eventsMerge.Where(x => x.City == "undefined").ToList();
        eventsMerge.AddRange(await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await importDataFromWeb.GetSwingPlanit(), store, resolver));
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

if (enableImportingOnExcel && eventsMergeUfficial.Count() != eventsMerge.Count() || (eventFromExcel[TipoEvento.Serate].Count() == 0 || eventFromExcel[TipoEvento.Workshop].Count() == 0))
    await ImportData.PrepareToAppendToGoogleSheet(eventsMergeUfficial.OrderBy(x => x.DateStart).ToList(), eventFromExcel);

if (enableSending)
    await EmailSender.SendWithMailjetAnonymous(eventsMerge.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))).ToList());

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







