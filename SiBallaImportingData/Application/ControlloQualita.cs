using SiBalla.Service;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;
using System.Text.RegularExpressions;

namespace SiBallaImportingData.Application;

/// <summary>
/// Controllo qualità dello Sheet (modo "qualita" dell'agente, di notte): solo controlli nel codice, senza Gemini.
/// Cerca doppioni, date sospette, serie con decisioni contraddittorie, dati mancanti e orari strani sulle righe pubblicate.
/// Solo proposte: non scrive niente nello Sheet. Ogni riga del risultato inizia con un simbolo per l'elenco dell'email
/// </summary>
internal static class ControlloQualita
{
    private const int MaxPerSezione = 25;

    // pubblicata = va su sito/app/email: niente da verificare, scartate, storico, mercatini (Bacheca) e tracce "workshop" in Serate
    private static bool Pubblicata(Event e) =>
        e.LuogoVerificato is not (ImportData.VerifiedPending or ImportData.VerifiedNo or ImportData.VerifiedStorico or ImportData.VerifiedMercatino)
        && !(e.LuogoVerificato == ImportData.VerifiedWorkshop && e.SheetName == TipoEvento.Serate.ToString());

    private static string Riga(Event e) =>
        $"{e.SheetName} riga {e.SheetRow} | {e.DateStart:yyyy-MM-dd}{(e.DateStart.TimeOfDay == TimeSpan.Zero ? "" : e.DateStart.ToString(" HH:mm"))} | " +
        $"{e.City?.Trim()} | {e.Organizer?.Trim()} | {e.PartyName?.Trim()} [{(string.IsNullOrWhiteSpace(e.LuogoVerificato) ? "confermata" : e.LuogoVerificato)}]";

    public static void Esegui(List<Event> sheetEvents)
    {
        var oggi = DateTime.Today;
        var future = sheetEvents.Where(e => (e.DateFinish ?? e.DateStart).Date >= oggi && !string.IsNullOrWhiteSpace(e.PartyName)).ToList();
        var pubblicate = future.Where(Pubblicata).ToList();
        Console.WriteLine();
        Console.WriteLine($"===== Controllo qualità dello Sheet: {future.Count} righe future, {pubblicate.Count} pubblicate =====");

        int doppioni = Doppioni(pubblicate);
        int date = DateSospette(future, sheetEvents);
        int serie = SerieContraddittorie(sheetEvents);
        int mancanti = DatiMancanti(pubblicate);
        int orari = OrariStrani(pubblicate);

        Console.WriteLine();
        Console.WriteLine($"===== Risultato controllo qualità: {doppioni} probabili doppioni, {date} date sospette, {serie} serie contraddittorie, " +
                          $"{mancanti} righe pubblicate con dati mancanti, {orari} orari strani =====");
        Console.WriteLine("Solo proposte: niente scritto nello Sheet.");
    }

