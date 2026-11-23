using System.Text;

namespace SiBallaImportingData.Service
{
    // Chiamata a Gemini generateContent con nuovi tentativi sugli errori temporanei (429 troppe richieste, 5xx come il 503 "high demand").
    // Quota GIORNALIERA finita (429 con "PerDay": piano gratuito, es. 500 richieste al giorno per modello): niente nuovi tentativi,
    // e per il resto del programma quel modello non viene più chiamato (riprova al giro dopo, la quota si azzera ogni giorno)
    public static class GeminiHttp
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(90) };

        // attese prima di ogni nuovo tentativo; se Gemini indica quanto aspettare (Retry-After) vale quello
        private static readonly TimeSpan[] AttesePerRiprovare = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

        // modelli con la quota giornaliera finita in questa esecuzione
        private static readonly HashSet<string> _quotaFinita = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True se in questa esecuzione Gemini ha già detto che la quota giornaliera del modello è finita</summary>
        public static bool QuotaFinita(string? model) => model is not null && _quotaFinita.Contains(model);

        /// <summary>
        /// Manda il corpo JSON al modello e restituisce la risposta. Errori non temporanei, o temporanei anche
        /// dopo l'ultimo tentativo, o quota giornaliera finita: HttpRequestException con codice e messaggio di Gemini
        /// </summary>
        public static async Task<string> GenerateContentAsync(string model, string apiKey, string jsonBody, CancellationToken ct = default)
        {
            if (QuotaFinita(model))
                throw new HttpRequestException($"Gemini 429: quota giornaliera di {model} già finita in questo giro", null, System.Net.HttpStatusCode.TooManyRequests);

            for (int tentativo = 1; ; tentativo++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var response = await _client.SendAsync(request, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return responseBody;

                int status = (int)response.StatusCode;
                // quota del giorno finita: riprovare tra 10 secondi non serve (Gemini dice "riprova tra qualche ora")
                if (status == 429 && responseBody.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                {
                    if (_quotaFinita.Add(model))
                        Console.WriteLine($"[AI] ⏸️ quota giornaliera gratuita di {model} finita: niente più chiamate a questo modello in questo giro");
                    throw new HttpRequestException($"Gemini 429: quota giornaliera di {model} finita", null, response.StatusCode);
                }

                // modello inesistente o ritirato ("no longer available"): come la quota finita, inutile richiamarlo in questo giro
                if (status == 404)
                {
                    if (_quotaFinita.Add(model))
                        Console.WriteLine($"[AI] ⏸️ modello {model} non disponibile: {Messaggio(responseBody)}");
                    throw new HttpRequestException($"Gemini 404: modello {model} non disponibile", null, response.StatusCode);
                }

                bool temporaneo = status == 429 || status >= 500;
                if (!temporaneo || tentativo > AttesePerRiprovare.Length)
                    throw new HttpRequestException($"Gemini {status}: {Messaggio(responseBody)}", null, response.StatusCode);

                var attesa = response.Headers.RetryAfter?.Delta ?? AttesePerRiprovare[tentativo - 1];
                Console.WriteLine($"[AI] Gemini {status}, nuovo tentativo tra {attesa.TotalSeconds:F0} s ({tentativo}/{AttesePerRiprovare.Length})");
                await Task.Delay(attesa, ct);
            }
        }

        // il campo "message" dell'errore di Gemini (la risposta intera è un JSON di decine di righe), al massimo 300 caratteri
        private static string Messaggio(string body)
        {
            var m = System.Text.RegularExpressions.Regex.Match(body, "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            var testo = m.Success ? m.Groups[1].Value.Replace("\\n", " ") : body;
            return testo.Length <= 300 ? testo : testo[..300] + "…";
        }
    }
}
