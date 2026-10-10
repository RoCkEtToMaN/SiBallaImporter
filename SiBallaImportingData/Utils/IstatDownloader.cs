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
    private const string IstatCsvUrl = "https://www.istat.it/storage/codici-unita-amministrative/Elenco-comuni-italiani.csv";

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
    /// Scarica il CSV ISTAT ufficiale, effettua il parsing e salva la mappa in IstatDic.
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

            Console.WriteLine($"3. Caricati {istatDictionary.Count} codici ISTAT.");

            IstatDic = istatDictionary;

            Console.WriteLine("✅ Sincronizzazione ISTAT completata con successo!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Errore durante la sincronizzazione ISTAT: {ex.Message}");
            return false;
        }
    }
}
