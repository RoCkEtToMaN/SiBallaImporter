
using SiBallaImportingData.Model;
using System.Configuration;

namespace SiBallaImportingData.Utils
{
    public static class EventFilter
    {
        public static List<Event> FilterESwingItEventsByDateRange(List<ESwingIt> events)
        {
            var eventList = new List<Event>();

            foreach (var e in events)
            {
                // Example date range: from January 1, 2020 to December 31, 2025
                DateTime startDate = DateTime.Today;
                DateTime endDate = startDate.AddDays(14);
                
                if (CheckEventInsideCircle(e, startDate, endDate))
                {
                    var end = new DateTime();
                    if (e.End != null)
                        end = (DateTime)e.End;

                    var newEvent = new Event
                    {
                        DateStart = e.Start,
                        DateFinish = e.End,
                        City = e.City,
                        Country = e.Country,
                        Organizer = e.Title,
                        NameAndData = e.End != null ? $"{e.Title} - {e.Start.ToShortDateString()} to {end.ToShortDateString()}" :
                            $"{e.Title} - {e.Start.ToShortDateString()}",
                    };
                    eventList.Add(newEvent);
                }
            }

            return eventList;
        }

        private static bool CheckEventInsideCircle(ESwingIt e, DateTime startDate, DateTime endDate)
        {
            var raggio_km = (float) Convert.ToDouble(ConfigurationManager.AppSettings["Radius"]); // Raggio in km
            //HaversineDistance(e.Latitude, e.Longitude) <= raggio_km * 1000;  // Converti km in metri
            return (HaversineDistance(e.Latitude, e.Longitude) <= raggio_km * 1000) && (startDate <= e.Start && e.End <= endDate);
        }

        private static float HaversineDistance(float lat1, float lon1)
        {
            //Calcola la distanza in metri tra due punti geografici.

            float rTerrestre = 6371000;  /*# Raggio terrestre in metri*/
            float lat2 = 0;
            float lon2 = 0;

            //# Converti gradi in radianti
            var phi1 = lat1 * (MathF.PI / 180.0f);  
            var phi2 = lat2 * (MathF.PI / 180.0f);
            var delta_phi = (lat2 - lat1) * (MathF.PI / 180.0f);
            var delta_lambda = (lon2 - lon1) * (MathF.PI / 180.0f);

            //# Formula di Haversine
            var a = (Math.Sin(delta_phi / 2) * Math.Sin(delta_phi / 2)) +
                    Math.Cos(phi1) * Math.Cos(phi2) * (Math.Sin(delta_lambda / 2) * Math.Sin(delta_lambda / 2));
            var c =(float) (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));


            return rTerrestre * c;

            //return true;
        }
    }
}
