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
            ["--riclassifica-sheet"] = "Import:RiclassificaSheet",
            ["--riclassifica-sheet-scrivi"] = "Import:RiclassificaSheetScrivi",
            ["--riclassifica-storico"] = "Import:RiclassificaStorico",
            ["--riclassifica-storico-scrivi"] = "Import:RiclassificaStoricoScrivi",
            ["--memoria-serie"] = "Import:MemoriaSerie",
            ["--memoria-serie-scrivi"] = "Import:MemoriaSerieScrivi",
            ["--test-gemini-sheet"] = "Import:TestGeminiSheet",
            ["--agente-verifica"] = "Import:AgenteVerifica",
            ["--agente-prova"] = "Import:AgenteProva",
            ["--agente-max"] = "Import:AgenteMax",
            ["--agente-indirizzi-prova"] = "Import:AgenteIndirizziProva",
            ["--agente-dizionario"] = "Import:AgenteDizionario",
            ["--agente-dizionario-scrivi"] = "Import:AgenteDizionarioScrivi",
            ["--indirizzi-agente"] = "Import:IndirizziConAgente",
            ["--agente-qualita"] = "Import:AgenteQualita",
            ["--agente-qualita-scrivi"] = "Import:AgenteQualitaScrivi",
            ["--reset-excel"] = "Import:ResetExcel",
            ["--giorni"] = "Import:Giorni",
            ["--radius"] = "Import:Radius",
            ["--radius-email"] = "Import:RadiusEmail",
            ["--no-push"] = "Import:NoPush",
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
            if (string.IsNullOrWhiteSpace(settings.Import.TavilyApiKey))
                settings.Import.TavilyApiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
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
            Console.WriteLine($"  IndirizziConAgente = {Import.IndirizziConAgente} (max {Import.AgenteDizionarioMax} eventi per giro), modello agente = {(string.IsNullOrWhiteSpace(Gemini.AgenteModel) ? Gemini.Model + " (lo stesso della classificazione)" : Gemini.AgenteModel)}, Tavily = {(string.IsNullOrWhiteSpace(Import.TavilyApiKey) ? "no" : $"sì (max {Import.TavilyMaxGiorno} ricerche al giorno, {Import.TavilyMaxMese} al mese)")}");
            Console.WriteLine($"  TestImmagini = {Import.TestImmagini}, TestSwing = {Import.TestSwing}, TestIndirizzi = {Import.TestIndirizzi} (max {Import.TestIndirizziMax})");
            Console.WriteLine($"  PulisciLezioni = {Import.PulisciLezioni}, PulisciLezioniScrivi = {Import.PulisciLezioniScrivi}, ResetExcel = {Import.ResetExcel}");
            Console.WriteLine($"  NoPush = {Import.NoPush}, RiclassificaSheet = {Import.RiclassificaSheet}, RiclassificaSheetScrivi = {Import.RiclassificaSheetScrivi}");
            Console.WriteLine($"  Giorni = {Import.Giorni}, Radius = {Import.Radius} km, RadiusEmail = {Import.RadiusEmail ?? Import.Radius} km");
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
        // prove dal PC: a fine giro il dizionario dei luoghi torna nel repo, ma senza commit né push
        public bool NoPush { get; set; }
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
        // vecchie righe future "da verificare" senza colore né nota: le classifica Gemini e mostra l'esito
        public bool RiclassificaSheet { get; set; }
        // come RiclassificaSheet, ma scrive nello Sheet ("nuovo" oppure riga arancione con il motivo nella nota)
        public bool RiclassificaSheetScrivi { get; set; }
        // righe "storico" (eventi passati recuperati) mai controllate: le classifica Gemini e mostra l'esito
        public bool RiclassificaStorico { get; set; }
        // come RiclassificaStorico, ma scrive nello Sheet (swing: nota + stile; non swing: "no" + motivo)
        public bool RiclassificaStoricoScrivi { get; set; }
        // memoria delle serie sulle righe future già nello Sheet (MemoriaSerie): mostra cosa cambierebbe
        // ("da verificare" -> "nuovo" se la serie è sempre confermata, -> "no" se l'hai sempre scartata)
        public bool MemoriaSerie { get; set; }
        // come MemoriaSerie, ma scrive nello Sheet (ogni giro con --excel lo fa già da solo)
        public bool MemoriaSerieScrivi { get; set; }
        // prova di Gemini sulle righe che hai già deciso tu (workshop, mercatini, no, serate confermate): confronta e basta
        public bool TestGeminiSheet { get; set; }
        // righe per tipo nella prova (le più recenti)
        public int TestGeminiSheetMax { get; set; } = 40;
        // agente di verifica (Service/AgenteVerifica.cs): sulle righe future "nuovo", "gemini" e "da verificare" legge la pagina
        // dell'evento, cerca nello Sheet e PROPONE lo stato con il motivo. Non scrive niente nello Sheet
        public bool AgenteVerifica { get; set; }
        // prova dell'agente sulle righe che hai già deciso tu (senza fargli vedere la tua decisione): confronta e basta
        public bool AgenteProva { get; set; }
        // righe da controllare (verifica) o righe per tipo (prova)
        public int AgenteMax { get; set; } = 15;
        // tetto per riga: chiamate a Gemini e pagine web lette
        public int AgentePassi { get; set; } = 8;
        public int AgentePagine { get; set; } = 3;
        // ricerche web (DuckDuckGo) per riga, servono soprattutto a trovare l'indirizzo
        public int AgenteRicerche { get; set; } = 2;
        // prova sugli indirizzi: righe con indirizzo controllato da te, l'agente deve ritrovarlo senza vederlo (confronto in km)
        public bool AgenteIndirizziProva { get; set; }
        // luoghi degli eventi futuri senza indirizzo: l'agente li cerca (pagine, mappa, ricerca web se c'è) e li mostra;
        // con AgenteDizionarioScrivi li salva nel dizionario (non verificati, Source "agente") e nello Sheet come "gemini"
        public bool AgenteDizionario { get; set; }
        public bool AgenteDizionarioScrivi { get; set; }
        // giro dell'importatore: con CercaIndirizzi (--indirizzi) cerca i luoghi con l'agente invece che con la ricerca semplice di
        // Gemini, al massimo AgenteDizionarioMax eventi per giro (i luoghi già trovati dall'agente si riusano senza contarli)
        public bool IndirizziConAgente { get; set; }
        // controllo qualità dello Sheet (Application/ControlloQualita.cs): doppioni, date sospette, serie contraddittorie,
        // dati mancanti, orari strani. Solo proposte, senza Gemini; di notte dal workflow "Agente verifica"
        public bool AgenteQualita { get; set; }
        // come AgenteQualita, ma cancella dallo Sheet i doppioni sicuri (tiene una riga per evento); il resto resta una proposta
        public bool AgenteQualitaScrivi { get; set; }
        public int AgenteDizionarioMax { get; set; } = 5;
        // ricerca web gratuita per l'agente (tavily.com, piano gratuito senza carta); vuoto = variabile TAVILY_API_KEY
        public string? TavilyApiKey { get; set; }
        // tetto alle ricerche Tavily (piano gratuito ~1000 al mese): contate in tavily-uso.json nella cartella dati
        public int TavilyMaxGiorno { get; set; } = 30;
        public int TavilyMaxMese { get; set; } = 900;
        // svuota tutto lo Sheet dalla riga 3 (intestazioni, formati e macro restano), con copia JSON prima.
        // Solo sul PC: su GitHub Actions viene ignorato, così un true dimenticato nel repo non svuota lo Sheet ogni giorno
        public bool ResetExcel { get; set; }
        public int Giorni { get; set; } = 7;
        public float Radius { get; set; } = 300;
        // Se omesso, per le email si usa il raggio di ricerca.
        public float? RadiusEmail { get; set; }
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
        // modello dell'agente di verifica (vuoto = Model): lavora a più passi, un modello più capace sbaglia meno
        public string? AgenteModel { get; set; }
        public string? ApiKey { get; set; }
        // ricerca Google per trovare gli indirizzi: sul piano gratuito risponde 429, serve la fatturazione attiva
        public bool UseGoogleSearch { get; set; }
        // indirizzi "a memoria" del modello, senza strumenti: gratuito ma può inventare (controllo con Photon comunque)
        public bool UseModelKnowledge { get; set; }
    }
}
