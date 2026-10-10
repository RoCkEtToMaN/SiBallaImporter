using Google.Cloud.Location;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiBallaImportingData.Service
{
    public class LocationInfo
    {
        public async Task<(double Latitiude, double Longitude)> GetCoordinatesFromCityAndNameLocation(string? city, string? state, string? location)
        {
            //string apiKey = "***RIMOSSA***"; // Sostituisci con la tua API Key

            //string url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(location + "," + city + ", " + state)}&key={apiKey}";
            ////string url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={latitude},{longitude}&key={apiKey}";

            try
            {
                var address = $"{location}, {city}, {state}";
                string apiKey = "***RIMOSSA***"; // Sostituisci con la tua API Key
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={apiKey}";

                var response = new HttpResponseMessage();

                using (var httpClient = new HttpClient())
                {

                    // Produttore (Apple, Samsung, ecc.)
                    //var manufacturer = DeviceInfo.Current.Manufacturer;

#if WINDOWS && DEBUG
                    //var handler = new WinHttpHandler();
                    //handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                    //handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                    //var test = new HttpClient(handler);
                    //response = await test.GetAsync(url);
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                    var httpClient2 = new HttpClient(handler);

                    response = await httpClient2.GetAsync(url);
#else

                    response = await httpClient.GetAsync(url);
#endif

                    Console.WriteLine($"Status: {response.StatusCode}");
                    Console.WriteLine("Headers:");
                    foreach (var header in response.Headers)
                    {
                        Console.WriteLine($"{header.Key}: {string.Join(",", header.Value)}");
                    }

                    string rawContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"\nRaw Content (first 500 chars):\n{rawContent.Substring(0, Math.Min(500, rawContent.Length))}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(content);

                        if (result?.Results?.Count > 0)
                        {
                            var location2 = result.Results[0].Geometry.Location;

                            return (location2.Lat, location2.Lng);
                        }
                    }

                    return (0, 0);
                }
            }
            catch (HttpRequestException ex)
            {
                // Eccezioni specifiche di rete/HTTP (DNS, connessione, timeout, ecc.)
                Console.WriteLine($"Errore nella richiesta: {ex.Message}");
                return (0, 0);
            }
            catch (FormatException ex)
            {
                // Gestisci l'errore
                Console.WriteLine($"Errore: {ex.Message}");
                return (0, 0); // Ritorna valori di default in caso di errore
            }
            catch (Exception ex)
            {
                // Gestisci l'errore
                Console.WriteLine($"Errore: {ex.Message}");

                System.Diagnostics.Debugger.Break(); // Forza interruzione qui
                //throw;
                return (0, 0); // Ritorna valori di default in caso di errore
            }

            //return null;
        }

        
        public class GoogleGeocodeResponse
        {
            [JsonPropertyName("results")]
            public List<GeocodeResult> Results { get; set; }

        }
        public class GeocodeResult
        {
            //public List<AddressComponent> AddressComponents { get; set; }
            [JsonPropertyName("geometry")]
            public Geometry Geometry { get; set; }
        }
        public class Geometry
        {
            [JsonPropertyName("location")]
            public LocationData Location { get; set; }
        }
        public class LocationData
        {
            [JsonPropertyName("lat")]
            public double Lat { get; set; }
            [JsonPropertyName("lng")]
            public double Lng { get; set; }
        }
    }
}
