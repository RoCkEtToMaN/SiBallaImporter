using SiBallaImportingData.Model;
using System.Configuration;
using System.Text;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    // Controllo finale con Gemini: separa gli eventi swing da quelli che probabilmente non lo sono.
    // Se l'API non è configurata o va in errore, tutti gli eventi restano nella lista swing (l'email parte comunque).
    public static class SwingEventClassifier
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(90) };

        private const string Istruzioni =
            "Sei un esperto di balli swing. Ti passo una lista di eventi trovati online. " +
            "Per ognuno decidi se è un evento di ballo swing o affine: Lindy Hop, Balboa, Boogie Woogie, Collegiate Shag, " +
            "St. Louis Shag, Charleston, Solo Jazz, Blues dance, Tap, serate o festival swing con musica dal vivo o DJ. " +
            "NON sono swing: liscio, balli di gruppo, rock'n'roll acrobatico sportivo, West Coast Swing se è l'unico stile, " +
            "fiere o mercatini vintage senza una parte di ballo, concerti rockabilly senza riferimenti al ballo swing, " +
            "eventi non legati al ballo. Se l'evento è misto ma include una parte swing, consideralo swing." +
            "Se sono eventi lezioni di prova senza una parte di ballo, considerali non swing." +
            "Per ogni evento rispondi con id, isSwing e un motivo breve in italiano (max 15 parole).";

        public static async Task<(List<Event> Swing, List<(Event Evento, string Motivo)> Altri)> ClassifyAsync(List<Event> eventi)
        {
            var swing = new List<Event>(eventi);
            var altri = new List<(Event, string)>();

            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            var model = ConfigurationManager.AppSettings["GeminiModel"];
            if (eventi.Count == 0 || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            {
                if (eventi.Count != 0)
                    Console.WriteLine("[AI] GEMINI_API_KEY o GeminiModel non impostati: controllo saltato.");
                return (swing, altri);
            }

            try
            {
                var elenco = new StringBuilder();
                for (int i = 0; i < eventi.Count; i++)
                {
                    var ev = eventi[i];
                    var descrizione = ev.EventDetail?.Description ?? string.Empty;
                    if (descrizione.Length > 600)
                        descrizione = descrizione[..600];

                    elenco.AppendLine($"id: {i}");
                    elenco.AppendLine($"titolo: {ev.PartyName}");
                    elenco.AppendLine($"stili: {ev.EventDetail?.StylesToString}");
                    elenco.AppendLine($"organizzatore: {ev.Organizer}");
                    elenco.AppendLine($"sito: {ev.EventDetail?.WebSite}");
                    elenco.AppendLine($"descrizione: {descrizione.Replace('\n', ' ')}");
                    elenco.AppendLine("---");
                }

                var body = new
                {
                    systemInstruction = new { parts = new[] { new { text = Istruzioni } } },
                    contents = new[] { new { role = "user", parts = new[] { new { text = elenco.ToString() } } } },
                    generationConfig = new
                    {
                        responseMimeType = "application/json",
                        responseSchema = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    id = new { type = "INTEGER" },
                                    isSwing = new { type = "BOOLEAN" },
                                    motivo = new { type = "STRING" }
                                },
                                required = new[] { "id", "isSwing", "motivo" }
                            }
                        }
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[AI] Errore Gemini {(int)response.StatusCode}: {responseBody}");
                    return (swing, altri);
                }

                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement.GetProperty("candidates")[0]
                    .GetProperty("content").GetProperty("parts")[0]
                    .GetProperty("text").GetString();

                using var risultati = JsonDocument.Parse(text ?? "[]");
                var nonSwing = new Dictionary<int, string>();
                foreach (var r in risultati.RootElement.EnumerateArray())
                {
                    if (!r.GetProperty("isSwing").GetBoolean())
                        nonSwing[r.GetProperty("id").GetInt32()] = r.GetProperty("motivo").GetString() ?? string.Empty;
                }

                swing.Clear();
                for (int i = 0; i < eventi.Count; i++)
                {
                    if (nonSwing.TryGetValue(i, out var motivo))
                    {
                        altri.Add((eventi[i], motivo));
                        Console.WriteLine($"[AI] Non swing: {eventi[i].PartyName} -> {motivo}");
                    }
                    else
                        swing.Add(eventi[i]);
                }

                Console.WriteLine($"[AI] Eventi swing: {swing.Count}, da verificare: {altri.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Controllo non riuscito, invio la lista completa: {ex.Message}");
                swing = new List<Event>(eventi);
                altri.Clear();
            }

            return (swing, altri);
        }
    }
}
