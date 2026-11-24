using SiBalla.Service;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System.Text.Json;

namespace SiBallaImportingData.Utils
{
    /// <summary>
    /// Registro delle decisioni del programma sullo stato delle righe ("Luogo verificato") e delle correzioni del curatore:
    /// data/registro-decisioni.json nel repo (lo committano i workflow). Ogni decisione di Gemini, della memoria delle serie o
    /// dell'agente viene registrata; ogni notte il confronto con lo Sheet trova dove il curatore ha cambiato idea (correzione)
    /// o ha deciso una riga lasciata a lui (decisione). Servono a ricavare nuove regole (data/regole-curatore.md) e a misurare
    /// quanto lavoro resta al curatore
    /// </summary>
    public static class RegistroDecisioni
    {
        public const string RepoRelativePath = "data/registro-decisioni.json";
        private const int GiorniDopoLaData = 60;   // voci di eventi passati da più di 60 giorni: tolte

        public sealed class Voce
        {
            public string Data { get; set; } = "";
            public string? Scheda { get; set; }
            public string Titolo { get; set; } = "";
            public string? Organizzatore { get; set; }
            public string? Citta { get; set; }
            public string? Link { get; set; }
            public string Valore { get; set; } = "";     // stato scritto dal programma
            public string Chi { get; set; } = "";        // gemini, memoria, agente
            public string? Motivo { get; set; }
            public string Quando { get; set; } = "";     // data della decisione
            public string? Curatore { get; set; }        // stato messo dal curatore, se diverso
            public string? CuratoreIl { get; set; }
            public string? Tipo { get; set; }            // correzione (aveva deciso il programma) / decisione (la riga era lasciata a lui)
        }

        private static Dictionary<string, Voce>? _voci;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static string? Percorso => DictionaryRepoSync.RepoPath(RepoRelativePath);

        // stesso evento = stessa data, stesso titolo normalizzato, stesso organizzatore e città (le copie Serate/Workshop coincidono)
        private static string Chiave(Event e) =>
            $"{e.DateStart:yyyy-MM-dd}|{KnownVenueStore.NormalizeTitle(e.PartyName)}|{KnownVenueStore.MakeKey(e.Organizer ?? "", e.City ?? "")}";

        private static Dictionary<string, Voce> Voci()
        {
            if (_voci is not null) return _voci;
            try
            {
                _voci = Percorso is { } p && File.Exists(p)
                    ? JsonSerializer.Deserialize<Dictionary<string, Voce>>(File.ReadAllText(p), Json) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[registro] ⚠️ {RepoRelativePath} non letto ({ex.Message}): riparto da vuoto");
                _voci = new();
            }
            return _voci;
        }

        /// <summary>Registra una decisione del programma sullo stato di una riga (la più recente vince)</summary>
        public static void Registra(Event e, string valore, string chi, string? motivo)
        {
            if (string.IsNullOrWhiteSpace(e.PartyName) || string.IsNullOrWhiteSpace(valore)) return;
            Voci()[Chiave(e)] = new Voce
            {
                Data = e.DateStart.ToString("yyyy-MM-dd"), Scheda = e.SheetName, Titolo = e.PartyName.Trim(),
                Organizzatore = e.Organizer?.Trim(), Citta = e.City?.Trim(), Link = e.EventDetail?.WebSite,
                Valore = valore, Chi = chi, Motivo = motivo is null ? null : motivo.Length > 300 ? motivo[..300] : motivo,
                Quando = DateTime.Today.ToString("yyyy-MM-dd"),
            };
        }

        public static void Salva()
        {
            if (_voci is null || Percorso is not { } p) return;
            var limite = DateTime.Today.AddDays(-GiorniDopoLaData).ToString("yyyy-MM-dd");
            foreach (var k in _voci.Where(kv => string.CompareOrdinal(kv.Value.Data, limite) < 0).Select(kv => kv.Key).ToList())
                _voci.Remove(k);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(_voci.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value), Json));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[registro] ⚠️ {RepoRelativePath} non salvato: {ex.Message}");
            }
        }

        // cosa vuol dire uno stato per il sito
        private static string Esito(string? stato) => (stato ?? "").Trim().ToLowerInvariant() switch
        {
            ImportData.VerifiedPending => "da verificare",
            ImportData.VerifiedNo => "no",
            ImportData.VerifiedMercatino => "mercatino",
            ImportData.VerifiedWorkshop => "workshop",
            ImportData.VerifiedSerataWorkshop => "serata+workshop",
            ImportData.VerifiedStorico => "storico",
            _ => "pubblicata",   // nuovo, gemini, ok, auto, vuoto, sì...
        };

        /// <summary>
        /// Confronta il registro con lo Sheet (dopo ImportData.LeggiNoteVerificaAsync): le righe in cui il curatore ha messo uno stato
        /// diverso da quello del programma diventano correzioni (o decisioni, se il programma l'aveva lasciata "da verificare").
        /// Restituisce quelle nuove di questo controllo
        /// </summary>
        public static List<Voce> RilevaCorrezioni(IEnumerable<Event> sheetEvents)
        {
            var nuove = new List<Voce>();
            var voci = Voci();
            foreach (var e in sheetEvents)
            {
                if (string.IsNullOrWhiteSpace(e.PartyName) || !voci.TryGetValue(Chiave(e), out var v) || v.Curatore is not null) continue;
                // copie automatiche in Workshop: la decisione è quella della riga di Serate
                if (ImportData.NotaCopiaDaSerate(e.NotaVerifica)) continue;
                // stato scritto dal programma (nota Gemini/Memoria/Agente, senza arancione appena tolto): non è del curatore
                var nota = (e.NotaVerifica ?? "").TrimStart();
                bool delProgramma = !e.RigaArancione && (nota.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)
                    || nota.StartsWith(MemoriaSerie.PrefissoNota, StringComparison.OrdinalIgnoreCase)
                    || nota.StartsWith(MemoriaSerie.PrefissoAgente, StringComparison.OrdinalIgnoreCase));
                var attuale = Esito(e.LuogoVerificato);
                if (attuale == Esito(v.Valore) || attuale is "storico") continue;
                if (delProgramma && e.LuogoVerificato is not (ImportData.VerifiedOk or "")) continue;
                // "da verificare" rimasto, oppure "nuovo"/"gemini" del programma dopo una sua stessa decisione: niente del curatore
                if (attuale == "da verificare" || e.LuogoVerificato is ImportData.VerifiedNew or ImportData.VerifiedGemini) continue;
                v.Curatore = string.IsNullOrWhiteSpace(e.LuogoVerificato) ? "(vuoto = confermata)" : e.LuogoVerificato;
                v.CuratoreIl = DateTime.Today.ToString("yyyy-MM-dd");
                v.Tipo = Esito(v.Valore) == "da verificare" ? "decisione" : "correzione";
                nuove.Add(v);
            }
            return nuove;
        }

        /// <summary>Correzioni e decisioni del curatore degli ultimi giorni (per il riepilogo e per proporre regole)</summary>
        public static List<Voce> UltimeDelCuratore(int giorni)
        {
            var da = DateTime.Today.AddDays(-giorni).ToString("yyyy-MM-dd");
            return Voci().Values.Where(v => v.CuratoreIl is not null && string.CompareOrdinal(v.CuratoreIl, da) >= 0).ToList();
        }
    }
}
