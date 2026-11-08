using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;
using System.Globalization;

namespace SiBallaImportingData.Service
{
    // Giro normale: per le righe future dello Sheet senza indirizzo chiede a Gemini il locale e lo scrive nello Sheet.
    // Vale solo se Photon trova l'indirizzo entro 10 km dalla città (controllo contro gli indirizzi inventati).
    // La riga diventa "gemini": va sul DB, ma il dizionario impara l'indirizzo solo quando scrivi "ok"
    public static class IndirizziGemini
    {
        // una ricerca senza risultato non si ripete per questi giorni (il sito dell'evento può aggiornarsi)
        private const int GiorniPrimaDiRiprovare = 30;

        public static async Task CercaAsync(List<Event> sheetEvents, KnownVenueStore store, AddressResolver resolver,
            GeminiOptions gemini, int max)
        {
            if (string.IsNullOrWhiteSpace(gemini.ApiKey) || string.IsNullOrWhiteSpace(gemini.Model))
            {
                Console.WriteLine("[indirizzi] Chiave Gemini o modello non impostati: ricerca indirizzi saltata");
                return;
            }

            // un gruppo = stesso evento (organizzatore + città + titolo): una ricerca, l'indirizzo va su tutte le sue date
            var gruppi = sheetEvents
                .Where(e => e.DateStart.Date >= DateTime.Today)
                .Where(e => string.IsNullOrWhiteSpace(e.EventDetail?.Address))
                .Where(e => e.SheetName is not null && e.SheetRow is not null)
                // senza la colonna non posso segnarla "gemini": al giro dopo sembrerebbe un indirizzo scritto da te
                .Where(e => e.SheetVerifiedColumn is not null)
                // eventi che Gemini ha già messo in dubbio: prima decidi tu se tenerli
                .Where(e => e.LuogoVerificato != ImportData.VerifiedPending)
                .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
                // senza ricerca Google Gemini può solo leggere il sito in "Riferimento"
                .Where(e => gemini.UseGoogleSearch || gemini.UseModelKnowledge || !string.IsNullOrWhiteSpace(e.EventDetail?.WebSite))
                .GroupBy(e => KnownVenueStore.MakeKey(e.Organizer!, e.City!) + "|" + KnownVenueStore.NormalizeTitle(e.PartyName))
                .Select(g => g.OrderBy(e => e.DateStart).ToList())
                .Where(g => store.GeminiNonTrovato(g[0]) is not DateTime d || d.AddDays(GiorniPrimaDiRiprovare) <= DateTime.Today)
                .OrderBy(g => g[0].DateStart)
                .ToList();

            var daFare = gruppi.Take(max).ToList();
            Console.WriteLine($"\n===== Indirizzi con Gemini: {daFare.Count} eventi (su {gruppi.Count} senza indirizzo, max {max} per giro) =====");
            if (daFare.Count == 0) return;

            var scritte = new List<(Event, string)>();
            int trovati = 0, nonTrovati = 0, errori = 0;
            foreach (var gruppo in daFare)
            {
                var ev = gruppo[0];
                Console.WriteLine($"🔎 {ev.DateStart:yyyy-MM-dd} {ev.City} | {ev.Organizer?.Trim()} | {ev.PartyName?.Trim()}");
                try
                {
                    var r = await VenueAddressFinder.FindAsync(ev, gemini);
                    (double Lat, double Lng)? coords = null;
                    string? scarto = null;
                    if (r is null || !r.Found)
                        scarto = $"Gemini: non trovato{(r?.Motivo is null ? "" : " - " + r.Motivo)}";
                    else if (string.Equals(r.Confidence, "bassa", StringComparison.OrdinalIgnoreCase))
                        scarto = $"Gemini: confidenza bassa ({r.VenueName}, {r.Address})";
                    else if ((coords = await resolver.GeocodeAsync(r.Address!, r.City ?? ev.City, ev.Country)) is null)
                        scarto = $"Photon: '{r.Address}' non trovato entro 10 km da {ev.City}";

                    if (scarto is not null)
                    {
                        nonTrovati++;
                        Console.WriteLine($"   ❌ {scarto}");
                        store.SegnaGeminiNonTrovato(ev);
                    }
                    else
                    {
                        trovati++;
                        var luogo = string.Join(", ", new[] { r!.VenueName, r.Address }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        var nota = $"Indirizzo trovato da Gemini il {DateTime.Today:dd/MM/yyyy}: {luogo} (confidenza {r.Confidence})\n" +
                                   $"Fonte: {r.SourceUrl}\n{r.Motivo}\n" +
                                   $"Controllato? Scrivi '{ImportData.VerifiedOk}' in '{ImportData.VerifiedHeader}'";
                        Console.WriteLine($"   ✅ {luogo} -> {coords!.Value.Lat.ToString("F7", CultureInfo.InvariantCulture)}, {coords.Value.Lng.ToString("F7", CultureInfo.InvariantCulture)} ({gruppo.Count} date)");

                        // in memoria subito, così in questo giro va già sul DB
                        foreach (var riga in gruppo)
                        {
                            riga.EventDetail.Address = r.Address;
                            riga.EventDetail.Location = luogo;
                            riga.EventDetail.Latitude = coords.Value.Lat;
                            riga.EventDetail.Longitude = coords.Value.Lng;
                            riga.LuogoVerificato = ImportData.VerifiedGemini;
                            scritte.Add((riga, nota));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // errore (Gemini giù anche dopo i tentativi, risposta illeggibile): non lo segno, si riprova al giro dopo
                    errori++;
                    Console.WriteLine($"   ❗ errore: {ex.Message}");
                }

                await Task.Delay(4000);   // piano gratuito: poche richieste al minuto
            }

            Console.WriteLine($"===== Indirizzi: trovati {trovati}, non trovati {nonTrovati} (si riprova tra {GiorniPrimaDiRiprovare} giorni), errori {errori} =====");

            try
            {
                await ImportData.ScriviIndirizziGeminiAsync(scritte);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sheet] ⚠️ indirizzi di Gemini non scritti sullo Sheet: {ex.Message}");
            }
        }
    }
}
