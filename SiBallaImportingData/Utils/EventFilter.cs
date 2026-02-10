using SiBallaImportingData.Model;
using System.Configuration;

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
            var eventList = new List<Event>();

            foreach (var e in events.Where(x => DateTime.Today <= x.Start && x.Start <= DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])) && 
                (!x.Title.ToLower().Contains("prova") && !x.Title.ToLower().Contains("prove") && !x.Title.ToLower().Contains("stage") && !x.Title.ToLower().Contains("corsi") && !x.Title.ToLower().Contains("corso") 
                && !x.Title.ToLower().Contains("workshop")) && !x.Url.ToLower().Contains("cors")))
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
                            WebSite = e.Url.Contains(";") ? e.Url.Split(';')[0] : e.Url,
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
        private static bool CheckEventInsideCircle(ESwingIt e)
        {
            var raggio_km = (float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"]); // Raggio in km

            return (HaversineDistance(e.Latitude, e.Longitude) <= raggio_km * 1000f);
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
