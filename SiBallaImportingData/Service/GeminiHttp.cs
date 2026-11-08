using System.Text;

namespace SiBallaImportingData.Service
{
    // Chiamata a Gemini generateContent con nuovi tentativi sugli errori temporanei (429 troppe richieste, 5xx come il 503 "high demand")
    public static class GeminiHttp
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(90) };

        // attese prima di ogni nuovo tentativo; se Gemini indica quanto aspettare (Retry-After) vale quello
        private static readonly TimeSpan[] AttesePerRiprovare = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

        /// <summary>
        /// Manda il corpo JSON al modello e restituisce la risposta. Errori non temporanei, o temporanei anche
        /// dopo l'ultimo tentativo: HttpRequestException con codice e testo della risposta
        /// </summary>
        public static async Task<string> GenerateContentAsync(string model, string apiKey, string jsonBody, CancellationToken ct = default)
        {
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
                bool temporaneo = status == 429 || status >= 500;
                if (!temporaneo || tentativo > AttesePerRiprovare.Length)
                    throw new HttpRequestException($"Gemini {status}: {responseBody}");

                var attesa = response.Headers.RetryAfter?.Delta ?? AttesePerRiprovare[tentativo - 1];
                Console.WriteLine($"[AI] Gemini {status}, nuovo tentativo tra {attesa.TotalSeconds:F0} s ({tentativo}/{AttesePerRiprovare.Length})");
                await Task.Delay(attesa, ct);
            }
        }
    }
}
