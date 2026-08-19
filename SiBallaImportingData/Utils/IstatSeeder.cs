using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;

public static class IstatSeeder
{
    private const string SourceUrl = "https://raw.githubusercontent.com/matteocontini/comuni-italiani/main/comuni.json";

    // Il tuo FirebaseClient
    private static readonly FirebaseClient _firebaseClient =
        new FirebaseClient("https://siballa-8fc2f-default-rtdb.europe-west1.firebasedatabase.app/");

    /// <summary>
    /// Esegui questo metodo UNA SOLA VOLTA per popolare Firebase.
    /// </summary>
    public static async Task SeedIstatCodesToFirebaseAsync()
    {
        try
        {
            Console.WriteLine("1. Scaricamento dati dei Comuni in corso...");
            using var httpClient = new HttpClient();
            string jsonContent = await httpClient.GetStringAsync(SourceUrl);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var comuniList = JsonSerializer.Deserialize<List<ComuneDto>>(jsonContent, options);

            if (comuniList == null || comuniList.Count == 0)
            {
                Console.WriteLine("Nessun dato trovato nella fonte.");
                return;
            }

            Console.WriteLine($"2. Elaborazione di {comuniList.Count} comuni...");
            var istatDictionary = new Dictionary<string, string>();

            foreach (var item in comuniList)
            {
                if (!string.IsNullOrWhiteSpace(item.Codice) && !string.IsNullOrWhiteSpace(item.Nome))
                {
                    string fullCode = item.Codice.Trim();
                    string shortCode = fullCode.TrimStart('0');

                    // Mappa sia con che senza zero iniziale (es. "016044" e "16044")
                    istatDictionary[fullCode] = item.Nome;
                    istatDictionary[shortCode] = item.Nome;
                }
            }

            Console.WriteLine("3. Inserimento dei dati su Firebase Realtime Database...");
            // Scrive l'intero dizionario nel nodo /IstatCodes
            await _firebaseClient
                .Child("IstatCodes")
                .PutAsync(istatDictionary);

            Console.WriteLine("✅ Inserimento completato con successo!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Errore durante l'inserimento: {ex.Message}");
        }
    }
}