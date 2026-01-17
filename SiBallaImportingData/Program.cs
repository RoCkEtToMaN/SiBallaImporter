// See https://aka.ms/new-console-template for more information

using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Configuration; // Aggiungi questa direttiva using
using System.Text.Json;        

bool enableImporting = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableImporting"]);
bool enableSearching = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSearching"]);

var eventi = new List<Event>();

if (enableSearching)
{
    var importDataFromWeb = new ImportDataFromWeb();
    eventi = EventFilter.FilterESwingItEventsByDateRange(await importDataFromWeb.GetESwing());

    await EmailSender.SendWithMailjetAnonymous(eventi);
}

if (enableImporting)
{
    Console.WriteLine("Inizio Importazione");

    var eventToBeStored = ImportData.LoadFile();
    var firestoreService = new FirestoreService();
    eventi = await firestoreService.LoadEventAsync();
    var workshop = await firestoreService.LoadWorkshopAsync();

    if (eventToBeStored.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)
    {
        await firestoreService.AddEventAsync(JsonSerializer.Serialize(serate));
    }
    if (eventToBeStored.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
    {
        await firestoreService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));
    }
}






  