    // 1. stesso giorno, stessa città, titolo molto simile, entrambe pubblicate (le copie Serate -> Workshop non contano).
    //    Solo nella stessa scheda: Serate + Workshop è voluto (festival e weekend con serate stanno su entrambi i fogli)
    private static int Doppioni(List<Event> pubblicate)
    {
        var coppie = new List<(Event A, Event B, double Sim)>();
        foreach (var g in pubblicate.GroupBy(e => (e.DateStart.Date, Citta: KnownVenueStore.NormalizeTitle(e.City))).Where(g => g.Count() > 1))
        {
            var lista = g.ToList();
            for (int i = 0; i < lista.Count; i++)
                for (int j = i + 1; j < lista.Count; j++)
                {
                    var (a, b) = (lista[i], lista[j]);
                    // stesso evento su entrambi i fogli (festival con serate, serata+workshop, workshop spostato): è voluto
                    if (a.SheetName != b.SheetName) continue;
                    if (Simili(a.PartyName, b.PartyName) is double sim) coppie.Add((a, b, sim));
                }
        }
        var stessaScheda = coppie.Where(x => x.A.SheetName == x.B.SheetName).ToList();

        Console.WriteLine("\n----- 🔁 Probabili doppioni nella stessa scheda (stesso giorno e città, titolo simile, entrambi pubblicati) -----");
        // le righe ripetute più volte (es. 6 copie dello stesso workshop) in un'unica voce
        var gruppi = new List<List<Event>>();
        foreach (var (a, b, _) in stessaScheda.OrderByDescending(x => x.Sim))
        {
            var esistente = gruppi.FirstOrDefault(gr => gr.Contains(a) || gr.Contains(b));
            if (esistente is null) gruppi.Add(new List<Event> { a, b });
            else { if (!esistente.Contains(a)) esistente.Add(a); if (!esistente.Contains(b)) esistente.Add(b); }
        }
        foreach (var gr in gruppi.Take(MaxPerSezione))
        {
            Console.WriteLine($"🔁 {gr.Count} righe per lo stesso evento ({gr[0].SheetName}): tienine una, le altre 'no' con nota 'doppione'");
            foreach (var e in gr.OrderBy(x => x.SheetRow)) Console.WriteLine($"   • {Riga(e)}");
        }
        if (gruppi.Count == 0) Console.WriteLine("   nessuno");
        return gruppi.Count;
    }

    // parole del titolo: le lettere "decorate" (𝗚𝗢𝗧𝗛𝗘𝗡𝗕𝗨𝗥𝗚, 𝘼𝙡𝙡) diventano normali (FormKC), apostrofi tipografici uniformati
    private static HashSet<string> Parole(string? s) =>
        AddressResolver.ParoleSignificative((s ?? "").Normalize(System.Text.NormalizationForm.FormKC).Replace('’', '\'').Replace('`', '\''))
            .Where(w => !w.All(char.IsDigit) && w is not ("night" or "party" or "festival" or "weekend" or "workshop" or "social"))
            .ToHashSet();

    // simili se condividono almeno il 60% delle parole, oppure se le parole di uno (almeno 2) sono tutte nell'altro
    private static double? Simili(string? a, string? b)
    {
        var pa = Parole(a); var pb = Parole(b);
        if (pa.Count == 0 || pb.Count == 0) return null;
        double jaccard = (double)pa.Intersect(pb).Count() / pa.Union(pb).Count();
        var (piccolo, grande) = pa.Count <= pb.Count ? (pa, pb) : (pb, pa);
        bool contenuto = piccolo.Count >= 2 && piccolo.IsSubsetOf(grande);
        return jaccard >= 0.6 || contenuto ? jaccard : null;
    }

    private static readonly string[] Mesi = { "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre" };
    private static readonly string[] Months = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
    private static readonly Regex DataNelTitolo = new(@"(?<!\d)(?<g>\d{1,2})\s*[./]\s*(?<m>\d{1,2})(?:\s*[./]\s*(?<a>\d{2,4}))?(?!\d)");
    private static readonly Regex GiornoMese = new(@"(?<!\d)(?<g>\d{1,2})\s+(?<mese>[a-zà]+)", RegexOptions.IgnoreCase);
    private static readonly Regex Anno = new(@"(?<!\d)(?<a>20\d{2})(?!\d)");

