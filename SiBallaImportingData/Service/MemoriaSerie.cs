using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;

namespace SiBallaImportingData.Service
{
    /// <summary>
    /// Memoria delle serie: quello che hai deciso nella colonna "Luogo verificato" per le altre date dello stesso evento
    /// (organizzatore + città + titolo normalizzato, come il dizionario degli stili) vale anche per le date nuove,
    /// senza chiedere a Gemini.
    ///  - almeno 2 date scartate da te ("no") e nessuna confermata -> "no"
    ///  - conferme che pesano almeno 2 e nessuna data scartata -> "nuovo" (va sul DB)
    ///  - storia mista o troppo corta -> decide Gemini come prima
    /// Conferma tua = "ok", cella vuota, "auto" o un altro valore scritto da te (peso 1); conferma tacita = riga
    /// "nuovo"/"gemini" arrivata alla sua data senza che tu l'abbia scartata (peso 0,5: non è detto che tu l'abbia guardata).
    /// Non conta: "da verificare", "storico", i "no" con la nota di Gemini (--riclassifica-storico) e quelli scritti dalla
    /// memoria stessa (nota "Memoria: ..."), altrimenti il programma si darebbe ragione da solo.
    /// Le date doppie (stessa serie, stesso giorno, una riga "no" e un'altra no) sono duplicati tolti, non scarti
    /// </summary>
    public sealed class MemoriaSerie
    {
        public const string PrefissoNota = "Memoria:";
        private const int MinScarti = 2;
        private const double MinConferme = 2;
        private const double PesoTacito = 0.5;

        private sealed class Storia
        {
            public int SiTuoi, SiTaciti, No;
            public double Si => SiTuoi + SiTaciti * PesoTacito;
        }

        private enum Voto { Nessuno, SiTuo, SiTacito, No }

        // somma delle serie di un organizzatore (in tutte le città), con qualche titolo d'esempio: non decide niente,
        // va a Gemini come indizio per gli eventi che la memoria non decide (StoriaOrganizzatore)
        private sealed class Organizzatore
        {
            public int SiTuoi, SiTaciti, No;
            public List<string> TitoliSi = new(), TitoliNo = new();
        }
        private const int MaxTitoliEsempio = 3;

        private readonly Dictionary<string, Storia> _serie = new();
        private readonly Dictionary<string, Organizzatore> _organizzatori = new();

        private static string? ChiaveOrganizzatore(Event e) =>
            string.IsNullOrWhiteSpace(e.Organizer) ? null : KnownVenueStore.MakeKey(e.Organizer, "");

        private MemoriaSerie() { }

        /// <summary>Organizzatore + città + titolo normalizzato, null se manca l'organizzatore o il titolo</summary>
        public static string? Chiave(Event e)
        {
            var titolo = KnownVenueStore.NormalizeTitle(e.PartyName);
            if (string.IsNullOrWhiteSpace(e.Organizer) || titolo.Length == 0) return null;
            return KnownVenueStore.MakeKey(e.Organizer, e.City ?? "") + "|" + titolo;
        }

        private static Voto VotoDi(Event e)
        {
            var nota = e.NotaVerifica ?? "";
            bool notaDelProgramma = nota.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)
                                    || nota.StartsWith(PrefissoNota, StringComparison.OrdinalIgnoreCase);
            switch (e.LuogoVerificato ?? "")
            {
                // riga ancora arancione = l'hai appena cambiata da "da verificare" (colore e nota di Gemini spariscono al giro dopo)
                case ImportData.VerifiedNo:
                    return e.RigaArancione || !notaDelProgramma ? Voto.No : Voto.Nessuno;
                case ImportData.VerifiedNew:
                case ImportData.VerifiedGemini:
                    return e.DateStart.Date < DateTime.Today ? Voto.SiTacito : Voto.Nessuno;
                case ImportData.VerifiedPending:
                case ImportData.VerifiedStorico:
                    return Voto.Nessuno;
                default:
                    return Voto.SiTuo;
            }
        }

