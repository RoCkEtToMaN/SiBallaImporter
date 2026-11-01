using SiBallaImportingData.Model;
using System.Text.Json;

namespace SiBallaImportingData.Utils
{
    /// <summary>
    /// File eventi-bot.json per il bot Telegram (AgenteRust): serate e workshop dello Sheet
    /// serializzati come per Appwrite, più gli eventi trovati online che non sono ancora nello Sheet
    /// </summary>
    public static class EventiPerBot
    {
        public static void Salva(string percorso, Dictionary<TipoEvento, List<Event>> eventiSheet, List<Event> trovati)
        {
            try
            {
                var contenuto = new
                {
                    Serate = eventiSheet.TryGetValue(TipoEvento.Serate, out var serate) ? serate : [],
                    Workshop = eventiSheet.TryGetValue(TipoEvento.Workshop, out var workshop) ? workshop : [],
                    Trovati = trovati.OrderBy(x => x.DateStart).ToList(),
                };
                File.WriteAllText(percorso, JsonSerializer.Serialize(contenuto));
                Console.WriteLine($"🤖 Eventi per il bot salvati in {percorso}: {contenuto.Serate.Count} serate, " +
                                  $"{contenuto.Workshop.Count} workshop, {contenuto.Trovati.Count} trovati online");
            }
            catch (Exception ex)
            {
                // il file serve solo al bot: un errore qui non deve fermare l'import
                Console.WriteLine($"⚠️ Eventi per il bot non salvati: {ex.Message}");
            }
        }
    }
}
