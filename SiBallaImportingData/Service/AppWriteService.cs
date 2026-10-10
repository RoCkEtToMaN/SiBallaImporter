using Appwrite;
using Appwrite.Models;
using Appwrite.Services;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Numeric;
using SiBallaImportingData.Model;
using System.Text.Json;

namespace SiBalla.Service;

public class AppWriteService
{
    private readonly Client _client;
    public Databases Databases { get; private set; }

    private const string Endpoint = "https://cloud.appwrite.io/v1";
    private const string ProjectId = "6aa5651e00042e1452be";

    public const string DatabaseId = "6aa638b50000f24a7b92";
    public const string SerateCollectionId = "serate";
    public const string WorkshopsCollectionId = "workshop";

    public AppWriteService()
    {
        _client = new Client()
            .SetEndpoint(Endpoint)
            .SetProject(ProjectId);

        Databases = new Databases(_client);
    }
    /// <summary>
    /// Metodo generico per inserire qualsiasi modello C# in una collezione Appwrite
    /// </summary>
    public async Task<Document?> SaveCollectionAsJsonAsync<T>(string collectionId, List<T> itemsList)
    {
        try
        {
            // 1. Serializza la collezione C# in una singola stringa JSON
            string jsonString = JsonSerializer.Serialize(itemsList);

            // 2. Prepara il dizionario indicando il campo stringa di destinazione
            var data = new Dictionary<string, object>
            {
                { "json", jsonString }
            };

            // 3. Invio del documento ad Appwrite
#pragma warning disable CS0618
            var response = await Databases.CreateDocument(
                databaseId: DatabaseId,
                collectionId: collectionId,
                documentId: ID.Unique(),
                data: data
            );
#pragma warning restore CS0618

            Console.WriteLine($"✅ JSON della collezione salvato con successo! Document ID: {response.Id}");
            return response;
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Errore Appwrite [{ex.Code}]: {ex.Message}");
            return null;
        }
    }

    public async Task ResetDbAsync()
    {
        if (Databases == null)
            throw new InvalidOperationException("Devi prima inizializzare il client Appwrite!");

        // Svuota la collezione Serate
        await ClearCollectionAsync(SerateCollectionId);

        // Svuota la collezione Workshop
        await ClearCollectionAsync(WorkshopsCollectionId);

        Console.WriteLine("✅ Database resettato con successo!");
    }

    private async Task ClearCollectionAsync(string collectionId)
    {
        try
        {
#pragma warning disable CS0618
            // 1. Recupera tutti i documenti presenti nella collezione
            var response = await Databases.ListDocuments(
                databaseId: DatabaseId,
                collectionId: collectionId
            );

            // 2. Elimina ogni documento tramite il suo ID univoco
            foreach (var doc in response.Documents)
            {
                await Databases.DeleteDocument(
                    databaseId: DatabaseId,
                    collectionId: collectionId,
                    documentId: doc.Id
                );
            }
#pragma warning restore CS0618
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Errore durante lo svuotamento di '{collectionId}': {ex.Message}");
        }
    }

    public async Task<List<Event>> GetEventsAsync(string collectionId)
    {
        try
        {
#pragma warning disable CS0618 // L'API di Appwrite che stai utilizzando è deprecata. Non esista ancora un metodo alternativo per ottenere i documenti,
            //quindi per ora puoi continuare a usare questo metodo.
            var response = await Databases.ListDocuments(
                databaseId: DatabaseId,
                collectionId: collectionId
            );
#pragma warning restore CS0618
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var eventsList = new List<Event>();

            foreach (var doc in response.Documents)
            {
                // 1. Estrai la stringa JSON salvata nell'attributo "json" (o "payload")
                if (doc.Data.TryGetValue("json", out var rawJson) && rawJson != null)
                {
                    string jsonString = rawJson.ToString()!;

                    // 2. Deserializza la stringa contenente la lista di Eventi
                    var items = JsonSerializer.Deserialize<List<Event>>(jsonString, options);
                    if (items != null)
                    {
                        eventsList.AddRange(items);
                    }
                }
            }

            return eventsList;
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"Errore di Appwrite: {ex.Message}");
            return new List<Event>();
        }
    }
}