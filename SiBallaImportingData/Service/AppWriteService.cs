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
    public Storage Storage { get; private set; }

    private const string Endpoint = "https://cloud.appwrite.io/v1";
    private const string ProjectId = "6aa5651e00042e1452be";

    public const string DatabaseId = "6aa638b50000f24a7b92";
    public const string SerateCollectionId = "serate";
    public const string WorkshopsCollectionId = "workshop";
    public const string ImagesBucketId = "immagini";

    // senza chiave valgono i permessi "Any" di collezioni e bucket; con la chiave (Appwrite:ApiKey) i permessi della chiave
    public AppWriteService(string? apiKey = null)
    {
        _client = new Client()
            .SetEndpoint(Endpoint)
            .SetProject(ProjectId);
        if (!string.IsNullOrWhiteSpace(apiKey))
            _client.SetKey(apiKey);

        Databases = new Databases(_client);
        Storage = new Storage(_client);
    }

    // link pubblico dell'immagine (il bucket deve avere il permesso di lettura "Any")
    public static string ImageViewUrl(string fileId) =>
        $"{Endpoint}/storage/buckets/{ImagesBucketId}/files/{fileId}/view?project={ProjectId}";

    /// <summary>
    /// Crea il bucket delle immagini se non c'è, con lettura pubblica (l'app le apre senza login).
    /// Serve la chiave API con lo scope buckets.write. Restituisce false se il bucket non è disponibile
    /// </summary>
    public async Task<bool> EnsureImagesBucketAsync()
    {
        try
        {
            await Storage.GetBucket(ImagesBucketId);
            return true;
        }
        catch (AppwriteException ex) when (ex.Code == 404)
        {
            try
            {
                await Storage.CreateBucket(
                    bucketId: ImagesBucketId,
                    name: "Immagini eventi",
                    permissions: new List<string> { Permission.Read(Role.Any()) },
                    fileSecurity: false,
                    maximumFileSize: 10 * 1024 * 1024,
                    allowedFileExtensions: new List<string> { "jpg", "jpeg", "png", "webp", "gif" }
                );
                Console.WriteLine($"✅ Bucket '{ImagesBucketId}' creato su Appwrite Storage");
                return true;
            }
            catch (AppwriteException createEx)
            {
                Console.WriteLine($"❌ Bucket '{ImagesBucketId}' non creato [{createEx.Code}]: {createEx.Message}");
                return false;
            }
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Bucket '{ImagesBucketId}' non raggiungibile [{ex.Code}]: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Carica l'immagine nel bucket. Se un file con lo stesso id c'è già (stesso evento) va bene così
    /// </summary>
    public async Task<bool> UploadImageAsync(string fileId, byte[] bytes, string fileName, string mimeType)
    {
        // errori di rete (es. connessione chiusa dal server a metà invio): riprovo una volta, poi lascio perdere quest'immagine
        for (int tentativo = 1; ; tentativo++)
        {
            try
            {
                await Storage.CreateFile(
                    bucketId: ImagesBucketId,
                    fileId: fileId,
                    file: InputFile.FromBytes(bytes, fileName, mimeType)
                );
                return true;
            }
            catch (AppwriteException ex) when (ex.Code == 409)
            {
                return true;
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine($"❌ Errore caricamento immagine {fileId} [{ex.Code}]: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (tentativo < 2)
            {
                Console.WriteLine($"⚠️ Caricamento immagine {fileId} interrotto ({ex.Message}), riprovo");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Caricamento immagine {fileId} non riuscito ({bytes.Length / 1024} KB, {mimeType}): {ex.Message}");
                return false;
            }
        }
    }

    public async Task<bool> DeleteImageAsync(string fileId)
    {
        try
        {
            await Storage.DeleteFile(ImagesBucketId, fileId);
            return true;
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Errore cancellazione immagine {fileId} [{ex.Code}]: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Id e nome di tutti i file del bucket immagini, null se non riesco a leggerli
    /// </summary>
    public async Task<Dictionary<string, string>?> GetImageFilesAsync()
    {
        try
        {
            var ids = new Dictionary<string, string>();
            string? last = null;
            while (true)
            {
                var queries = new List<string> { Query.Limit(100) };
                if (last is not null)
                    queries.Add(Query.CursorAfter(last));

                var response = await Storage.ListFiles(ImagesBucketId, queries);
                foreach (var file in response.Files)
                    ids[file.Id] = file.Name;

                if (response.Files.Count < 100)
                    return ids;
                last = response.Files[^1].Id;
            }
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Errore lettura del bucket '{ImagesBucketId}' [{ex.Code}]: {ex.Message}");
            return null;
        }
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

    /// <summary>
    /// Elimina tutti i documenti della collezione, tranne keepId se indicato. Restituisce false se qualcosa è andato storto
    /// </summary>
    public async Task<bool> ClearCollectionAsync(string collectionId, string? keepId = null)
    {
        try
        {
#pragma warning disable CS0618
            // ListDocuments restituisce al massimo 25 documenti per volta: ripeto finché resta solo keepId (o niente)
            while (true)
            {
                // 1. Recupera i documenti presenti nella collezione
                var response = await Databases.ListDocuments(
                    databaseId: DatabaseId,
                    collectionId: collectionId
                );

                var daEliminare = response.Documents.Where(d => d.Id != keepId).ToList();
                if (daEliminare.Count == 0)
                    return true;

                // 2. Elimina ogni documento tramite il suo ID univoco
                foreach (var doc in daEliminare)
                {
                    await Databases.DeleteDocument(
                        databaseId: DatabaseId,
                        collectionId: collectionId,
                        documentId: doc.Id
                    );
                }
            }
#pragma warning restore CS0618
        }
        catch (AppwriteException ex)
        {
            Console.WriteLine($"❌ Errore durante lo svuotamento di '{collectionId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sostituisce il contenuto della collezione con un unico documento: app e bot leggono sempre una sola copia dello Sheet.
    /// Prima salva il nuovo e poi cancella i vecchi: se il salvataggio fallisce restano i dati del giro prima,
    /// invece di una collezione vuota. Per qualche secondo ci sono due copie
    /// </summary>
    public async Task<Document?> ReplaceCollectionAsJsonAsync<T>(string collectionId, List<T> itemsList)
    {
        var nuovo = await SaveCollectionAsJsonAsync(collectionId, itemsList);
        if (nuovo is null)
        {
            Console.WriteLine($"⚠️ '{collectionId}': salvataggio fallito, restano i dati precedenti");
            Environment.ExitCode = 1;
            return null;
        }

        // se la cancellazione dei vecchi fallisce il giro dopo li ripulisce (cancella tutto tranne il documento nuovo)
        if (!await ClearCollectionAsync(collectionId, keepId: nuovo.Id))
            Environment.ExitCode = 1;

        return nuovo;
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