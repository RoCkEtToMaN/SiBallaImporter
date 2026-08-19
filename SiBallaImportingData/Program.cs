// See https://aka.ms/new-console-template for more information

using Google.Apis.Sheets.v4.Data;
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

Console.WriteLine("Caricamento codici ISTAT in corso...");
Console.WriteLine("Inizializzazione dati geografici...");
//await IstatSeeder.SeedIstatCodesToFirebaseAsync();
//await LocationHelper.InitIstatFromFirebaseAsync();
await IstatDownloader.InitializeAsync();
await IstatDownloader.LoadCacheAsync();
await IstatDownloader.SyncIstatFromOfficialCsvAsync();
await IstatDownloader.GetIstatCodesAsync();
//var dicComuni = await IstatDownloader.GetIstatCodesAsync();
//await IstatDownloader.SyncIstatFromOfficialCsvAsync();



// Esempio di utilizzo della normalizzazione
//var (city, state, country) = LocationHelper.NormalizeLocation("Liguria", "16044", "Italia");

//Console.WriteLine($"Risultato: City={city}, State={state}, Country={country}");
// Output: City=Genova, State=Liguria, Country=Italia

bool enableImportingOnDB = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImportingOnDB"]);
bool enableImportingOnExcel = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImportingOnExcel"]);
bool enableSearching = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSearching"]);
bool enableSending = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSending"]);
bool resetDb = Convert.ToBoolean(ConfigurationManager.AppSettings["ResetDb"]);
string databaseUrl = ConfigurationManager.AppSettings["DatabaseUrl"];

//ImportData.TestReadWritePermissions();

//ImportData.WriteExcelFile();
var eventiESwing = new List<Event>();
var swingPlanitEvent = new List<Event>();
var eventsMerge = new List<Event>();
var importDataFromWeb = new ImportDataFromWeb();
//var test = await importDataFromWeb.GetSwingPlanit();
//var eventsOnExcelFile = ImportData.LoadFile();


if (enableSearching)
{
    //var eSwingEvents = EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing());
    eventsMerge.AddRange(EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing()));
    //eventiESwing = eSwingEvents.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))).ToList();

    eventsMerge.AddRange(EventFilter.FilterSwingPlanitEventsByDateRange(await importDataFromWeb.GetSwingPlanit()));
}

if(enableImportingOnExcel)
    await ImportData.PrepareToAppendToGoogleSheet(eventsMerge.OrderBy(x => x.DateStart).ToList());
//ImportData.AppendToGoogleSheet(eventiESwing);

if (enableSending)
    await EmailSender.SendWithMailjetAnonymous(eventsMerge.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))).ToList());

if (enableImportingOnDB)
{
    Console.WriteLine("Inizio Importazione");

    //var eventToBeStored = ImportData.LoadFile();
    var firebaseDbService = new FirebaseDbService(databaseUrl);

    if(resetDb)
        await firebaseDbService.ResetDb();
   

    //firebaseDbService.InitializeDb();
    var eventi = await firebaseDbService.LoadEventAsync();
    var workshop = await firebaseDbService.LoadWorkshopAsync();
    //await importDataFromWeb.GetCoordinatesFromCityAndNameLocation("New York", "NY", "Central Park");

    //if (eventsOnExcelFile.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)  // QUI PASSO GLI EVENTI DEL EXCEL   
    //    await firebaseDbService.AddEventAsync(JsonSerializer.Serialize(serate));    
    //if (eventsOnExcelFile.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)    
    //    await firebaseDbService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));    
}






  
