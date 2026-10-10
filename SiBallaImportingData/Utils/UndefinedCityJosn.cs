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
    }
}
