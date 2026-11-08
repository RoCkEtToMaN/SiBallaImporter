using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;
using System.Text;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    // Cerca con Gemini il locale di un evento senza indirizzo, leggendo la pagina del suo sito (url_context).
    // La ricerca Google (google_search) richiede il piano a pagamento: si attiva con Gemini:UseGoogleSearch.
    // Restituisce solo l'indirizzo: le coordinate le calcola Photon, che fa anche da controllo contro gli indirizzi inventati.
    public static class VenueAddressFinder
    {
        private const string Istruzioni =
            "Trovi il luogo esatto di eventi di ballo swing. Per l'evento indicato leggi la pagina indicata in 'sito' " +
            "(e, se puoi cercare sul web, le pagine dell'organizzatore o di siti di eventi) e trova il locale dove si svolge in quella data. " +
            "Rispondi SOLO con un oggetto JSON, senza altro testo: " +
            "{\"found\": bool, \"venueName\": string, \"address\": string, \"city\": string, " +
            "\"confidence\": \"alta\"|\"media\"|\"bassa\", \"sourceUrl\": string, \"motivo\": string}. " +
            "address = via e numero civico, senza città né CAP. motivo = breve spiegazione in italiano (max 20 parole). " +
            "Se nessuna fonte pubblica un indirizzo preciso metti found=false: non inventare indirizzi " +
            "e non usare l'indirizzo dell'organizzatore se non è detto che l'evento si tenga lì.";

        public record Result(bool Found, string? VenueName, string? Address, string? City, string? Confidence, string? SourceUrl, string? Motivo);

        public static async Task<Result?> FindAsync(Event ev, GeminiOptions gemini, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gemini.ApiKey) || string.IsNullOrWhiteSpace(gemini.Model))
            {
                Console.WriteLine("[AI] Chiave Gemini (Gemini:ApiKey o GEMINI_API_KEY) o modello non impostati: ricerca indirizzo saltata.");
                return null;
            }

            var descrizione = ev.EventDetail?.Description ?? string.Empty;
            if (descrizione.Length > 800)
                descrizione = descrizione[..800];

            var evento = new StringBuilder();
            evento.AppendLine($"titolo: {ev.PartyName?.Trim()}");
            evento.AppendLine($"data: {ev.DateStart:yyyy-MM-dd}");
            evento.AppendLine($"organizzatore: {ev.Organizer?.Trim()}");
            evento.AppendLine($"città: {ev.City}");
            evento.AppendLine($"paese: {ev.Country}");
            evento.AppendLine($"sito: {ev.EventDetail?.WebSite}");
            evento.AppendLine($"descrizione: {descrizione.Replace('\n', ' ')}");

            var istruzioni = Istruzioni;
            var tools = new List<object>();

            if (gemini.UseModelKnowledge)
            {
                // solo "parere": nessuno strumento, il modello risponde con ciò che sa (rischio di indirizzi inventati)
                istruzioni += " Non puoi consultare il web: rispondi solo se conosci con certezza il locale abituale " +
                              "di questo organizzatore o evento, altrimenti found=false. sourceUrl può essere vuoto.";
            }
            else
            {
                // senza sito e senza ricerca Google il modello potrebbe solo tirare a indovinare
                var hasSite = !string.IsNullOrWhiteSpace(ev.EventDetail?.WebSite);
                if (!hasSite && !gemini.UseGoogleSearch)
                    return new Result(false, null, null, null, null, null, "nessun sito da leggere");

                if (hasSite) tools.Add(new { url_context = new { } });
                if (gemini.UseGoogleSearch) tools.Add(new { google_search = new { } });
            }

            // con gli strumenti non si può imporre responseSchema: chiedo il JSON nel testo e lo estraggo
            var body = new Dictionary<string, object>
            {
                ["systemInstruction"] = new { parts = new[] { new { text = istruzioni } } },
                ["contents"] = new[] { new { role = "user", parts = new[] { new { text = evento.ToString() } } } }
            };
            if (tools.Count > 0)
                body["tools"] = tools;

            // 429 e 5xx (es. 503 "high demand") vengono riprovati dentro GeminiHttp
            var responseBody = await GeminiHttp.GenerateContentAsync(gemini.Model!, gemini.ApiKey!, JsonSerializer.Serialize(body), ct);

            using var doc = JsonDocument.Parse(responseBody);
            var candidate = doc.RootElement.GetProperty("candidates")[0];
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                return null;

            // il testo può arrivare in più parti e dentro un blocco ```json
            var text = string.Concat(parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out _))
                .Select(p => p.GetProperty("text").GetString()));
            int start = text.IndexOf('{'), end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            using var json = JsonDocument.Parse(text[start..(end + 1)]);
            var r = json.RootElement;
            string? Get(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var found = r.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True;
            var source = Get("sourceUrl");

            // se il modello non indica la fonte uso la prima della ricerca Google
            if (string.IsNullOrWhiteSpace(source)
                && candidate.TryGetProperty("groundingMetadata", out var gm)
                && gm.TryGetProperty("groundingChunks", out var chunks)
                && chunks.GetArrayLength() > 0
                && chunks[0].TryGetProperty("web", out var web))
                source = web.TryGetProperty("title", out var t) ? t.GetString() : null;

            return new Result(found && !string.IsNullOrWhiteSpace(Get("address")),
                Get("venueName"), Get("address"), Get("city"), Get("confidence"), source, Get("motivo"));
        }
    }
}
