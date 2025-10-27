// See https://aka.ms/new-console-template for more information

using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Text.Json;

Console.WriteLine("Inizio Importazione");

//var import = new ImportData();

var eventToBeStored = ImportData.LoadFile();

var firestoreService = new FirestoreService();


var eventi = await firestoreService.LoadEventAsync();
var workshop = await firestoreService.LoadWorkshopAsync();


if (eventToBeStored.TryGetValue(TipoEvento.Serate, out var serate) && serate.Count > 0)
{
    await firestoreService.AddEventAsync(JsonSerializer.Serialize(serate));
}
if (eventToBeStored.TryGetValue(TipoEvento.Workshop, out var workshops) && workshops.Count > 0)
{
    await firestoreService.AddWorkshopAsync(JsonSerializer.Serialize(workshops));
}


int i = 0;
