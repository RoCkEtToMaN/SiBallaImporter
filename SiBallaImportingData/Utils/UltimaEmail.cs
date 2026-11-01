using SiBallaImportingData.Model;
using System.Text.Json;

namespace SiBallaImportingData.Utils
{
    /// <summary>
    /// Ricorda gli eventi dell'ultima email mandata (ultima-email.json nella cartella dati), per mandarla
    /// solo quando ce n'è almeno uno nuovo. Gli eventi che spariscono perché sono passati non contano:
    /// con la finestra dei prossimi Giorni che scorre, la lista cambierebbe tutti i giorni
    /// </summary>
    public static class UltimaEmail
    {
        private static string Percorso => DataPaths.Get("ultima-email.json");

        private static string Chiave(Event e) => $"{e.PartyName?.Trim().ToLowerInvariant()}|{e.DateStart:yyyy-MM-dd}";

        /// <summary>Gli eventi che l'ultima email non aveva (tutti, se non c'è ancora un'ultima email)</summary>
        public static List<Event> Nuovi(List<Event> eventi)
        {
            var precedenti = Leggi();
            return eventi.Where(e => !precedenti.Contains(Chiave(e))).ToList();
        }

        public static void Ricorda(List<Event> eventi)
        {
            try
            {
                File.WriteAllText(Percorso, JsonSerializer.Serialize(eventi.Select(Chiave).Distinct().ToList()));
            }
            catch (Exception ex)
            {
                // al peggio domani la rimanda: non deve fermare l'import
                Console.WriteLine($"⚠️ Eventi dell'ultima email non salvati: {ex.Message}");
            }
        }

        private static HashSet<string> Leggi()
        {
            try
            {
                return File.Exists(Percorso)
                    ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(Percorso)) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ {Percorso} illeggibile, la considero vuota: {ex.Message}");
                return [];
            }
        }
    }
}
