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

        // siti che Gemini non riesce a leggere senza login (url_context restituisce la pagina di accesso o niente)
        private static readonly string[] SitiIlleggibili =
            ["facebook.com", "fb.com", "fb.me", "fb.watch", "instagram.com", "whatsapp.com", "wa.me", "t.me", "telegram.me", "telegram.org"];

        private static bool SitoLeggibile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var u = url.Trim();
            if (!u.Contains("://")) u = "https://" + u;
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return true;   // link strano: lascio decidere a Gemini
            var host = uri.Host.ToLowerInvariant();
            return !SitiIlleggibili.Any(s => host == s || host.EndsWith("." + s));
        }

        // 4xx che non dipendono dal momento (non 429) e non dalla chiave: la richiesta per quell'evento non passerà mai
        private static bool RichiestaRifiutata(HttpRequestException ex) =>
            ex.StatusCode is System.Net.HttpStatusCode code
            && (int)code is >= 400 and < 500 && (int)code != 429
            && !ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
            && (int)code != 401 && (int)code != 403;

        private record Esito(string? Scarto, string? Indirizzo, double Lat, double Lng, string? Come);

        // dal risultato di Gemini alle coordinate: indirizzo confermato da Photon, altrimenti il nome del locale cercato con Photon
        private static async Task<Esito> ConfermaAsync(VenueAddressFinder.Result? r, Event ev, AddressResolver resolver)
        {
            if (r is null)
                return new($"Gemini: nessuna risposta utile", null, 0, 0, null);
            var bassa = string.Equals(r.Confidence, "bassa", StringComparison.OrdinalIgnoreCase);
            var city = r.City ?? ev.City;

            // 1. indirizzo di Gemini confermato da Photon entro 10 km dalla città
            if (r.Found && !bassa)
            {
                var coords = await resolver.GeocodeAsync(r.Address!, city, ev.Country);
                if (coords is not null)
                    return new(null, r.Address, coords.Value.Lat, coords.Value.Lng, "indirizzo di Gemini, confermato da Photon");
            }

            // 2. Gemini sa il nome del locale ma non la via (o Photon non trova la via): cerco il locale per nome
            if (!bassa && !string.IsNullOrWhiteSpace(r.VenueName))
            {
                var locale = await resolver.CercaLocaleAsync(r.VenueName, city, ev.Country);
                if (locale is not null)
                    return new(null, locale.Value.Via ?? r.VenueName, locale.Value.Lat, locale.Value.Lng,
                        $"locale '{locale.Value.Nome}' trovato da Photon col nome indicato da Gemini" + (locale.Value.Via is null ? " (via non trovata)" : ""));
            }

            if (bassa && (r.Found || !string.IsNullOrWhiteSpace(r.VenueName)))
                return new($"Gemini: confidenza bassa ({string.Join(", ", new[] { r.VenueName, r.Address }.Where(s => !string.IsNullOrWhiteSpace(s)))})", null, 0, 0, null);
            if (r.Found)
                return new($"Photon: né '{r.Address}' né '{r.VenueName}' trovati entro 10 km da {ev.City}", null, 0, 0, null);
            return new($"Gemini: non trovato{(string.IsNullOrWhiteSpace(r.VenueName) ? "" : $" (locale '{r.VenueName}' non trovato da Photon)")}" +
                       $"{(r.Motivo is null ? "" : " - " + r.Motivo)}", null, 0, 0, null);
        }

        public static async Task CercaAsync(List<Event> sheetEvents, KnownVenueStore store, AddressResolver resolver,
            GeminiOptions gemini, int max)
        {
            if (string.IsNullOrWhiteSpace(gemini.ApiKey) || string.IsNullOrWhiteSpace(gemini.Model))
            {
                Console.WriteLine("[indirizzi] Chiave Gemini o modello non impostati: ricerca indirizzi saltata");
                return;
            }

            // un gruppo = stesso evento (organizzatore + città + titolo): una ricerca, l'indirizzo va su tutte le sue date
            bool soloSito = !gemini.UseGoogleSearch && !gemini.UseModelKnowledge;
            var gruppi = sheetEvents
                .Where(e => e.DateStart.Date >= DateTime.Today)
                .Where(e => string.IsNullOrWhiteSpace(e.EventDetail?.Address))
                .Where(e => e.SheetName is not null && e.SheetRow is not null)
                // senza la colonna non posso segnarla "gemini": al giro dopo sembrerebbe un indirizzo scritto da te
                .Where(e => e.SheetVerifiedColumn is not null)
                // eventi che Gemini ha già messo in dubbio: prima decidi tu se tenerli
                .Where(e => e.LuogoVerificato != ImportData.VerifiedPending && !ImportData.Scartato(e))
                .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
                .Where(e => !soloSito || !string.IsNullOrWhiteSpace(e.EventDetail?.WebSite))
                .GroupBy(e => KnownVenueStore.MakeKey(e.Organizer!, e.City!) + "|" + KnownVenueStore.NormalizeTitle(e.PartyName))
                .Select(g => g.OrderBy(e => e.DateStart).ToList())
                .Where(g => store.GeminiNonTrovato(g[0]) is not DateTime d || d.AddDays(GiorniPrimaDiRiprovare) <= DateTime.Today)
                .OrderBy(g => g[0].DateStart)
                .ToList();

            // senza ricerca Google Gemini può solo leggere il sito in "Riferimento", e Facebook & co. senza login non si leggono:
            // li salto invece di sprecare una ricerca (non li segno, così se cambi il link o attivi la ricerca Google ripartono)
            int saltatiSito = 0;
            if (soloSito)
            {
                saltatiSito = gruppi.Count(g => !SitoLeggibile(g[0].EventDetail?.WebSite));
                gruppi = gruppi.Where(g => SitoLeggibile(g[0].EventDetail?.WebSite)).ToList();
            }

            var daFare = gruppi.Take(max).ToList();
            Console.WriteLine($"\n===== Indirizzi con Gemini: {daFare.Count} eventi (su {gruppi.Count} senza indirizzo, max {max} per giro; " +
                              $"{saltatiSito} saltati perché il link è Facebook, Instagram, WhatsApp o Telegram) =====");
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
                    var esito = await ConfermaAsync(r, ev, resolver);

                    if (esito.Scarto is not null)
                    {
                        nonTrovati++;
                        Console.WriteLine($"   ❌ {esito.Scarto}");
                        store.SegnaGeminiNonTrovato(ev);
                    }
                    else
                    {
                        trovati++;
                        var luogo = string.Join(", ", new[] { r!.VenueName, esito.Indirizzo }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                        var nota = $"Indirizzo trovato il {DateTime.Today:dd/MM/yyyy}: {luogo}\n" +
                                   $"{esito.Come}\n" +
                                   $"Gemini (confidenza {r.Confidence ?? "-"}): {r.Motivo}\n" +
                                   $"Fonte: {r.SourceUrl}\n" +
                                   $"Controllato? Scrivi '{ImportData.VerifiedOk}' in '{ImportData.VerifiedHeader}'";
                        Console.WriteLine($"   ✅ {luogo} -> {esito.Lat.ToString("F7", CultureInfo.InvariantCulture)}, {esito.Lng.ToString("F7", CultureInfo.InvariantCulture)} ({esito.Come}, {gruppo.Count} date)");

                        // in memoria subito, così in questo giro va già sul DB
                        foreach (var riga in gruppo)
                        {
                            riga.EventDetail.Address = esito.Indirizzo;
                            riga.EventDetail.Location = luogo;
                            riga.EventDetail.Latitude = esito.Lat;
                            riga.EventDetail.Longitude = esito.Lng;
                            riga.LuogoVerificato = ImportData.VerifiedGemini;
                            scritte.Add((riga, nota));
                        }
                    }
                }
                catch (HttpRequestException ex) when (RichiestaRifiutata(ex))
                {
                    // Gemini rifiuta proprio questa richiesta (es. 400 INVALID_ARGUMENT per un link che non riesce ad aprire):
                    // riprovarla ogni notte occuperebbe un posto per niente, la tratto come "non trovato"
                    nonTrovati++;
                    Console.WriteLine($"   ❌ Gemini rifiuta la richiesta ({(int)ex.StatusCode!}): trattato come non trovato");
                    store.SegnaGeminiNonTrovato(ev);
                }
                catch (Exception ex)
                {
                    // errore temporaneo (Gemini giù anche dopo i tentativi, risposta illeggibile): non lo segno, si riprova al giro dopo
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
