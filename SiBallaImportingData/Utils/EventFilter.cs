using SiBallaImportingData.Model;
using System.Configuration;
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
        public static List<Event> FilterESwingItEventsByDateRange(List<ESwingIt> events)
        {
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

                // 1. Controlla se il titolo contiene una delle parole escluse
                if (excludedTitleWords.Any(word => title.Contains(word)))
                    return false;

                // 2. Controlla la descrizione
                if (desc.Contains("kirsten"))
                    return false;

                // 3. Controlla l'URL
                if (url.Contains("cors"))
                    return false;

                // Se passa tutti i controlli, l'evento è valido
                return true;
            }).ToList();

            

            foreach (var e in filteredEvents)
            {            
                if (CheckEventInsideCircle(e))
                {
                    Console.WriteLine("Data evento e città : " + e.Start + ", " + e.City);
                    var end = new DateTime();
                    if (e.End != null)
                        end = (DateTime)e.End;                  

                    var newEvent = new Event
                    {
                        DateStart = e.Start,
                        DateFinish = e.End,
                        Duration = e.Duration,
                        City = e.City,
                        Country = e.Country,
                        Organizer = e.Description,
                        PartyName = e.Title,
                        NameAndData = e.End != null ? $"{e.Title} - {e.Start.ToShortDateString()} to {end.ToShortDateString()}" :
                            $"{e.Title} - {e.Start.ToShortDateString()}",
                        EventDetail = new EventDetail()
                        {
                            //WebSite = e.Url,
                            WebSite = e.Url.Contains(";") ? e.Url.Split(';')[0] : e.Url,
                            City = e.City,
                            Country = e.Country,
                            Longitude = e.Longitude.ToString(),
                            Latitude = e.Latitude.ToString(),
                            //Styles = e.Styles,
                            StylesToString = e.StylesString
                        }
                    };
                    eventList.Add(newEvent);

                    Console.WriteLine($"{e.City}  entro {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} km: {"✅ SÌ"}");
                }
                else
                    Console.WriteLine($" {e.City}  Entro {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} km: {"❌ NO"}");
            }

            return eventList;
        }
        public static List<Event> FilterSwingPlanitEventsByDateRange(List<Event> events)
        {
            var eventList = new List<Event>();

            // 1.Estraggono e valutano i valori prima della query LINQ(fuori dalla lambda)
            int giorni = Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]);
            DateTime oggi = DateTime.Today;
            DateTime dataLimite = oggi.AddDays(giorni);

            // 2. Esegui il filtraggio in modo pulito e sicuro in memoria locale
            var filteredEvents = events.Where(x => oggi <= x.DateStart && x.DateStart <= dataLimite).ToList();
            //.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])))
            foreach (var e in events)
            {            
                if (CheckEventInsideCircle(e))
                {
                    Console.WriteLine("Data evento e città : " + e.DateStart + ", " + e.City);
                    var end = new DateTime();
                    if (e.DateFinish != null)
                        end = (DateTime)e.DateFinish;                  

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
                            //WebSite = e.Url,
                            WebSite = e.EventDetail.WebSite.Contains(";") ? e.EventDetail.WebSite.Split(';')[0] : e.EventDetail.WebSite,
                            City = e.City,
                            Country = e.Country,
                            Longitude = e.EventDetail.Longitude,
                            Latitude = e.EventDetail.Latitude,
                            Styles = e.EventDetail.Styles,
                            StylesToString = e.EventDetail.StylesToString
                        }
                    };
                    eventList.Add(newEvent);

                    Console.WriteLine($"{e.City}  entro {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} km: {"✅ SÌ"}");
                }
                else
                    Console.WriteLine($" {e.City}  Entro {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} km: {"❌ NO"}");
            }

            return eventList;
        }
        private static bool CheckEventInsideCircle(Object e)
        {
            var raggio_km = (float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"]); // Raggio in km

            
            if (e is Event)
            {
                var eEvent = e as Event;

                if (eEvent.EventDetail.Latitude == null || eEvent.EventDetail.Longitude == null)
                    return false;
        
                return (HaversineDistance(float.Parse(eEvent.EventDetail.Latitude), float.Parse(eEvent.EventDetail.Longitude)) <= raggio_km* 1000f);
            }
            else
            {
                var eEvent = e as ESwingIt;

                if (eEvent?.Latitude == null || eEvent?.Longitude == null)
                    return false;

                return (HaversineDistance(eEvent.Latitude, eEvent.Longitude) <= raggio_km * 1000f);
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
