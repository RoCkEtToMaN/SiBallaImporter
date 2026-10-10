using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public static class OpenStreetMapGeocoder
{
    // Istanza riutilizzabile con Timeout impostato per evitare blocchi prolungati
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15) // Sblocca la richiesta dopo 8 secondi di mancata risposta
    };

    static OpenStreetMapGeocoder()
    {
        // Header obbligatorio per le chiamate verso i servizi basati su OpenStreetMap
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiBallaDataImporting/1.0 (siballanewsletter@gmail.com)");
    }

    public static async Task<(double Latitude, double Longitude)> GetCoordinatesFromCityAndNameLocation(string city, string state, string location)
    {
        try
        {
            string address = $"{location}, {city}, {state}";
            string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(address)}&limit=1";

            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return (0, 0);

            string jsonResponse = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<PhotonResponse>(jsonResponse);

            var feature = data?.Features?.FirstOrDefault();
            if (feature?.Geometry?.Coordinates != null && feature.Geometry.Coordinates.Length >= 2)
            {
                // Photon restituisce le coordinate nell'ordine [Longitude, Latitude]
                double lon = feature.Geometry.Coordinates[0];
                double lat = feature.Geometry.Coordinates[1];
                return (lat, lon);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore Geocoding Photon: {ex.Message}");
        }

        return (0, 0);
    }
    /// <summary>
    /// Restituisce Città, Regione e Paese partendo da latitudine e longitudine.
    /// </summary>
    public static async Task<(string City, string Region, string Country)?> GetmCityAndNameLocationFromCoordinatesAsync(double latitude, double longitude)
    {
        try
        {
            // Rispettiamo la regola del rate-limit di Nominatim (massimo 1 richiesta al secondo)
            await Task.Delay(1000);

            // Formattiamo le coordinate con il punto come separatore decimale indipendente dalla lingua del PC
            string latStr = latitude.ToString(CultureInfo.InvariantCulture);
            string lonStr = longitude.ToString(CultureInfo.InvariantCulture);

            string url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latStr}&lon={lonStr}&zoom=10";

            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return (null, null, null);

            string jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            // Verifichiamo se la risposta contiene l'indirizzo
            if (root.TryGetProperty("address", out var addressElement))
            {
                // Spesso nei piccoli comuni la città si trova in 'town', 'village' o 'municipality' anziché 'city'
                string city = null;
                if (addressElement.TryGetProperty("city", out var cityProp)) city = cityProp.GetString();
                else if (addressElement.TryGetProperty("town", out var townProp)) city = townProp.GetString();
                else if (addressElement.TryGetProperty("village", out var villageProp)) city = villageProp.GetString();
                else if (addressElement.TryGetProperty("municipality", out var muniProp)) city = muniProp.GetString();

                // Regione (Stato federato o Provincia autonoma)
                string region = null;
                if (addressElement.TryGetProperty("state", out var stateProp)) region = stateProp.GetString();
                else if (addressElement.TryGetProperty("region", out var regProp)) region = regProp.GetString();

                // Paese
                string country = null;
                if (addressElement.TryGetProperty("country", out var countryProp)) country = countryProp.GetString();

                return (city, region, country);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REVERSE GEO ERRORE]: {ex.Message}");
        }

        return (null, null, null);
    }
    private class PhotonResponse
    {
        [JsonPropertyName("features")]
        public PhotonFeature[] Features { get; set; }
    }

    private class PhotonFeature
    {
        [JsonPropertyName("geometry")]
        public PhotonGeometry Geometry { get; set; }
    }

    private class PhotonGeometry
    {
        [JsonPropertyName("coordinates")]
        public double[] Coordinates { get; set; }
    }
}