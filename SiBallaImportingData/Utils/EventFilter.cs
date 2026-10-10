using SiBallaImportingData.Model;
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
        public static async Task<List<Event>> FilterESwingItEventsByDateRange(List<ESwingIt> events, KnownVenueStore store, AddressResolver resolver, float radiusKm)
        {
            KnownVenueStore.Load(DataPaths.Get("venues.json"));
            var genova = events.Where(x => x.City?.ToLower() == "genova").OrderBy(x => x.Start).ToList();//170
            var eventList = new List<Event>();
            //DateTime.Today <= x.Start && x.Start <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])) &&

            // Elenco delle parole da escludere nel titolo (tutte rigorosamente in minuscolo)
            string[] excludedTitleWords = {
                "prova", "prove", "stage", "corsi", "corso", "workshop", "blues dance - tutti i mercoledi"
            };

            var filteredEvents = events.Where(x =>
            {
                // Normalizzazione sicura (gestisce eventuali null senza crashare)
                var title = x.Title?.ToLower() ?? string.Empty;
                var desc = x.Description?.ToLower() ?? string.Empty;
                var url = x.Url?.ToLower() ?? string.Empty;
                var dateStart = x.Start;

                // 1. Controlla se il titolo contiene una delle parole escluse
                if (excludedTitleWords.Any(word => title.Contains(word)))
                    return false;

                // 2. Controlla la descrizione
                if (desc.Contains("kirsten"))
                    return false;

                // 3. Controlla l'URL
                if (url.Contains("cors"))
                    return false;

                if (DateTime.SpecifyKind(dateStart.Date, DateTimeKind.Local) < DateTime.Today)
                    return false;

                // Se passa tutti i controlli, l'evento è valido
                return true;
            }).ToList();            

            foreach (var e in filteredEvents)
            {            
                if (CheckEventInsideCircle(e, radiusKm))
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
                .ToList();

            foreach (var e in filteredEvents2)
            {
                if (CheckEventInsideCircle(e, radiusKm))
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

                    var newEvent = new Event
                    {
                        DateStart = e.DateStart,
                        DateFinish = e.DateFinish,
                        Duration = e.Duration,
                        City = e.City,
                        Country = e.Country,
                        Organizer = e.EventDetail.Description,
                        PartyName = e.PartyName,
                        NameAndData = e.DateFinish != null ? $"{e.PartyName} - {e.DateStart.ToShortDateString()} to {end.ToShortDateString()}" :
                            $"{e.PartyName} - {e.DateStart.ToShortDateString()}",

                        EventDetail = new EventDetail()
                        {
                            WebSite = e.EventDetail.WebSite?.Split(';')[0],
                            City = e.City,
                            Country = e.Country,
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

                    Console.WriteLine($"{e.City}  entro {radiusKm} km: {"✅ SÌ"}");
                }
                else
                    Console.WriteLine($" {e.City}  Entro {radiusKm} km: {"❌ NO"}");
            }

            return eventList;
        }
        private static bool CheckEventInsideCircle(Object e, float raggio_km)
        {

            
            if (e is Event)
            {
                var eEvent = e as Event;

                if (eEvent.EventDetail.Latitude == null || eEvent.EventDetail.Longitude == null)
                    return false;
        
                return (HaversineDistance((float)eEvent.EventDetail.Latitude, (float)eEvent.EventDetail.Longitude) <= raggio_km* 1000f);
            }
            else
            {
                var eEvent = e as ESwingIt;

                if (eEvent?.Latitude == null || eEvent?.Longitude == null)
                    return false;

                return (HaversineDistance((float)eEvent.Latitude, (float)eEvent.Longitude) <= raggio_km * 1000f);
            }
            
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
