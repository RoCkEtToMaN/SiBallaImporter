using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public static class RobustGeocoder
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5) // Evita attese infinite in caso di server down
    };

    static RobustGeocoder()
    {
        // User-Agent obbligatorio per le API basate su OpenStreetMap
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiBallaDataImporting/1.0 (admin@tuodominio.it)");
    }

    public static async Task<(double Latitude, double Longitude)> GetCoordinatesAsync(string city, string state, string location)
    {
        string address = $"{location}, {city}, {state}";

        // 1. Primo tentativo: Photon (Komoot)
        var coords = await TryPhotonAsync(address);
        if (coords != (0, 0)) return coords;

        // 2. Secondo tentativo (Fallback): Nominatim (OSM)
        await Task.Delay(1000); // Rispettiamo il rate limit per Nominatim
        coords = await TryNominatimAsync(address);
        if (coords != (0, 0)) return coords;

        return (0, 0); // Nessun risultato o tutti i server irraggiungibili
    }

    private static async Task<(double Latitude, double Longitude)> TryPhotonAsync(string address)
    {
        try
        {
            string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(address)}&limit=1";
            var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return (0, 0);

            string json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<PhotonResponse>(json);
            var coords = data?.Features?.FirstOrDefault()?.Geometry?.Coordinates;

            if (coords != null && coords.Length >= 2)
            {
                return (coords[1], coords[0]); // Photon restituisce [Lon, Lat]
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVVISO] Photon non raggiungibile: {ex.Message}");
        }

        return (0, 0);
    }

    private static async Task<(double Latitude, double Longitude)> TryNominatimAsync(string address)
    {
        try
        {
            string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(address)}&format=json&limit=1";
            var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return (0, 0);

            string json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<List<NominatimResult>>(json);
            var result = results?.FirstOrDefault();

            if (result != null &&
                double.TryParse(result.Lat, CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(result.Lon, CultureInfo.InvariantCulture, out double lon))
            {
                return (lat, lon);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVVISO] Nominatim non raggiungibile: {ex.Message}");
        }

        return (0, 0);
    }

    // Classi di deserializzazione
    private class PhotonResponse
    {
        [JsonPropertyName("features")] public PhotonFeature[]? Features { get; set; }
    }
    private class PhotonFeature
    {
        [JsonPropertyName("geometry")] public PhotonGeometry? Geometry { get; set; }
    }
    private class PhotonGeometry
    {
        [JsonPropertyName("coordinates")] public double[]? Coordinates { get; set; }
    }
    private class NominatimResult
    {
        [JsonPropertyName("lat")] public string Lat { get; set; } = string.Empty;
        [JsonPropertyName("lon")] public string Lon { get; set; } = string.Empty;
    }
}