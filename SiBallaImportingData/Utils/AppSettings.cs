using Microsoft.Extensions.Configuration;

namespace SiBallaImportingData.Utils
{
    // Configurazione a strati (l'ultimo vince):
    //   appsettings.json -> user-secrets (solo PC locale) -> variabili d'ambiente -> riga di comando
    // Variabili d'ambiente: sezione e chiave separate da "__", es. Import__EnableSending=true
    // Riga di comando: --Import:EnableSending=true oppure le scorciatoie in SwitchMappings (--send, --giorni=7, ...)
    public class AppSettings
    {
        public ImportOptions Import { get; set; } = new();
        public EmailOptions Email { get; set; } = new();
        public GeminiOptions Gemini { get; set; } = new();
        public PathsOptions Paths { get; set; } = new();
        public AppwriteOptions Appwrite { get; set; } = new();

        // scorciatoie per la riga di comando
        private static readonly Dictionary<string, string> SwitchMappings = new()
        {
            ["--search"] = "Import:EnableSearching",
            ["--send"] = "Import:EnableSending",
            ["--excel"] = "Import:EnableImportingOnExcel",
            ["--db"] = "Import:EnableImportingOnDB",
            ["--coords"] = "Import:UpdateSheetCoordinates",
            ["--reset-db"] = "Import:ResetDb",
            ["--test-immagini"] = "Import:TestImmagini",
            ["--immagini-sheet"] = "Import:CercaImmaginiSheet",
            ["--indirizzi"] = "Import:CercaIndirizzi",
            ["--test-swing"] = "Import:TestSwing",
            ["--test-indirizzi"] = "Import:TestIndirizzi",
            ["--pulisci-lezioni"] = "Import:PulisciLezioni",
            ["--pulisci-lezioni-scrivi"] = "Import:PulisciLezioniScrivi",
            ["--reset-excel"] = "Import:ResetExcel",
            ["--giorni"] = "Import:Giorni",
            ["--radius"] = "Import:Radius",
        };

        public static AppSettings Load(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets(typeof(AppSettings).Assembly, optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(NormalizeArgs(args), SwitchMappings)
                .Build();

            var settings = configuration.Get<AppSettings>() ?? new AppSettings();

            // compatibilità con la variabile d'ambiente usata finora
            if (string.IsNullOrWhiteSpace(settings.Gemini.ApiKey))
                settings.Gemini.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            return settings;
        }

        // "--send" senza valore diventa "--send=true", così i flag si possono scrivere anche da soli
        private static string[] NormalizeArgs(string[] args)
        {
            var result = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                bool isSwitch = arg.StartsWith("--") && !arg.Contains('=');
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                result.Add(isSwitch && !hasValue ? arg + "=true" : arg);
            }
            return result.ToArray();
        }

        public void Print()
        {
            Console.WriteLine("Configurazione:");
            Console.WriteLine($"  EnableSearching = {Import.EnableSearching}");
            Console.WriteLine($"  EnableSending = {Import.EnableSending}, EmailSoloSeCambia = {Import.EmailSoloSeCambia}");
            Console.WriteLine($"  EnableImportingOnExcel = {Import.EnableImportingOnExcel}");
            Console.WriteLine($"  EnableImportingOnDB = {Import.EnableImportingOnDB}");
            Console.WriteLine($"  UpdateSheetCoordinates = {Import.UpdateSheetCoordinates}");
            Console.WriteLine($"  ResetDb = {Import.ResetDb}");
            Console.WriteLine($"  CercaImmaginiSheet = {Import.CercaImmaginiSheet} (max {Import.CercaImmaginiSheetMax} righe)");
            Console.WriteLine($"  CercaIndirizzi = {Import.CercaIndirizzi} (max {Import.CercaIndirizziMax} eventi)");
            Console.WriteLine($"  TestImmagini = {Import.TestImmagini}, TestSwing = {Import.TestSwing}, TestIndirizzi = {Import.TestIndirizzi} (max {Import.TestIndirizziMax})");
            Console.WriteLine($"  PulisciLezioni = {Import.PulisciLezioni}, PulisciLezioniScrivi = {Import.PulisciLezioniScrivi}, ResetExcel = {Import.ResetExcel}");
            Console.WriteLine($"  Giorni = {Import.Giorni}, Radius = {Import.Radius} km");
            Console.WriteLine($"  Cartella dati = {DataPaths.Directory}");
            Console.WriteLine($"  Mailjet = {(Email.HasMailjetKeys ? "configurato" : "mancante")}, Gemini = {(string.IsNullOrWhiteSpace(Gemini.ApiKey) ? "mancante" : Gemini.Model)}");
            Console.WriteLine($"  Appwrite chiave API = {(string.IsNullOrWhiteSpace(Appwrite.ApiKey) ? "mancante (solo permessi Any)" : "configurata")}");
        }
    }

