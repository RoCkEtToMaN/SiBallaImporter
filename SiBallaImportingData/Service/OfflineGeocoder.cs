using NGeoNames;
using NGeoNames.Entities;
using SiBallaImportingData.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    public static class OfflineGeocoder
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const string DownloadUrl = "http://download.geonames.org/export/dump/cities15000.zip";
        private const string ZipFileName = "cities15000.zip";
        private const string TxtFileName = "cities15000.txt";


        static OfflineGeocoder()
        {
            // User-Agent richiesto dalle policy di OpenStreetMap per evitare HTTP 403
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiBallaDataImporting/1.0 (info@siballa.it)");
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }
        public static async Task EnsureGeoNamesFileExistsAsync()
        {
            if (File.Exists(TxtFileName)) return;

            Console.WriteLine("[GEO] File cities15000.txt non trovato. Download in corso (~2.5 MB)...");

            using (var client = new HttpClient())
            {
                byte[] zipBytes = await client.GetByteArrayAsync(DownloadUrl);
                await File.WriteAllBytesAsync(ZipFileName, zipBytes);
            }

            Console.WriteLine("[GEO] Estrazione del file in corso...");
            using (ZipArchive archive = ZipFile.OpenRead(ZipFileName))
            {
                var entry = archive.GetEntry(TxtFileName);
                entry?.ExtractToFile(TxtFileName, overwrite: true);
            }

            if (File.Exists(ZipFileName))
            {
                File.Delete(ZipFileName); // Rimuove il file .zip temporaneo
            }

            Console.WriteLine("[GEO] Database cities15000.txt pronto.");
        }
        // Lista in memoria di tutte le città del mondo
        private static List<ExtendedGeoName> _worldCities = new List<ExtendedGeoName>();

        /// <summary>
        /// Da chiamare una sola volta all'avvio dell'applicazione (es. nel Main)
        /// </summary>
        /// <param name="geoNamesFilePath">Il percorso del file cities15000.txt</param>
        public static void Initialize(string geoNamesFilePath)
        {
            Console.WriteLine("[GEO] Caricamento del database geografico globale in memoria...");

            try
            {
                // Legge il file di GeoNames e lo salva in RAM (richiede circa 1-2 secondi)
                _worldCities = GeoFileReader.ReadExtendedGeoNames(geoNamesFilePath).ToList();
                Console.WriteLine($"[GEO] Caricate {_worldCities.Count} città. Geocoding offline pronto.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GEO ERRORE] Impossibile caricare il file GeoNames: {ex.Message}");
            }
        }

            /// <summary>
            /// Restituisce Latitudine e Longitudine filtrando per città e (opzionalmente) per nazione.
            /// </summary>
            public static (double Latitude, double Longitude) GetCoordinates(string cityName, string countryInput = null)
            {
                if (string.IsNullOrWhiteSpace(cityName) || !_worldCities.Any())
                    return (0, 0);

                // Usa CountryCodeHelper.ToIso2Code già definito nel tuo progetto[cite: 1]
                string targetIso = !string.IsNullOrWhiteSpace(countryInput)
                    ? CountryCodeHelper.ToIso2Code(countryInput)
                    : null;

                // 1. Filtra per nome città o nomi alternativi
                var cityMatches = _worldCities.Where(x =>
                    x.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase) ||
                    (x.AlternateNames != null && x.AlternateNames.Contains(cityName, StringComparer.OrdinalIgnoreCase))
                );

                // 2. Se la nazione è specificata, filtra per CountryCode
                if (!string.IsNullOrEmpty(targetIso))
                {
                    var countryMatches = cityMatches
                        .Where(x => x.CountryCode.Equals(targetIso, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (countryMatches.Any())
                    {
                        var bestInCountry = countryMatches.OrderByDescending(x => x.Population).First();
                        return (bestInCountry.Latitude, bestInCountry.Longitude);
                    }
                    else
                    {
                       return Task.Run(() => FetchFromWebAndSaveAsync(cityName, targetIso)).Result;
                    }
                }

                // 3. Fallback: prende il miglior risultato globale per popolazione
                var globalMatches = cityMatches.ToList();
                if (globalMatches.Any())
                {
                    var bestGlobal = globalMatches.OrderByDescending(x => x.Population).First();
                    return (bestGlobal.Latitude, bestGlobal.Longitude);
                }

                return (0, 0);
            }

        private static async Task<(double Latitude, double Longitude)> FetchFromWebAndSaveAsync(string cityName, string countryIso)
        {
            try
            {
                // 1. Rispetta la frequenza massima di 1 richiesta al secondo per Nominatim
                await Task.Delay(1000);

                // Costruisce la query di ricerca (es. "São Pedro do Sul, PT")
                string query = string.IsNullOrWhiteSpace(countryIso)
                    ? cityName
                    : $"{cityName}, {countryIso}";

                string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";

                // 2. Chiamata API HTTP
                var response = await HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return (0, 0);

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 3. Parsing del risultato JSON
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var firstMatch = root[0];

                    if (double.TryParse(firstMatch.GetProperty("lat").GetString(), CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(firstMatch.GetProperty("lon").GetString(), CultureInfo.InvariantCulture, out double lon))
                    {
                        // 4. SALVATAGGIO IMMEDIATO NEL FILE custom_locations.json
                        CustomLocationsManager.AddAndSave(cityName, countryIso, lat, lon);

                        Console.WriteLine($"[GEO WEB SUCCESS] Trovata e salvata: {cityName} ({countryIso}) -> Lat: {lat}, Lon: {lon}");
                        return (lat, lon);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GEO WEB ERRORE] Impossibile recuperare '{cityName}': {ex.Message}");
            }

            return (0, 0);
        }
    }
}

