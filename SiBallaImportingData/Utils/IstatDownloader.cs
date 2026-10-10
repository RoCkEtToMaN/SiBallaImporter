using Firebase.Database;
using Firebase.Database.Query;
using SiBallaImportingData.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class IstatDownloader
{
    private const string CacheFilePath = "zip_cache.json";
    private static readonly HttpClient HttpClient = new HttpClient();

    // Cache thread-safe in memoria: la chiave è nel formato "COUNTRY:ZIP" (es. "IT:16044" o "TH:10200")
    private static ConcurrentDictionary<string, ZippoResponse> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private const string IstatCsvUrl = "https://www.istat.it/storage/codici-unita-amministrative/Elenco-comuni-italiani.csv";

    private static readonly FirebaseClient _firebaseClient =
        new FirebaseClient("https://siballa-8fc2f-default-rtdb.europe-west1.firebasedatabase.app/");

    public static Dictionary<string, string>? IstatDic { get; set; }

    private const string ZipDownloadUrl = "https://download.geonames.org/export/zip/allCountries.zip";
    private const string ZipFileName = "allCountries.zip";
    private const string TxtFileName = "allCountries.txt";
    // Mappa in memoria: Chiave "COUNTRY:ZIP" -> Nome Città (es. "IT:16044" -> "Genova", "TH:10200" -> "Bangkok")
    public static readonly ConcurrentDictionary<string, string> ZipToCityMap = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Inizializza il database dei CAP: scarica ed estrae il file se assente in locale, poi lo carica in memoria.
    /// </summary>
    public static async Task InitializeAsync()
    {
        // 1. Se il file TXT decompresso non esiste, procede al download e all'estrazione
        if (!File.Exists(TxtFileName))
        {
            await DownloadAndExtractGeoNamesZipAsync();
        }

        // 2. Carica i dati in memoria
        await LoadTxtToMemoryAsync();
    }

    /// <summary>
    /// Scarica il file ZIP da GeoNames e ne estrae il file allCountries.txt
    /// </summary>
    private static async Task DownloadAndExtractGeoNamesZipAsync()
    {
        Console.WriteLine("[GEONAMES] File locale assente. Download del database mondiale dei CAP in corso (~25 MB)...");

        using (var httpClient = new HttpClient())
        {
            byte[] zipBytes = await httpClient.GetByteArrayAsync(ZipDownloadUrl);
            await File.WriteAllBytesAsync(ZipFileName, zipBytes);
        }

        Console.WriteLine("[GEONAMES] Estrazione file in corso...");

        using (ZipArchive archive = ZipFile.OpenRead(ZipFileName))
        {
            ZipArchiveEntry entry = archive.GetEntry(TxtFileName);
            if (entry != null)
            {
                entry.ExtractToFile(TxtFileName, overwrite: true);
            }
        }

        // Pulizia del file .zip scaricato per risparmiare spazio
        if (File.Exists(ZipFileName))
        {
            File.Delete(ZipFileName);
        }

        Console.WriteLine("[GEONAMES] Estrazione completata con successo!");
    }

    /// <summary>
    /// Legge il file TSV e popola il dizionario locale.
    /// </summary>
    private static async Task LoadTxtToMemoryAsync()
    {
        Console.WriteLine("[GEONAMES] Caricamento del file CAP in memoria...");
        int loadedCount = 0;

        using (var reader = new StreamReader(TxtFileName, Encoding.UTF8))
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // I campi di GeoNames sono separati da Tab (\t)
                string[] cols = line.Split('\t');

                if (cols.Length >= 3)
                {
                    string countryCode = cols[0].Trim().ToUpper(); // es. IT, TH, US
                    string zipCode = cols[1].Trim();               // es. 16044, 10200
                    string cityName = cols[2].Trim();              // es. Genova, Bangkok

                    if (!string.IsNullOrEmpty(countryCode) && !string.IsNullOrEmpty(zipCode) && !string.IsNullOrEmpty(cityName))
                    {
                        string key = $"{countryCode}:{zipCode}";
                        ZipToCityMap[key] = cityName;
                        loadedCount++;
                    }
                }
            }
        }

        Console.WriteLine($"[GEONAMES] Caricamento completato! {loadedCount} CAP pronti in memoria.");
    }

    /// <summary>
    /// Cerca il nome della città partendo da Codice Paese (ISO 2 lettere) e CAP.
    /// </summary>
    public static string GetCityName(string countryCode, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(zipCode))
            return null;

        string key = $"{countryCode.Trim().ToUpper()}:{zipCode.Trim()}";

        return ZipToCityMap.TryGetValue(key, out var city) ? city : null;
    }

    /// <summary>
    /// Scarica il CSV ISTAT ufficiale, effettua il parsing e salva la mappa su Firebase.
    /// </summary>
    public static async Task<bool> SyncIstatFromOfficialCsvAsync()
    {
        try
        {
            Console.WriteLine("1. Download del CSV ufficiale ISTAT...");

            // Abilita i provider per la codifica ISO-8859-1 usata dai CSV ISTAT
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var latin1Encoding = Encoding.GetEncoding("iso-8859-1");

            using var httpClient = new HttpClient();
            byte[] csvBytes = await httpClient.GetByteArrayAsync(IstatCsvUrl);
            string csvText = latin1Encoding.GetString(csvBytes);

            var istatDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var reader = new StringReader(csvText);
            string line;
            bool isHeader = true;

            Console.WriteLine("2. Parsing dei dati in corso...");
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Il delimitatore dei file ISTAT è il punto e virgola ';'
                string[] columns = line.Split(';');

                if (isHeader)
                {
                    isHeader = false; // Salta la riga dei titoli
                    continue;
                }

                // Nel tracciato ISTAT:
                // Column 4 (o 5): Codice Comune formato alfanumerico (es. "016044")
                // Column 6: Denominazione in italiano (es. "Genova")
                if (columns.Length > 20)
                {
                    string code = columns[4].Trim().Trim('"');
                    string cityName = columns[6].Trim().Trim('"');
                    string provincia = columns[11].Trim().Trim('"');

                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(cityName))
                    {
                        string fullCode = code.PadLeft(6, '0');
                        string shortCode = fullCode.TrimStart('0');

                        // Registra sia la versione completa con zero iniziale che quella senza
                        istatDictionary[fullCode] = cityName;
                        istatDictionary[shortCode] = cityName;
                    }
                }
            }

            Console.WriteLine($"3. Invio di {istatDictionary.Count} codici ISTAT a Firebase...");

            IstatDic = istatDictionary;

            // Salva l'intera struttura sul database
            //await _firebaseClient
            //    .Child("IstatCodes")
            //    .PutAsync(istatDictionary);

            Console.WriteLine("✅ Sincronizzazione ISTAT completata con successo!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Errore durante la sincronizzazione ISTAT: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Recupera l'intera mappa dei codici ISTAT dal database Firebase.
    /// </summary>
    public static async Task<Dictionary<string, string>> GetIstatCodesFromDBAsync()
    {
        try
        {
            Console.WriteLine("Lettura della tabella IstatCodes da Firebase...");

            // Legge l'intero nodo /IstatCodes come Dizionario
            var istatData = await _firebaseClient
                .Child("IstatCodes")
                .OnceSingleAsync<Dictionary<string, string>>();

            if (istatData != null && istatData.Count > 0)
            {
                Console.WriteLine($"[INFO] Recuperati con successo {istatData.Count} codici ISTAT.");
                IstatDic = istatData;
                return new Dictionary<string, string>(istatData, StringComparer.OrdinalIgnoreCase);
            }

            Console.WriteLine("[AVVISO] La tabella IstatCodes su Firebase è vuota.");
            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRORE] Impossibile recuperare la tabella ISTAT da Firebase: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
    //private static readonly HttpClient HttpClient = new HttpClient();

    /// <summary>
    /// Carica il file JSON di cache all'avvio dell'applicazione.
    /// </summary>
    public static async Task LoadCacheAsync()
    {
        if (!File.Exists(CacheFilePath)) return;

        try
        {
            string json = await File.ReadAllTextAsync(CacheFilePath);
            var loadedData = JsonSerializer.Deserialize<ConcurrentDictionary<string, ZippoResponse>>(json);

            if (loadedData != null)
            {
                _memoryCache = new ConcurrentDictionary<string, ZippoResponse>(loadedData, StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"[CACHE] Caricati {_memoryCache.Count} CAP salvati da file locale.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVVISO] Impossibile leggere il file di cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Cerca il CAP in memoria. Se non c'è, chiama l'API, salva il risultato in memoria e aggiorna il JSON.
    /// </summary>
    public static async Task<ZippoResponse> GetOrFetchZipInfoAsync(string countryCode, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(zipCode))
            return null;

        string cacheKey = $"{countryCode.Trim().ToUpper()}:{zipCode.Trim()}";

        // 1. Se presente in memoria, restituisce direttamente i dati salvati
        if (_memoryCache.TryGetValue(cacheKey, out var cachedResponse))
        {
            return cachedResponse;
        }

        // 2. Se manca, interroga l'API online
        try
        {
            string url = $"http://api.zippopotam.us/{countryCode.Trim().ToLower()}/{zipCode.Trim()}";
            var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            var zippoData = JsonSerializer.Deserialize<ZippoResponse>(json);

            if (zippoData != null && zippoData.Places != null && zippoData.Places.Any())
            {
                // Salva in memoria
                _memoryCache[cacheKey] = zippoData;

                // Persiste la cache aggiornata su file JSON
                await SaveCacheToDiskAsync();

                return zippoData;
            }
        }
        catch
        {
            // Errore di rete o API non raggiungibile
        }

        return null;
    }

    /// <summary>
    /// Restituisce solo il nome della città (estratto dal modello completo).
    /// </summary>
    public static async Task<string> GetCityNameAsync(string countryCode, string zipCode)
    {
        var data = await GetOrFetchZipInfoAsync(countryCode, zipCode);
        return data?.Places?.FirstOrDefault()?.PlaceName;
    }

    private static async Task SaveCacheToDiskAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_memoryCache, options);
            await File.WriteAllTextAsync(CacheFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRORE] Impossibile salvare il file di cache su disco: {ex.Message}");
        }
    }

    public static async Task<string> GetCityFromZipAsync(string countryCode, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(zipCode) || string.IsNullOrWhiteSpace(countryCode))
            return null;

        try
        {
            string url = $"http://api.zippopotam.us/{countryCode.Trim().ToLower()}/{zipCode.Trim()}";
            var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<ZippoResponse>(json);

            return data?.Places?.FirstOrDefault()?.PlaceName;
        }
        catch
        {
            return null; // Fallback in caso di errore di rete
        }
    }

    /// <summary>
    /// Recupera il nome della città per un singolo codice ISTAT specifico.
    /// </summary>
    public static async Task<string> GetCityByIstatCodeAsync(string istatCode)
    {
        if (string.IsNullOrWhiteSpace(istatCode)) return null;

        try
        {
            string cleanCode = istatCode.Trim();

            // Interroga direttamente il sotto-nodo del singolo codice (es. /IstatCodes/16044)
            string cityName = await _firebaseClient
                .Child("IstatCodes")
                .Child(cleanCode)
                .OnceSingleAsync<string>();

            return cityName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRORE] Errore durante la ricerca del codice {istatCode}: {ex.Message}");
            return null;
        }
    }
}