    // 2. data nel titolo diversa dalla data della riga, anno nel titolo diverso, anni lontani, stessa serie e stesso giorno/mese in anni diversi
    private static int DateSospette(List<Event> future, List<Event> tutte)
    {
        Console.WriteLine("\n----- 📅 Date sospette -----");
        var trovate = new List<string>();
        foreach (var e in future)
        {
            var titolo = e.PartyName ?? "";
            var inizio = e.DateStart.Date; var fine = (e.DateFinish ?? e.DateStart).Date;
            bool Dentro(int g, int m) => Enumerable.Range(0, Math.Max(1, (fine - inizio).Days + 1)).Select(d => inizio.AddDays(d)).Any(d => d.Day == g && d.Month == m);
            // "15 - 17 January", "19-20-21 Febbraio": intervallo che parte dal giorno della riga (manca solo la data di fine)
            static bool Intervallo(string t, int giornoInizio, int posizione) =>
                Regex.IsMatch(t[Math.Max(0, posizione - 14)..posizione], $@"(?<!\d){giornoInizio}\s*[-–/,&]\s*(\d{{1,2}}\s*[-–/,&]\s*)*$");
            // "EARLY BIRDS -> 31/10", "iscrizioni entro il 15/11": scadenze, non la data dell'evento
            static bool Scadenza(string t, int posizione) =>
                Regex.IsMatch(t[Math.Max(0, posizione - 25)..posizione], @"early|deadline|entro|until|fino al|iscrizion|registr|->", RegexOptions.IgnoreCase);

            foreach (Match x in DataNelTitolo.Matches(titolo))
            {
                int g = int.Parse(x.Groups["g"].Value), m = int.Parse(x.Groups["m"].Value);
                if (g is < 1 or > 31 || m is < 1 or > 12) continue;
                if (!Dentro(g, m) && !Dentro(m, g) && !Scadenza(titolo, x.Index)) { trovate.Add($"📅 nel titolo c'è {g:00}/{m:00} ma la riga è il {inizio:dd/MM}: {Riga(e)}"); break; }
            }
            foreach (Match x in GiornoMese.Matches(titolo))
            {
                var nome = x.Groups["mese"].Value.ToLowerInvariant();
                int m = Array.IndexOf(Mesi, nome) + 1; if (m == 0) m = Array.IndexOf(Months, nome) + 1;
                int g = int.Parse(x.Groups["g"].Value);
                if (m == 0 || g is < 1 or > 31) continue;
                if (!Dentro(g, m) && !Intervallo(titolo, inizio.Day, x.Index) && !Scadenza(titolo, x.Index)) { trovate.Add($"📅 nel titolo c'è {g} {nome} ma la riga è il {inizio:dd/MM}: {Riga(e)}"); break; }
            }
            foreach (Match x in Anno.Matches(titolo))
            {
                int a = int.Parse(x.Groups["a"].Value);
                // "Stagione 2026/2027" e simili: va bene se l'anno della riga è tra quelli citati
                var anni = Anno.Matches(titolo).Select(y => int.Parse(y.Groups["a"].Value)).ToList();
                if (!anni.Contains(inizio.Year) && !anni.Contains(fine.Year)) { trovate.Add($"📅 nel titolo c'è il {a} ma la riga è del {inizio.Year}: {Riga(e)}"); }
                break;
            }
            if (inizio.Year > DateTime.Today.Year + 1)
                trovate.Add($"📅 data molto lontana ({inizio.Year}): {Riga(e)}");
        }

        // stessa serie, stesso giorno e mese, anno diverso: spesso l'anno sbagliato (2027 al posto di 2026)
        foreach (var g in tutte.Where(e => MemoriaSerie.Chiave(e) is not null && e.LuogoVerificato != ImportData.VerifiedNo)
                               .GroupBy(e => (MemoriaSerie.Chiave(e), e.DateStart.Month, e.DateStart.Day)))
        {
            var anni = g.Select(e => e.DateStart.Year).Distinct().ToList();
            if (anni.Count > 1 && g.Any(e => e.DateStart.Date >= DateTime.Today))
                trovate.Add($"📅 stessa serie, stesso giorno e mese in anni diversi ({string.Join(", ", anni.OrderBy(a => a))}): " +
                            string.Join("  ⟷  ", g.OrderBy(e => e.DateStart).Select(Riga)));
        }

        foreach (var t in trovate.Take(MaxPerSezione)) Console.WriteLine(t);
        if (trovate.Count == 0) Console.WriteLine("   nessuna");
        else if (trovate.Count > MaxPerSezione) Console.WriteLine($"   … e altre {trovate.Count - MaxPerSezione}");
        return trovate.Count;
    }

