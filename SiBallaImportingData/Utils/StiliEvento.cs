using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System.Text.RegularExpressions;

namespace SiBallaImportingData.Utils;

/// <summary>
/// Stili di ballo della colonna H (Stile): nomi, sinonimi e deduzione per gli eventi trovati online che non li indicano.
/// Ordine di ricerca: fonte -> dizionario (stesso evento) -> Gemini -> dizionario (organizzatore) -> parole chiave.
/// Lo stile dedotto viene scritto nello Sheet con la cella colorata e una nota (vedi ImportData.ColoreStileDedotto):
/// finché resta colorata il dizionario non lo impara.
/// </summary>
public static class StiliEvento
{
    // nomi scritti nella colonna H (gli stessi già usati nello Sheet e capiti da ParseStili e dal sito)
    public static readonly string[] Nomi =
        ["Lindy", "Balboa", "Blues", "SoloJazz", "Shag", "Charleston", "Boogie Woogie", "SlowBal", "StLouisShag", "Rhythm 'n' Blues"];

    private static readonly Dictionary<string, string> Sinonimi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lindy"] = "Lindy", ["lindy hop"] = "Lindy", ["lindyhop"] = "Lindy",
        ["balboa"] = "Balboa", ["bal"] = "Balboa",
        ["blues"] = "Blues", ["blues dance"] = "Blues",
        ["solojazz"] = "SoloJazz", ["solo jazz"] = "SoloJazz", ["solo"] = "SoloJazz", ["jazz"] = "SoloJazz",
        ["shag"] = "Shag", ["collegiate shag"] = "Shag",
        ["charleston"] = "Charleston",
        ["boogie woogie"] = "Boogie Woogie", ["boogie-woogie"] = "Boogie Woogie", ["boogiewoogie"] = "Boogie Woogie", ["boogie"] = "Boogie Woogie",
        ["slowbal"] = "SlowBal", ["slow bal"] = "SlowBal", ["slow balboa"] = "SlowBal",
        ["stlouisshag"] = "StLouisShag", ["st louis shag"] = "StLouisShag", ["st. louis shag"] = "StLouisShag",
        // Rhythm 'n' Blues (24/11/2026): ballo R&B della scena swing, diverso dal Blues dance
        ["rhythm 'n' blues"] = "Rhythm 'n' Blues", ["rhythm ’n’ blues"] = "Rhythm 'n' Blues", ["rhythm n blues"] = "Rhythm 'n' Blues",
        ["rhythm and blues"] = "Rhythm 'n' Blues", ["rhythm & blues"] = "Rhythm 'n' Blues", ["rhythm&blues"] = "Rhythm 'n' Blues",
        ["r&b"] = "Rhythm 'n' Blues", ["rnb"] = "Rhythm 'n' Blues", ["r'n'b"] = "Rhythm 'n' Blues", ["r’n’b"] = "Rhythm 'n' Blues",
    };

    /// <summary>"Rhythm 'n' Blues", "Rhythm & Blues", "Rhythm and Blues", "R&B", "R'n'B", "RnB" (apostrofi diritti o tipografici)</summary>
    public const string RhythmNBlues = @"rhythm\s*['’]?\s*(n|and|&)\s*['’]?\s*blues|\br\s*['’]?\s*(n|&)\s*['’]?\s*b\b|\brnb\b";

    // parole nel titolo o nell'organizzatore -> stile (ultima riserva: grossolana, per questo la cella resta da confermare)
    private static readonly (Regex Parola, string Stile)[] ParoleChiave =
    [
        (new(@"\blindy\b", RegexOptions.IgnoreCase), "Lindy"),
        (new(@"\bbalboa\b", RegexOptions.IgnoreCase), "Balboa"),
        (new(RhythmNBlues, RegexOptions.IgnoreCase), "Rhythm 'n' Blues"),
        // "Blues" da solo: non quello di "Rhythm 'n' Blues" / "Rhythm & Blues"
        (new(@"(?<!rhythm\s*['’]?\s*(n|and|&)\s*['’]?\s*)\bblues\b", RegexOptions.IgnoreCase), "Blues"),
        (new(@"\bsolo\s*jazz\b", RegexOptions.IgnoreCase), "SoloJazz"),
        (new(@"\bshag\b", RegexOptions.IgnoreCase), "Shag"),
        (new(@"\bcharleston\b", RegexOptions.IgnoreCase), "Charleston"),
        // niente più "rock", "jive", "fifties": il Rock 'n' Roll non è uno stile del calendario (curatore, 24/11/2026), e una serata
        // rock'n'roll non deve ritrovarsi un "Boogie Woogie" dedotto
        (new(@"boogie", RegexOptions.IgnoreCase), "Boogie Woogie"),
    ];

    /// <summary>"Lindy Hop, jazz; Boogie" -> ["Lindy", "SoloJazz", "Boogie Woogie"] (i nomi sconosciuti restano come sono)</summary>
    public static List<string> Normalizza(string? testo)
    {
        var out_ = new List<string>();
        if (string.IsNullOrWhiteSpace(testo)) return out_;
        foreach (var parte in testo.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var nome = Sinonimi.TryGetValue(parte, out var n) ? n : parte;
            if (!out_.Contains(nome, StringComparer.OrdinalIgnoreCase)) out_.Add(nome);
        }
        return out_;
    }

    public static string Testo(IEnumerable<string> stili) => string.Join(", ", stili);

    public static List<string> DaParoleChiave(Event e)
    {
        var testo = $"{e.PartyName} {e.Organizer}";
        return ParoleChiave.Where(p => p.Parola.IsMatch(testo)).Select(p => p.Stile).Distinct().ToList();
    }

    /// <summary>
    /// Eventi nuovi senza stile dalla fonte: cerca lo stile e lo mette in StylesToString (colonna H), segnando da dove viene
    /// in StileDedottoDa. Va chiamato dopo SwingEventClassifier.SegnaPerSheetAsync (che chiede gli stili anche a Gemini)
    /// </summary>
    public static void Completa(IEnumerable<Event> eventi, KnownVenueStore store)
    {
        var conteggio = new Dictionary<string, int>();
        int senza = 0;
        foreach (var e in eventi)
        {
            if (e.EventDetail is null || !string.IsNullOrWhiteSpace(e.EventDetail.StylesToString)) continue;

            (List<string>? Stili, string Fonte)[] candidati =
            [
                (store.StiliEvento(e), "dizionario, stesso evento"),
                (SwingEventClassifier.StiliDaGemini(e), "Gemini"),
                (store.StiliOrganizzatore(e), "dizionario, organizzatore"),
                (DaParoleChiave(e), "parole chiave"),
            ];
            var trovato = candidati.FirstOrDefault(c => c.Stili is { Count: > 0 });
            if (trovato.Stili is null)
            {
                senza++;
                continue;
            }

            e.EventDetail.StylesToString = Testo(trovato.Stili);
            e.StileDedottoDa = trovato.Fonte;
            conteggio[trovato.Fonte] = conteggio.GetValueOrDefault(trovato.Fonte) + 1;
        }

        if (conteggio.Count > 0 || senza > 0)
            Console.WriteLine($"[stili] eventi nuovi senza stile dalla fonte: dedotto per {conteggio.Values.Sum()} " +
                              $"({string.Join(", ", conteggio.Select(c => $"{c.Key}: {c.Value}"))}), non trovato per {senza}");
    }
}