    public class ImportOptions
    {
        public bool EnableSearching { get; set; } = true;
        public bool EnableSending { get; set; }
        // manda l'email solo se ci sono eventi che l'ultima email non aveva (giro quotidiano su GitHub)
        public bool EmailSoloSeCambia { get; set; }
        public bool EnableImportingOnExcel { get; set; }
        public bool EnableImportingOnDB { get; set; }
        // riscrive sullo sheet (colonna F) le coordinate trovate da Photon o verificate nel JSON
        public bool UpdateSheetCoordinates { get; set; }
        public bool ResetDb { get; set; }
        // righe future dello Sheet senza immagine: la cerca sul sito in "Riferimento" e scrive "sì"/"no" nella colonna "Immagine"
        public bool CercaImmaginiSheet { get; set; }
        public int CercaImmaginiSheetMax { get; set; } = 100;
        // righe future dello Sheet senza indirizzo: cerca il locale con Gemini, lo conferma con Photon e lo scrive (stato "gemini")
        public bool CercaIndirizzi { get; set; }
        public int CercaIndirizziMax { get; set; } = 20;
        // conta solo le immagini trovate, senza scrivere su foglio/DB
        public bool TestImmagini { get; set; }
        // classifica con Gemini gli eventi dei prossimi Giorni e stampa le liste, senza email/foglio/DB
        public bool TestSwing { get; set; }
        // cerca con Gemini il locale degli eventi dello sheet senza indirizzo e stampa i risultati, senza scrivere nulla
        public bool TestIndirizzi { get; set; }
        public int TestIndirizziMax { get; set; } = 10;
        // righe future dello Sheet che sembrano lezioni (EventFilter.IsLesson): elenca e basta
        public bool PulisciLezioni { get; set; }
        // come PulisciLezioni, ma le cancella davvero dallo Sheet
        public bool PulisciLezioniScrivi { get; set; }
        // svuota tutto lo Sheet dalla riga 3 (intestazioni, formati e macro restano), con copia JSON prima.
        // Solo sul PC: su GitHub Actions viene ignorato, così un true dimenticato nel repo non svuota lo Sheet ogni giorno
        public bool ResetExcel { get; set; }
        public int Giorni { get; set; } = 7;
        public float Radius { get; set; } = 300;
        public string Centro { get; set; } = "Genova";
    }

    public class EmailOptions
    {
        // destinatari separati da ';'
        public string Recipients { get; set; } = string.Empty;
        public string? MailjetApiKey { get; set; }
        public string? MailjetApiSecret { get; set; }
        // se impostato l'email non parte: il testo HTML viene salvato in questo file (per provare il batch)
        public string? PreviewFile { get; set; }

        public bool HasMailjetKeys => !string.IsNullOrWhiteSpace(MailjetApiKey) && !string.IsNullOrWhiteSpace(MailjetApiSecret);
    }

    public class PathsOptions
    {
        // vuoto = %LOCALAPPDATA%\SiBallaImporter (vedi DataPaths)
        public string? DataDirectory { get; set; }
    }

    public class AppwriteOptions
    {
        // chiave API del progetto (scope databases + files): serve per caricare le immagini su Storage
        public string? ApiKey { get; set; }
    }

    public class GeminiOptions
    {
        // i Flash "pieni" più recenti sul piano gratuito possono impiegare minuti: il lite risponde in pochi secondi
        public string Model { get; set; } = "gemini-flash-lite-latest";
        public string? ApiKey { get; set; }
        // ricerca Google per trovare gli indirizzi: sul piano gratuito risponde 429, serve la fatturazione attiva
        public bool UseGoogleSearch { get; set; }
        // indirizzi "a memoria" del modello, senza strumenti: gratuito ma può inventare (controllo con Photon comunque)
        public bool UseModelKnowledge { get; set; }
    }
}
