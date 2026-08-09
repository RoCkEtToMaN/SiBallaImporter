// See https://aka.ms/new-console-template for more information

using Google.Apis.Sheets.v4.Data;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Configuration; // Aggiungi questa direttiva using
using System.Text.Json;        

bool enableImporting = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImporting"]);
bool enableSearching = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSearching"]);
bool enableSending = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSending"]);
bool resetDb = Convert.ToBoolean(ConfigurationManager.AppSettings["ResetDb"]);
string databaseUrl = ConfigurationManager.AppSettings["DatabaseUrl"];

//ImportData.TestReadWritePermissions();

//ImportData.WriteExcelFile();
var eventiESwing = new List<Event>();
var importDataFromWeb = new ImportDataFromWeb();
//var test = await importDataFromWeb.GetSwingPlanit();



if (enableSearching)
    eventiESwing = EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing());


//ImportData.AppendToGoogleSheet();

if (enableSending)
    await EmailSender.SendWithMailjetAnonymous(eventiESwing);

if (enableImporting)
{
    Console.WriteLine("Inizio Importazione");

    var eventToBeStored = ImportData.LoadFile();
    var firebaseDbService = new FirebaseDbService(databaseUrl);

    if(resetDb)
        await firebaseDbService.ResetDb();
   

    //firebaseDbService.InitializeDb();
    var eventi = await firebaseDbService.LoadEventAsync();
    var workshop = await firebaseDbService.LoadWorkshopAsync();
    await importDataFromWeb.GetCoordinatesFromCityAndNameLocation("New York", "NY", "Central Park");

    if (eventToBeStored.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)  // QUI PASSO GLI EVENTI DEL EXCEL   
        await firebaseDbService.AddEventAsync(JsonSerializer.Serialize(serate));    
    if (eventToBeStored.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)    
        await firebaseDbService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));    
}






  
