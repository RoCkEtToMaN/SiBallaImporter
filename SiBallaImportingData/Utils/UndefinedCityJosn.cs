using SiBallaImportingData.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SiBallaImportingData.Utils
{
    public static class UndefinedCityJosn
    {
        public static string ExportEventsToJson(IEnumerable<EventDetail> events, string customTitle = "undefined_city_events")
        {
            if (!customTitle.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                customTitle += ".json";
            }

            string sanitizedTitle = string.Join("_", customTitle.Split(Path.GetInvalidFileNameChars()));
            string targetPath = Path.Combine(AppContext.BaseDirectory, sanitizedTitle);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };

            // Serializza l'intera lista/collezione in una volta sola
            string jsonContent = JsonSerializer.Serialize(events, options);
            File.WriteAllText(targetPath, jsonContent);

            Console.WriteLine($"[JSON SAVED] Salvata lista di eventi in: {targetPath}");

            return targetPath;
        }

        public static string SaveNoCityEventToBin(EventDetail eventDetail, string customTitle)
        {
            // 1. Assicura che il titolo abbia l'estensione .json
            if (!customTitle.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                customTitle += ".json";
            }

            // 2. Rimuove eventuali caratteri non validi per i nomi di file dal titolo
            string sanitizedTitle = string.Join("_", customTitle.Split(Path.GetInvalidFileNameChars()));

            // 3. Costruisce il percorso assoluto puntando alla cartella del bin dell'eseguibile
            string targetPath = Path.Combine(AppContext.BaseDirectory, sanitizedTitle);

            // 4. Configura le opzioni di serializzazione JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true, // Formattazione leggibile con a capo e rientri
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // es. PartyName -> partyName
                Converters = { new JsonStringEnumConverter() } // Salva gli enum come stringhe ("Event", "Lindy")
            };

            // 5. Serializza e scrive il file su disco
            string jsonContent = JsonSerializer.Serialize(eventDetail, options);
            File.WriteAllText(targetPath, jsonContent);

            Console.WriteLine($"[JSON SAVED] File salvato con successo in: {targetPath}");

            return targetPath;
        }
    }
}
