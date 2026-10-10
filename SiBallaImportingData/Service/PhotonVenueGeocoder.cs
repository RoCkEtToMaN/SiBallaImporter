using System.Globalization;
using System.Text.Json;

public class PhotonVenueGeocoder
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // il server pubblico di Photon chiede di identificarsi
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SiBallaImportingData/1.0");
        return c;
    }

    /// <summary>
    /// Cerca "query" e restituisce il primo risultato entro maxKm dal centro città.
    /// </summary>
    public async Task<(double Lat, double Lng, string? Name)?> FindAsync(
        string query, (double Lat, double Lng) cityCenter, double maxKm = 10, CancellationToken ct = default)
    {
        var hasCenter = cityCenter != (0.0, 0.0);

        var url = "https://photon.komoot.io/api/?limit=5&q=" + Uri.EscapeDataString(query);
        if (hasCenter)
        {
            url += "&lat=" + cityCenter.Lat.ToString(CultureInfo.InvariantCulture)
                 + "&lon=" + cityCenter.Lng.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var coords = f.GetProperty("geometry").GetProperty("coordinates");
                double lng = coords[0].GetDouble();      // Photon restituisce [longitudine, latitudine]
                double lat = coords[1].GetDouble();

                if (hasCenter && HaversineKm(lat, lng, cityCenter.Lat, cityCenter.Lng) > maxKm)
                    continue;                            // omonimo in un'altra città: scarto

                string? name = null;
                if (f.TryGetProperty("properties", out var p) && p.TryGetProperty("name", out var n))
                    name = n.GetString();

                return (lat, lng, name);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"Photon: errore per \"{query}\": {ex.Message}");
        }

        return null;
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }
}