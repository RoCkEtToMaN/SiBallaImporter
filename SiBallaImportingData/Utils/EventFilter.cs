using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System.Diagnostics.Eventing.Reader;

namespace SiBallaImportingData.Utils
{
    public static class EventFilter
    {
        public static bool IsInRadius(float lat, float lon, float radiusKm)
        {
            float distanzaMetri = HaversineDistance(lat, lon);
            float radiusMetri = radiusKm * 1000f;

            return distanzaMetri <= radiusMetri;
        }
        // Per le email la distanza deve essere verificabile: luogo sconosciuto escluso.
        public static bool IsWithinEmailRadius(Event ev, float radiusKm)
        {
            var lat = ev.EventDetail?.Latitude;
            var lng = ev.EventDetail?.Longitude;
            if (!HasCoords(lat, lng))
            {
                var city = string.IsNullOrWhiteSpace(ev.City) ? ev.EventDetail?.City : ev.City;
                var country = string.IsNullOrWhiteSpace(ev.Country) ? ev.EventDetail?.Country : ev.Country;
                if (!HasCity(city)) return false;
                (lat, lng) = OfflineGeocoder.GetCoordinates(city!, country);
            }
            return HasCoords(lat, lng) && IsInRadius((float)lat!.Value, (float)lng!.Value, radiusKm);
        }

        // Parole che indicano una lezione o un corso, non un evento (tutte in minuscolo, confronto per sottostringa)
        private static readonly string[] LessonTitleWords = {
            "lezion", "prova", "prove", "corsi", "corso", "classe", "classi", "livello", "livelli",
            "open level", "principianti", "intermedi", "avanzati", "beginner", "intermediate", "advanced"
        };

        /// <summary>
        /// True se dal titolo o dal sito sembra una lezione (di prova, a livelli, corso) e non un evento.
        /// Vale per tutte le fonti (e-swing e SwingPlanit).
        /// </summary>
        public static bool IsLesson(string? title, string? url)
        {
            var t = title?.ToLower() ?? string.Empty;
            var u = url?.ToLower() ?? string.Empty;
            return LessonTitleWords.Any(word => t.Contains(word)) || u.Contains("cors") || u.Contains("lezion");
        }

