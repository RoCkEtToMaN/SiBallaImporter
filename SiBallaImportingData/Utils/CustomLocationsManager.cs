using SiBallaImportingData.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    public static class CustomLocationsManager
    {
        private static readonly string FilePath = "custom_locations.json";
        private static List<CustomLocation> _customCache = new();
        // Oggetto lock per garantire l'accesso esclusivo al file da parte di un solo thread alla volta
        private static readonly object FileLock = new object();

        /// <summary>
        /// Carica la lista di posizioni dal file custom_locations.json se esiste.
        /// </summary>
        public static void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    _customCache = JsonSerializer.Deserialize<List<CustomLocation>>(json) ?? new List<CustomLocation>();
                    Console.WriteLine($"[CACHE] Caricate {_customCache.Count} posizioni personalizzate da {FilePath}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CACHE ERRORE] Impossibile leggere {FilePath}: {ex.Message}");
                    _customCache = new List<CustomLocation>();
                }
            }
        }

        /// <summary>
        /// Cerca nella cache locale le coordinate per una determinata città e codice Paese ISO.
        /// </summary>
        public static (double Lat, double Lon)? TryGet(string city, string countryCode)
        {
            if (string.IsNullOrWhiteSpace(city)) return null;

            var match = _customCache.FirstOrDefault(x =>
                x.City.Equals(city.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(countryCode) || x.CountryCode.Equals(countryCode.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                return (match.Latitude, match.Longitude);
            }

            return null;
        }

        /// <summary>
        /// Aggiunge una nuova coordinata in memoria e aggiorna il file JSON su disco.
        /// </summary>
        public static void AddAndSave(string city, string countryCode, double lat, double lon)
        {
            if (string.IsNullOrWhiteSpace(city) || (lat == 0 && lon == 0)) return;

            string cleanCity = city.Trim();
            string cleanCountry = countryCode?.Trim() ?? string.Empty;

            lock (FileLock)
            {
                // Verifica duplicati all'interno della sezione protetta
                bool exists = _customCache.Any(x =>
                    x.City.Equals(cleanCity, StringComparison.OrdinalIgnoreCase) &&
                    x.CountryCode.Equals(cleanCountry, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    _customCache.Add(new CustomLocation
                    {
                        City = cleanCity,
                        CountryCode = cleanCountry,
                        Latitude = lat,
                        Longitude = lon
                    });

                    try
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        string json = JsonSerializer.Serialize(_customCache, options);
                        File.WriteAllText(FilePath, json);
                        Console.WriteLine($"[CACHE SAVED] Aggiunta {cleanCity} ({cleanCountry}) a {FilePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CACHE ERRORE] Impossibile salvare su file: {ex.Message}");
                    }
                }
            }
        }
    }
}