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
    ///  - almeno 1 data segnata da te "mercatino" (o "workshop") e nessun'altra decisione -> "mercatino" (Bacheca)
    ///    o "workshop" (la riga va nella scheda Workshop)
    ///  - storia mista o troppo corta -> si guarda l'organizzatore (sotto), altrimenti decide Gemini come prima
    /// Organizzatore (tutte le sue serie, in tutte le città): se le tue decisioni sono almeno 2 date e tutte dello stesso
    /// tipo ("no", "mercatino" o "workshop"), senza nessuna conferma, anche i suoi eventi nuovi prendono quel valore.
    /// Conferma tua = "ok", cella vuota, "auto" o un altro valore scritto da te (peso 1); conferma tacita = riga
    /// "nuovo"/"gemini" arrivata alla sua data senza che tu l'abbia scartata (peso 0,5: non è detto che tu l'abbia guardata).
    /// Non conta: "da verificare", "storico", i valori con la nota di Gemini (--riclassifica-storico) o della memoria stessa
    /// (nota "Memoria: ..."), altrimenti il programma si darebbe ragione da solo, e le copie in Workshop delle righe di Serate
    /// segnate "workshop" (nota "Spostato da Serate ...": la decisione è già contata nella riga di Serate).
    /// Le date doppie (stessa serie, stesso giorno, una riga "no" e un'altra no) sono duplicati tolti, non scarti
    /// </summary>
    public sealed class MemoriaSerie
    {
        public const string PrefissoNota = "Memoria:";
        // nota delle correzioni fatte dall'agente (es. "Agente: doppione della riga N"): come Gemini e la memoria, non è una tua decisione
        public const string PrefissoAgente = "Agente:";
        private const int MinScarti = 2;
        private const double MinConferme = 2;
        private const double PesoTacito = 0.5;
        // date decise da te (tutte dello stesso tipo) perché valga la regola dell'organizzatore: con 1 sola data
        // sullo Sheet del 22/11/2026 gli organizzatori "solo no" sarebbero 103, con 2 sono 15
        private const int MinOrganizzatore = 2;

        private sealed class Storia
        {
            public int SiTuoi, SiTaciti, No, Mercatini, Workshop, SerataWorkshop;
            public double Si => SiTuoi + SiTaciti * PesoTacito;
        }

        private enum Voto { Nessuno, SiTuo, SiTacito, No, Mercatino, Workshop, SerataWorkshop }

        // somma delle serie di un organizzatore (in tutte le città), con qualche titolo d'esempio: decide da solo solo se
        // le tue decisioni sono tutte dello stesso tipo (OrganizzatoreDecide), altrimenti va a Gemini come indizio
        private sealed class Organizzatore
        {
            public int SiTuoi, SiTaciti, No, Mercatini, Workshop;
            public double Si => SiTuoi + SiTaciti * PesoTacito;
            public List<string> TitoliSi = new(), TitoliNo = new();
        }
        private const int MaxTitoliEsempio = 3;

        private readonly Dictionary<string, Storia> _serie = new();
        private readonly Dictionary<string, Organizzatore> _organizzatori = new();

        // ---- parole imparate (24/11/2026): una parola (o coppia di parole) del titolo che compare solo in eventi scartati da te,
        // di almeno MinParolaOrganizzatori organizzatori diversi, e mai in un evento che hai tenuto, diventa una regola "no" per
        // gli eventi nuovi e per le righe future "nuovo"/"gemini"/"da verificare". Non vale se il titolo nomina uno stile del
        // calendario (stesso freno dell'agente) o se la serie/l'organizzatore hanno date confermate. Basta un tuo "sì" su un
        // evento con quella parola perché la regola sparisca al giro dopo.
        private const int MinParolaNo = 5;
        private const int MinParolaOrganizzatori = 6;   // con 3 uscivano "paris", "cours", "open" (provato il 24/11/2026)
        public const string RegoleImparateRepoPath = "data/regole-imparate.md";
        private sealed class Parola
        {
            public int No, Tenuti;
            public HashSet<string> Organizzatori = new();
            public List<string> Titoli = new();
        }
        private readonly Dictionary<string, Parola> _regoleParole = new();

        private static readonly HashSet<string> ParoleVuote = new(StringComparer.OrdinalIgnoreCase)
        {
            "del", "della", "delle", "dei", "degli", "alla", "alle", "agli", "con", "per", "tra", "fra", "una", "uno", "the", "and",
            "for", "with", "des", "les", "une", "aux", "avec", "pour", "par", "der", "die", "das", "und", "mit", "von",
            "lunedi", "martedi", "mercoledi", "giovedi", "venerdi", "sabato", "domenica", "monday", "tuesday", "wednesday",
            "thursday", "friday", "saturday", "sunday", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche",
            "edizione", "edition", "night", "serata", "soiree", "party", "social", "dance", "swing", "festival", "event",
        };

        /// <summary>Parole del titolo (minuscole, senza accenti, >= 3 lettere, niente mesi/giorni/numeri) e coppie di parole vicine</summary>
        private static HashSet<string> Parole(string? titolo)
        {
            var t = KnownVenueStore.NormalizeTitle(titolo);
            var parole = System.Text.RegularExpressions.Regex.Matches(t, @"[a-z]+").Select(m => m.Value)
                .Where(w => w.Length >= 3 && !ParoleVuote.Contains(w) && !MesiEOrdinali.IsMatch(w)).ToList();
            var out_ = new HashSet<string>(parole);
            for (int i = 0; i + 1 < parole.Count; i++) out_.Add(parole[i] + " " + parole[i + 1]);
            return out_;
        }

        private void ImparaParole(IEnumerable<Event> sheetEvents)
        {
            var tutte = new Dictionary<string, Parola>();
            foreach (var e in sheetEvents)
            {
                if (string.IsNullOrWhiteSpace(e.PartyName) || AgenteVerifica.StileDelCalendario.IsMatch(e.PartyName)) continue;
                var voto = VotoDi(e);
                if (voto is not (Voto.No or Voto.SiTuo or Voto.SiTacito or Voto.SerataWorkshop)) continue;
                foreach (var w in Parole(e.PartyName))
                {
                    if (!tutte.TryGetValue(w, out var p)) tutte[w] = p = new Parola();
                    if (voto != Voto.No) { p.Tenuti++; continue; }
                    p.No++;
                    p.Organizzatori.Add(KnownVenueStore.MakeKey(e.Organizer ?? "", ""));
                    var titolo = e.PartyName.Trim();
                    if (p.Titoli.Count < MaxTitoliEsempio && !p.Titoli.Contains(titolo)) p.Titoli.Add(titolo.Length > 60 ? titolo[..60] : titolo);
                }
            }
            foreach (var (w, p) in tutte)
                if (p.Tenuti == 0 && p.No >= MinParolaNo && p.Organizzatori.Count >= MinParolaOrganizzatori)
                    _regoleParole[w] = p;
            // una coppia di parole è inutile se una delle due è già una regola da sola
            foreach (var coppia in _regoleParole.Keys.Where(k => k.Contains(' ')).ToList())
                if (coppia.Split(' ').Any(_regoleParole.ContainsKey)) _regoleParole.Remove(coppia);
        }

        /// <summary>Le regole imparate, una per riga ("🧠 «parola»: N scartati da te, ..."), per il log, l'email e data/regole-imparate.md</summary>
        public List<string> RegoleImparate() => _regoleParole.OrderByDescending(x => x.Value.No)
            .Select(x => $"🧠 «{x.Key}» nel titolo -> no: {x.Value.No} eventi scartati da te ({x.Value.Organizzatori.Count} organizzatori), " +
                         $"nessuno tenuto; es. {string.Join(", ", x.Value.Titoli.Select(t => $"\"{t}\""))}")
            .ToList();

        private void SalvaRegoleImparate()
        {
            if (DictionaryRepoSync.RepoPath(RegoleImparateRepoPath) is not { } p) return;
            try
            {
                var testo = "# Regole imparate dal programma\n\n" +
                            "Le scrive da solo l'importatore a ogni giro, dalle tue decisioni in \"Luogo verificato\" (non modificarle qui).\n" +
                            "Una parola del titolo diventa una regola \"no\" se compare in almeno " + MinParolaNo + " eventi scartati da te, di almeno " +
                            MinParolaOrganizzatori + " organizzatori, e in nessun evento che hai tenuto.\n" +
                            "Non vale se il titolo nomina uno stile del calendario (Lindy, Balboa, Blues, Shag, Charleston, Boogie, Solo Jazz, Rhythm 'n' Blues).\n" +
                            "Per togliere una regola basta un tuo \"sì\" (o \"ok\") su un evento con quella parola. Per le regole scritte da te: regole-curatore.md.\n\n" +
                            string.Join("\n", RegoleImparate().Select(r => "- " + r[2..].Trim())) + "\n";
                if (File.Exists(p) && File.ReadAllText(p) == testo) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, testo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[memoria] regole imparate non salvate: {ex.Message}");
            }
        }

        private static string? ChiaveOrganizzatore(Event e) =>
            string.IsNullOrWhiteSpace(e.Organizer) ? null : KnownVenueStore.MakeKey(e.Organizer, "");

        private MemoriaSerie() { }

        // mesi (it, en, fr, de, es) e ordinali ("4th", "3a"): "Brescia Balboa Series 4TH Edition February" e "... March" sono
        // la stessa serie. Solo qui: NormalizeTitle serve anche agli id delle immagini e al dizionario, che non devono cambiare
        private static readonly System.Text.RegularExpressions.Regex MesiEOrdinali = new(
            @"\b(gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre" +
            @"|january|february|march|april|may|june|july|august|september|october|november|december" +
            @"|janvier|fevrier|mars|avril|mai|juin|juillet|aout|septembre|octobre|decembre" +
            @"|januar|februar|marz|maerz|juni|juli|oktober|dezember" +
            @"|enero|febrero|abril|mayo|junio|julio|septiembre|octubre|noviembre|diciembre" +
            @"|\d+(st|nd|rd|th|a|o|e|er|eme|ème))\b");

        /// <summary>Titolo della serie: NormalizeTitle senza mesi né ordinali (se resta qualcosa)</summary>
        public static string TitoloSerie(string? titolo)
        {
            var t = KnownVenueStore.NormalizeTitle(titolo);
            var senza = System.Text.RegularExpressions.Regex.Replace(MesiEOrdinali.Replace(t, " "), @"\s+", " ").Trim();
            return senza.Length > 0 ? senza : t;
        }

        /// <summary>Organizzatore + città + titolo della serie, null se manca l'organizzatore o il titolo</summary>
        public static string? Chiave(Event e)
        {
            var titolo = TitoloSerie(e.PartyName);
            if (string.IsNullOrWhiteSpace(e.Organizer) || titolo.Length == 0) return null;
            return KnownVenueStore.MakeKey(e.Organizer, e.City ?? "") + "|" + titolo;
        }

        private static Voto VotoDi(Event e)
        {
            var nota = e.NotaVerifica ?? "";
            // copia in Workshop di una riga di Serate: la decisione è già contata là
            if (ImportData.NotaCopiaDaSerate(nota)) return Voto.Nessuno;
            // riga di Workshop messa lì dalla memoria (workshop lungo più di un giorno): idem, non è una tua conferma
            if (e.SheetName == TipoEvento.Workshop.ToString() && nota.StartsWith(PrefissoNota, StringComparison.OrdinalIgnoreCase)) return Voto.Nessuno;
            bool notaDelProgramma = nota.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)
                                    || nota.StartsWith(PrefissoNota, StringComparison.OrdinalIgnoreCase)
                                    || nota.StartsWith(PrefissoAgente, StringComparison.OrdinalIgnoreCase);
            // riga ancora arancione = l'hai appena cambiata da "da verificare" (colore e nota di Gemini spariscono al giro dopo)
            bool tuo = e.RigaArancione || !notaDelProgramma;
            switch (e.LuogoVerificato ?? "")
            {
                case ImportData.VerifiedNo:
                    return tuo ? Voto.No : Voto.Nessuno;
                // come per "no": vale solo se l'hai scritto tu (quelli di Gemini e della memoria hanno la nota del programma)
                case ImportData.VerifiedMercatino:
                    return tuo ? Voto.Mercatino : Voto.Nessuno;
                case ImportData.VerifiedWorkshop:
                    return tuo ? Voto.Workshop : Voto.Nessuno;
                case ImportData.VerifiedSerataWorkshop:
                    return tuo ? Voto.SerataWorkshop : Voto.Nessuno;
                case ImportData.VerifiedNew:
                case ImportData.VerifiedGemini:
                    return e.DateStart.Date < DateTime.Today ? Voto.SiTacito : Voto.Nessuno;
                case ImportData.VerifiedPending:
                case ImportData.VerifiedStorico:
                    return Voto.Nessuno;
                default:
                    // valore scritto dall'agente (nota "Agente: …"): pubblicato ma non controllato da te, come "nuovo"
                    if (nota.StartsWith(PrefissoAgente, StringComparison.OrdinalIgnoreCase))
                        return e.DateStart.Date < DateTime.Today ? Voto.SiTacito : Voto.Nessuno;
                    return Voto.SiTuo;
            }
        }

        /// <summary>Costruisce la memoria dalle righe dello Sheet (dopo ImportData.LeggiNoteVerificaAsync)</summary>
        public static MemoriaSerie Da(IEnumerable<Event> sheetEvents)
        {
            var memoria = new MemoriaSerie();
            foreach (var serie in sheetEvents.Select(e => (Chiave: Chiave(e), Evento: e)).Where(x => x.Chiave is not null).GroupBy(x => x.Chiave!))
            {
                // per la serie una conferma nella scheda Workshop vale "workshop" (le date nuove vanno là);
                // per l'organizzatore resta una conferma, così chi fa anche festival non vede finire in Workshop le sue serate
                var storia = new Storia();
                var perOrganizzatore = new Storia();
                foreach (var giorno in serie.GroupBy(x => x.Evento.DateStart.Date))
                {
                    var voti = giorno.Select(x => (x.Evento, Voto: VotoDi(x.Evento))).ToList();
                    // per l'organizzatore "serata+workshop" è una conferma
                    Conta(perOrganizzatore, voti.Select(x => x.Voto == Voto.SerataWorkshop ? Voto.SiTuo : x.Voto).ToList());
                    Conta(storia, voti.Select(x => x.Voto is Voto.SiTuo or Voto.SiTacito && x.Evento.SheetName == TipoEvento.Workshop.ToString()
                        ? Voto.Workshop : x.Voto).ToList());
                }
                if (storia.Si == 0 && storia.No == 0 && storia.Mercatini == 0 && storia.Workshop == 0 && storia.SerataWorkshop == 0) continue;
                memoria._serie[serie.Key] = storia;

                var primo = serie.First().Evento;
                if (ChiaveOrganizzatore(primo) is not string chiaveOrg) continue;
                if (!memoria._organizzatori.TryGetValue(chiaveOrg, out var org))
                    memoria._organizzatori[chiaveOrg] = org = new Organizzatore();
                org.SiTuoi += perOrganizzatore.SiTuoi;
                org.SiTaciti += perOrganizzatore.SiTaciti;
                org.No += perOrganizzatore.No;
                org.Mercatini += perOrganizzatore.Mercatini;
                org.Workshop += perOrganizzatore.Workshop;
                var titolo = primo.PartyName?.Trim() ?? "";
                if (titolo.Length > 60) titolo = titolo[..60];
                var titoli = perOrganizzatore.No > 0 && perOrganizzatore.Si == 0 ? org.TitoliNo
                           : perOrganizzatore.Si > 0 && perOrganizzatore.No == 0 ? org.TitoliSi : null;
                if (titoli is not null && titoli.Count < MaxTitoliEsempio && !titoli.Contains(titolo))
                    titoli.Add(titolo);
            }

            int Serie(string stato) => memoria._serie.Values.Count(s => SerieDecide(s) == stato);
            var perOrg = memoria._organizzatori.Values.Select(OrganizzatoreDecide).Where(x => x is not null)
                .GroupBy(x => x!.Value.Stato).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"[memoria] serie con una storia: {memoria._serie.Count}; decise: {Serie(ImportData.VerifiedNew)} swing, " +
                              $"{Serie(ImportData.VerifiedNo)} scartate, {Serie(ImportData.VerifiedMercatino)} mercatini, " +
                              $"{Serie(ImportData.VerifiedWorkshop)} workshop, {Serie(ImportData.VerifiedSerataWorkshop)} serata+workshop (le altre: storia mista o troppo corta)");
            Console.WriteLine($"[memoria] organizzatori decisi per tutti i loro eventi: " +
                              $"{perOrg.GetValueOrDefault(ImportData.VerifiedNo)} scartati, {perOrg.GetValueOrDefault(ImportData.VerifiedMercatino)} mercatini, " +
                              $"{perOrg.GetValueOrDefault(ImportData.VerifiedWorkshop)} workshop (su {memoria._organizzatori.Count} con una storia)");

            memoria.ImparaParole(sheetEvents);
            Console.WriteLine($"[memoria] regole imparate dalle parole dei titoli: {memoria._regoleParole.Count} (elenco in {RegoleImparateRepoPath})");
            foreach (var r in memoria.RegoleImparate()) Console.WriteLine(r);
            memoria.SalvaRegoleImparate();
            return memoria;
        }

        // un giorno della serie conta una volta: vince la conferma, poi lo scarto (solo se tutte le righe sono "no"), mercatino, workshop
        private static void Conta(Storia storia, List<Voto> voti)
        {
            if (voti.Contains(Voto.SerataWorkshop)) storia.SerataWorkshop++;
            else if (voti.Contains(Voto.SiTuo)) storia.SiTuoi++;
            else if (voti.Contains(Voto.SiTacito)) storia.SiTaciti++;
            else if (voti.All(v => v == Voto.No)) storia.No++;
            else if (voti.Contains(Voto.Mercatino)) storia.Mercatini++;
            else if (voti.Contains(Voto.Workshop)) storia.Workshop++;
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
            if (o.Workshop > 0) parti.Add(Date(o.Workshop, "segnat") + " come workshop/corso dal curatore");
            if (o.Mercatini > 0) parti.Add(Date(o.Mercatini, "segnat") + " come mercatino dal curatore");
            if (parti.Count == 0) return null;
            var testo = string.Join(", ", parti);
            if (o.TitoliSi.Count > 0) testo += "; confermati: " + string.Join(", ", o.TitoliSi.Select(t => $"\"{t}\""));
            if (o.TitoliNo.Count > 0) testo += "; scartati: " + string.Join(", ", o.TitoliNo.Select(t => $"\"{t}\""));
            return testo;
        }

        // stato deciso dalla storia della serie, null se mista o troppo corta
        private static string? SerieDecide(Storia s)
        {
            // "serata+workshop" da te, eventualmente con date passate senza obiezioni, e nient'altro
            if (s.SerataWorkshop > 0)
                return s.SiTuoi == 0 && s.No == 0 && s.Mercatini == 0 && s.Workshop == 0 ? ImportData.VerifiedSerataWorkshop : null;
            if (s.Mercatini > 0 || s.Workshop > 0)
            {
                if (s.Si > 0 || s.No > 0 || (s.Mercatini > 0 && s.Workshop > 0)) return null;
                return s.Mercatini > 0 ? ImportData.VerifiedMercatino : ImportData.VerifiedWorkshop;
            }
            if (s.No >= MinScarti && s.Si == 0) return ImportData.VerifiedNo;
            if (s.Si >= MinConferme && s.No == 0) return ImportData.VerifiedNew;
            return null;
        }

        // stato deciso dalla storia dell'organizzatore: tutte le tue decisioni dello stesso tipo, nessuna conferma
        private static (string Stato, int Date)? OrganizzatoreDecide(Organizzatore o)
        {
            if (o.Si > 0) return null;
            var tipi = new[] { (Stato: ImportData.VerifiedNo, Date: o.No), (Stato: ImportData.VerifiedMercatino, Date: o.Mercatini), (Stato: ImportData.VerifiedWorkshop, Date: o.Workshop) }
                .Where(x => x.Date > 0).ToList();
            if (tipi.Count != 1 || tipi[0].Date < MinOrganizzatore) return null;
            return tipi[0];
        }

        /// <summary>
        /// Stato da dare a una data nuova ("nuovo", "no", "mercatino" o "workshop") con il motivo per la nota, null se la
        /// memoria non decide. Prima la serie, poi l'organizzatore
        /// </summary>
        public (string Stato, string Motivo)? Decidi(Event e)
        {
            if (Chiave(e) is string chiave && _serie.TryGetValue(chiave, out var s))
            {
                var stato = SerieDecide(s);
                if (stato == ImportData.VerifiedMercatino)
                    return (stato, $"{PrefissoNota} mercatino come le altre date ({s.Mercatini} segnate da te), va in Bacheca");
                if (stato == ImportData.VerifiedWorkshop)
                    return (stato, $"{PrefissoNota} workshop/corso come le altre date ({s.Workshop} segnate workshop o confermate nella scheda Workshop), va nella scheda Workshop");
                if (stato == ImportData.VerifiedSerataWorkshop)
                    return (stato, $"{PrefissoNota} serata e anche workshop come le altre date ({s.SerataWorkshop} segnate da te), una copia va nella scheda Workshop");
                if (stato == ImportData.VerifiedNo)
                    return (stato, $"{PrefissoNota} {s.No} date di questa serie scartate da te, nessuna confermata");
                if (stato == ImportData.VerifiedNew)
                    return (stato, $"{PrefissoNota} swing come le altre date ({s.SiTuoi} confermate da te, {s.SiTaciti} passate senza obiezioni)");
            }

            if (ChiaveOrganizzatore(e) is string chiaveOrg && _organizzatori.TryGetValue(chiaveOrg, out var o)
                && OrganizzatoreDecide(o) is { } d)
            {
                var cosa = d.Stato == ImportData.VerifiedNo ? "scartate da te"
                         : d.Stato == ImportData.VerifiedMercatino ? "segnate mercatino da te (va in Bacheca)"
                         : "segnate workshop/corso da te (va nella scheda Workshop)";
                return (d.Stato, $"{PrefissoNota} organizzatore con {d.Date} date {cosa}, nessuna confermata");
            }

            // parole imparate: non se il titolo nomina uno stile del calendario, né se la serie o l'organizzatore hanno conferme
            if (_regoleParole.Count > 0 && !string.IsNullOrWhiteSpace(e.PartyName) && !AgenteVerifica.StileDelCalendario.IsMatch(e.PartyName)
                && !(Chiave(e) is string ch && _serie.TryGetValue(ch, out var st) && (st.Si > 0 || st.SerataWorkshop > 0))
                && !(ChiaveOrganizzatore(e) is string co && _organizzatori.TryGetValue(co, out var og) && og.Si > 0))
            {
                var regola = Parole(e.PartyName).Where(_regoleParole.ContainsKey)
                    .Select(w => (Parola: w, Info: _regoleParole[w])).OrderByDescending(x => x.Info.No).FirstOrDefault();
                if (regola.Info is not null)
                    return (ImportData.VerifiedNo, $"{PrefissoNota} regola imparata: «{regola.Parola}» nel titolo, {regola.Info.No} eventi così " +
                                                   $"scartati da te ({regola.Info.Organizzatori.Count} organizzatori) e nessuno tenuto");
            }
            return null;
        }
    }
}