        public static async Task<List<Event>> FilterESwingItEventsByDateRange(List<ESwingIt> events, KnownVenueStore store, AddressResolver resolver, float radiusKm)
        {
            KnownVenueStore.Load(DataPaths.Get("venues.json"));
            var genova = events.Where(x => x.City?.ToLower() == "genova").OrderBy(x => x.Start).ToList();//170
            var eventList = new List<Event>();
            //DateTime.Today <= x.Start && x.Start <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])) &&

            // Parole escluse solo per e-swing (in minuscolo): stage e workshop arrivano già da SwingPlanit
            string[] excludedTitleWords = {
                "stage", "workshop", "blues dance - tutti i mercoledi"
            };

            var filteredEvents = events.Where(x =>
            {
                // Normalizzazione sicura (gestisce eventuali null senza crashare)
                var title = x.Title?.ToLower() ?? string.Empty;
                var desc = x.Description?.ToLower() ?? string.Empty;
                var dateStart = x.Start;

                // 1. Lezioni e corsi (titolo o URL)
                if (IsLesson(x.Title, x.Url))
                {
                    Console.WriteLine($"🎓 Lezione scartata: {x.Title}");
                    return false;
                }

                // 2. Controlla se il titolo contiene una delle parole escluse
                if (excludedTitleWords.Any(word => title.Contains(word)))
                    return false;

                // 3. Controlla la descrizione
                if (desc.Contains("kirsten"))
                    return false;

                if (DateTime.SpecifyKind(dateStart.Date, DateTimeKind.Local) < DateTime.Today)
                    return false;

                // Se passa tutti i controlli, l'evento è valido
                return true;
            }).ToList();            

            foreach (var e in filteredEvents)
            {            
                if (IsInsideCircle(e.Latitude, e.Longitude, e.City, e.Country, radiusKm))
                {
                    Console.WriteLine("Data evento e città : " + e.Start + ", " + e.City);
                    var end = new DateTime();
                    if (e.End != null)
                        end = (DateTime)e.End;

                    var descr = e.Title + ".\n" + e.Description;

                    var located = await resolver.ApplyAsync(e);
                    if (located)
                        Console.WriteLine($"📍 Coordinate aggiornate: {e.Title} ({e.City}) -> {e.Latitude}, {e.Longitude}");

                    (e.ExternalImage, e.LocalImage) = await ImportDataFromWeb.GetESwingImageAsync(e.EventLink);
                    if (!string.IsNullOrEmpty(e.FullImageUrl))
                        Console.WriteLine($"🖼️ Immagine trovata: {e.Title} -> {e.FullImageUrl}");

                    var newEvent = new Event
                    {
                        DateStart = e.Start,
                        DateFinish = e.End,
                        Duration = e.Duration,
                        City = e.City,
                        Country = e.Country,
                        Organizer = e.Organizer,                        
                        PartyName = e.Title,
                        NameAndData = e.End != null ? $"{e.Title} - {e.Start.ToShortDateString()} to {end.ToShortDateString()}" :
                            $"{e.Title} - {e.Start.ToShortDateString()}",
                        EventDetail = new EventDetail()
                        {
                            //WebSite = e.Url,
                            //WebSite = e.Url.Contains(";") ? e.Url.Split(';')[0] : e.Url,
                            WebSite = e.Url?.Split(';')[0],
                            City = e.City,
                            Country = e.Country,
                            Longitude = e.Longitude,
                            Latitude = e.Latitude,
                            //Styles = e.Styles,
                            StylesToString = e.StylesString,
                            Description = descr,
                            ImageUrl = string.IsNullOrEmpty(e.FullImageUrl) ? null : e.FullImageUrl
                        }
                    };
                    eventList.Add(newEvent);

                    if (store.TryApply(newEvent))
                        Console.WriteLine($"📍 Luogo noto per: {newEvent.PartyName} ({newEvent.City})");

                    MarkLocation(newEvent);
                    Console.WriteLine($"{e.City}  entro {radiusKm} km: {"✅ SÌ"}");
                }
                else
                    Console.WriteLine($" {e.City}  Entro {radiusKm} km: {"❌ NO"}");
            }

            return eventList;
        }
        public static async Task<List<Event>> FilterSwingPlanitEventsByDateRangeAsync(
            List<Event> events, KnownVenueStore store, AddressResolver resolver, float radiusKm)
        {
            var eventList = new List<Event>();

            DateTime oggi = DateTime.Today;

            var filteredEvents2 = events
                .Where(x => DateTime.SpecifyKind(x.DateStart, DateTimeKind.Local) >= oggi)
                .Where(x =>
                {
                    if (!IsLesson(x.PartyName, x.EventDetail?.WebSite)) return true;
                    Console.WriteLine($"🎓 Lezione scartata: {x.PartyName}");
                    return false;
                })
                .ToList();

            foreach (var e in filteredEvents2)
            {
                if (IsInsideCircle(e.EventDetail.Latitude, e.EventDetail.Longitude,
                        string.IsNullOrWhiteSpace(e.City) ? e.EventDetail.City : e.City,
                        string.IsNullOrWhiteSpace(e.Country) ? e.EventDetail.Country : e.Country, radiusKm))
                {
                    Console.WriteLine("Data evento e città : " + e.DateStart + ", " + e.City);
                    var end = new DateTime();
                    if (e.DateFinish != null)
                        end = (DateTime)e.DateFinish;

                    var descr = e.PartyName + ".\n" + e.EventDetail.Description;

                    // SwingPlanit non ha immagini: le cerco sul sito esterno dell'evento (campo Website) tramite l'OpenGraph di e-swing
                    string? imageUrl = null;
                    var webSite = e.EventDetail.WebSite?.Split(';')[0];
                    if (!string.IsNullOrWhiteSpace(webSite) && !webSite.Contains("swingplanit.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var (image, localImage) = await ImportDataFromWeb.GetESwingImageAsync(webSite);
                        imageUrl = !string.IsNullOrWhiteSpace(localImage) ? $"https://e-swing.it{localImage}" : image;
                        if (!string.IsNullOrEmpty(imageUrl))
                            Console.WriteLine($"🖼️ Immagine trovata: {e.PartyName} -> {imageUrl}");
                    }

                    // il parser di SwingPlanit salva città e nazione solo in EventDetail ("Town:" / "Country:")
                    var city = string.IsNullOrWhiteSpace(e.City) ? e.EventDetail.City : e.City;
                    var country = string.IsNullOrWhiteSpace(e.Country) ? e.EventDetail.Country : e.Country;

                    var newEvent = new Event
                    {
                        DateStart = e.DateStart,
                        DateFinish = e.DateFinish,
                        Duration = e.DateFinish != null ? end - e.DateStart : e.Duration,
                        City = city,
                        Country = country,
                        Organizer = e.Organizer, // SwingPlanit non indica l'organizzatore; la descrizione resta in EventDetail.Description
                        PartyName = e.PartyName,
                        NameAndData = e.DateFinish != null ? $"{e.PartyName} - {e.DateStart.ToShortDateString()} to {end.ToShortDateString()}" :
                            $"{e.PartyName} - {e.DateStart.ToShortDateString()}",

                        EventDetail = new EventDetail()
                        {
                            WebSite = e.EventDetail.WebSite?.Split(';')[0],
                            City = city,
                            Country = country,
                            Location = e.EventDetail.Location,      // NUOVO
                            Longitude = e.EventDetail.Longitude,
                            Latitude = e.EventDetail.Latitude,
                            Styles = e.EventDetail.Styles,
                            StylesToString = e.EventDetail.StylesToString,
                            Description = descr,
                            ImageUrl = imageUrl
                        }
                    };
                    eventList.Add(newEvent);

                    // NUOVO: geocodifica dall'indirizzo (JSON, cache o Photon)
                    if (await resolver.ApplyAsync(newEvent))
                        Console.WriteLine($"📍 Coordinate aggiornate: {newEvent.PartyName} ({newEvent.City}) -> {newEvent.EventDetail.Latitude}, {newEvent.EventDetail.Longitude}");

                    // nome sala e indirizzo, se esiste un luogo verificato nel JSON
                    if (store.TryApply(newEvent))
                        Console.WriteLine($"📍 Luogo noto per: {newEvent.PartyName} ({newEvent.City})");

                    MarkLocation(newEvent);
                    Console.WriteLine($"{e.City}  entro {radiusKm} km: {"✅ SÌ"}");
                }
                else
                    Console.WriteLine($" {e.City}  Entro {radiusKm} km: {"❌ NO"}");
            }

            return eventList;
        }

        public static bool HasCity(string? city) =>
            !string.IsNullOrWhiteSpace(city) && !city.Equals("undefined", StringComparison.OrdinalIgnoreCase);

        private static bool HasCoords(double? lat, double? lng) =>
            lat is double la && lng is double lo && !(la == 0 && lo == 0);

        /// <summary>
        /// Coordinate dell'evento, altrimenti centro della città. Se non si sa dove sia l'evento
        /// lo tengo (true): finisce nell'email come "luogo da verificare" invece di sparire.
        /// </summary>
        private static bool IsInsideCircle(double? lat, double? lng, string? city, string? country, float raggio_km)
        {
            // Import:Radius = 0 (o negativo): nessun limite di distanza per la ricerca (l'email ha il suo RadiusEmail)
            if (raggio_km <= 0) return true;
            if (!HasCoords(lat, lng))
            {
                if (!HasCity(city)) return true;
                (lat, lng) = OfflineGeocoder.GetCoordinates(city!, country);
                if (!HasCoords(lat, lng)) return true;
            }

            return HaversineDistance((float)lat!.Value, (float)lng!.Value) <= raggio_km * 1000f;
        }

        /// <summary>Senza coordinate precise metto il centro città (per la mappa va sistemato a mano), senza città segno il luogo come sconosciuto.</summary>
        private static void MarkLocation(Event ev)
        {
            var d = ev.EventDetail;
            if (HasCoords(d.Latitude, d.Longitude)) return;

            if (HasCity(ev.City))
            {
                var (lat, lng) = OfflineGeocoder.GetCoordinates(ev.City!, ev.Country);
                if (HasCoords(lat, lng))
                {
                    (d.Latitude, d.Longitude) = (lat, lng);
                    ev.ProblemaLuogo = "coordinate del centro città";
                    return;
                }
            }

            ev.LuogoSconosciuto = true;
            ev.ProblemaLuogo = HasCity(ev.City) ? $"città '{ev.City}' non trovata" : "città sconosciuta";
            Console.WriteLine($"❓ Luogo da verificare: {ev.PartyName} -> {ev.ProblemaLuogo}");
        }
        private static float HaversineDistance(float lat1, float lon1)
        {
            //Calcola la distanza in metri tra due punti geografici.

            const float R = 6371000f; // Raggio terrestre in metri
            float lat2 = 44.4264f;
            float lon2 = 8.91519f;

            //# Converti gradi in radianti
            var phi1 = lat1 * (MathF.PI / 180.0f);  
            var phi2 = lat2 * (MathF.PI / 180.0f);
            var delta_phi = (lat2 - lat1) * (MathF.PI / 180.0f);
            var delta_lambda = (lon2 - lon1) * (MathF.PI / 180.0f);

            //# Formula di Haversine
            //var a = (Math.Sin(delta_phi / 2) * Math.Sin(delta_phi / 2)) +
            //        Math.Cos(phi1) * Math.Cos(phi2) * (Math.Sin(delta_lambda / 2) * Math.Sin(delta_lambda / 2));

            // CORRETTO: Usa MathF per tutto
            float a = MathF.Sin(delta_phi / 2) * MathF.Sin(delta_phi / 2) +
                      MathF.Cos(phi1) * MathF.Cos(phi2) *
                      MathF.Sin(delta_lambda / 2) * MathF.Sin(delta_lambda / 2);

            var c =(float) (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));

            return R * c;
        }
        public static void Test()
        {
            var cittas = new (string Nome, float Lat, float Lon)[]
            {
                ("Genova (centro)", 44.4264f, 8.91519f),
                ("Milano", 45.4642f, 9.1900f),
                ("Torino", 45.0703f, 7.6869f),
                ("Firenze", 43.7696f, 11.2558f),
                ("Roma", 41.9028f, 12.4964f),
                ("Savona", 44.3090f, 8.4770f) // ~40km da Genova
            };

            float raggioKm = 200f;
            Console.WriteLine($"Cerchio di {raggioKm} km attorno a Genova:");
            Console.WriteLine("=========================================");

            foreach (var citta in cittas)
            {
                float distanzaKm = HaversineDistance(citta.Lat, citta.Lon) / 1000f;
                bool dentro = IsInRadius(citta.Lat, citta.Lon, raggioKm);

                Console.WriteLine($"{citta.Nome}:");
                Console.WriteLine($"  Distanza: {distanzaKm:F1} km");
                Console.WriteLine($"  Entro {raggioKm} km: {(dentro ? "✅ SÌ" : "❌ NO")}");
                Console.WriteLine();
            }
        }
    }
}