    // 3. serie con decisioni tue di tipo diverso (es. alcune date confermate, altre "no" o "workshop")
    private static int SerieContraddittorie(List<Event> tutte)
    {
        Console.WriteLine("\n----- ⚖️ Serie con decisioni diverse (le date future della serie le decide la memoria solo se sono coerenti) -----");
        var serie = tutte
            .Select(e => (Evento: e, Chiave: MemoriaSerie.Chiave(e), Tua: ImportCommands.DecisioneTua(e)))
            .Where(x => x.Chiave is not null && x.Tua is not null)
            .GroupBy(x => x.Chiave!)
            .Where(g => g.Select(x => x.Tua).Distinct().Count() > 1)
            // solo le serie che hanno ancora date future
            .Where(g => tutte.Any(e => e.DateStart.Date >= DateTime.Today && MemoriaSerie.Chiave(e) == g.Key))
            .ToList();
        foreach (var g in serie.Take(MaxPerSezione))
        {
            var primo = g.First().Evento;
            var conteggi = string.Join(", ", g.GroupBy(x => x.Tua).Select(x => $"{x.Count()} {x.Key}"));
            Console.WriteLine($"⚖️ {primo.PartyName?.Trim()} | {primo.Organizer?.Trim()} | {primo.City?.Trim()}: {conteggi}");
            foreach (var x in g.OrderBy(x => x.Evento.DateStart).Take(6))
                Console.WriteLine($"   • {x.Tua,-16} {Riga(x.Evento)}");
        }
        if (serie.Count == 0) Console.WriteLine("   nessuna");
        else if (serie.Count > MaxPerSezione) Console.WriteLine($"   … e altre {serie.Count - MaxPerSezione}");
        return serie.Count;
    }

    // 4. righe pubblicate senza stile, senza organizzatore o senza coordinate
    private static int DatiMancanti(List<Event> pubblicate)
    {
        Console.WriteLine("\n----- 🕳️ Righe pubblicate con dati mancanti -----");
        var mancanti = pubblicate
            .Select(e => (Evento: e, Cosa: string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(e.SheetStiliRaw) ? "stile" : null,
                string.IsNullOrWhiteSpace(e.Organizer) ? "organizzatore" : null,
                e.EventDetail?.Latitude is null or 0 || e.EventDetail?.Longitude is null or 0 ? "coordinate" : null,
                string.IsNullOrWhiteSpace(e.City) ? "città" : null,
            }.Where(s => s is not null))))
            .Where(x => x.Cosa.Length > 0)
            .OrderBy(x => x.Evento.DateStart)
            .ToList();
        foreach (var g in mancanti.GroupBy(x => x.Cosa).OrderByDescending(g => g.Count()))
            Console.WriteLine($"   senza {g.Key}: {g.Count()} righe");
        // nell'elenco solo i prossimi 14 giorni: sono quelli che si vedono adesso su sito, app ed email (il resto è nei conteggi)
        var vicine = mancanti.Where(x => x.Evento.DateStart.Date <= DateTime.Today.AddDays(14)).ToList();
        Console.WriteLine($"   nei prossimi 14 giorni: {vicine.Count}");
        foreach (var x in vicine.Take(MaxPerSezione))
            Console.WriteLine($"🕳️ senza {x.Cosa}: {Riga(x.Evento)}");
        if (vicine.Count > MaxPerSezione) Console.WriteLine($"   … e altre {vicine.Count - MaxPerSezione}");
        return mancanti.Count;
    }

    // 5. orari improbabili per una serata o un workshop (tra le 3 e le 9 del mattino)
    private static int OrariStrani(List<Event> pubblicate)
    {
        Console.WriteLine("\n----- 🕒 Orari strani (inizio tra le 3 e le 9 del mattino) -----");
        var strani = pubblicate.Where(e => e.DateStart.TimeOfDay != TimeSpan.Zero && e.DateStart.Hour is >= 3 and < 9).OrderBy(e => e.DateStart).ToList();
        foreach (var e in strani.Take(MaxPerSezione)) Console.WriteLine($"🕒 inizio alle {e.DateStart:HH:mm}: {Riga(e)}");
        if (strani.Count == 0) Console.WriteLine("   nessuno");
        return strani.Count;
    }
}