        /// <summary>Costruisce la memoria dalle righe dello Sheet (dopo ImportData.LeggiNoteVerificaAsync)</summary>
        public static MemoriaSerie Da(IEnumerable<Event> sheetEvents)
        {
            var memoria = new MemoriaSerie();
            foreach (var serie in sheetEvents.Select(e => (Chiave: Chiave(e), Evento: e)).Where(x => x.Chiave is not null).GroupBy(x => x.Chiave!))
            {
                var storia = new Storia();
                foreach (var giorno in serie.GroupBy(x => x.Evento.DateStart.Date))
                {
                    var voti = giorno.Select(x => VotoDi(x.Evento)).ToList();
                    if (voti.Contains(Voto.SiTuo)) storia.SiTuoi++;
                    else if (voti.Contains(Voto.SiTacito)) storia.SiTaciti++;
                    else if (voti.All(v => v == Voto.No)) storia.No++;
                }
                if (storia.Si == 0 && storia.No == 0) continue;
                memoria._serie[serie.Key] = storia;

                var primo = serie.First().Evento;
                if (ChiaveOrganizzatore(primo) is not string chiaveOrg) continue;
                if (!memoria._organizzatori.TryGetValue(chiaveOrg, out var org))
                    memoria._organizzatori[chiaveOrg] = org = new Organizzatore();
                org.SiTuoi += storia.SiTuoi;
                org.SiTaciti += storia.SiTaciti;
                org.No += storia.No;
                var titolo = primo.PartyName?.Trim() ?? "";
                if (titolo.Length > 60) titolo = titolo[..60];
                var titoli = storia.No > 0 && storia.Si == 0 ? org.TitoliNo : storia.Si > 0 && storia.No == 0 ? org.TitoliSi : null;
                if (titoli is not null && titoli.Count < MaxTitoliEsempio && !titoli.Contains(titolo))
                    titoli.Add(titolo);
            }

            int swing = memoria._serie.Values.Count(s => s.Si >= MinConferme && s.No == 0);
            int scartate = memoria._serie.Values.Count(s => s.No >= MinScarti && s.Si == 0);
            Console.WriteLine($"[memoria] serie con una storia: {memoria._serie.Count}; decise: {swing} swing, {scartate} scartate " +
                              $"(le altre: storia mista o troppo corta, decide Gemini)");
            return memoria;
        }

        /// <summary>
        /// Le tue decisioni sugli altri eventi dello stesso organizzatore, in una riga per Gemini
        /// (es. "4 date confermate dal curatore, 2 scartate; scartati: \"Corso Lindy\""), null se non ce ne sono
        /// </summary>
        public string? StoriaOrganizzatore(Event e)
        {
            if (ChiaveOrganizzatore(e) is not string chiave || !_organizzatori.TryGetValue(chiave, out var o)) return null;
            var parti = new List<string>();
            static string Date(int n, string participio) => n == 1 ? $"1 data {participio}a" : $"{n} date {participio}e";
            if (o.SiTuoi > 0) parti.Add(Date(o.SiTuoi, "confermat") + " dal curatore");
            if (o.SiTaciti > 0) parti.Add(Date(o.SiTaciti, "pubblicat") + " senza obiezioni");
            if (o.No > 0) parti.Add(Date(o.No, "scartat") + " dal curatore");
            var testo = string.Join(", ", parti);
            if (o.TitoliSi.Count > 0) testo += "; confermati: " + string.Join(", ", o.TitoliSi.Select(t => $"\"{t}\""));
            if (o.TitoliNo.Count > 0) testo += "; scartati: " + string.Join(", ", o.TitoliNo.Select(t => $"\"{t}\""));
            return testo;
        }

        /// <summary>
        /// Stato da dare a una data nuova della serie ("nuovo" o "no") con il motivo per la nota, null se la memoria non decide
        /// </summary>
        public (string Stato, string Motivo)? Decidi(Event e)
        {
            if (Chiave(e) is not string chiave || !_serie.TryGetValue(chiave, out var s)) return null;
            if (s.No >= MinScarti && s.Si == 0)
                return (ImportData.VerifiedNo, $"{PrefissoNota} {s.No} date di questa serie scartate da te, nessuna confermata");
            if (s.Si >= MinConferme && s.No == 0)
                return (ImportData.VerifiedNew, $"{PrefissoNota} swing come le altre date " +
                                                $"({s.SiTuoi} confermate da te, {s.SiTaciti} passate senza obiezioni)");
            return null;
        }
    }
}
