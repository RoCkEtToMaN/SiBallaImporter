using System.Text.RegularExpressions;
using SiBallaImportingData.Model;

namespace SiBallaImportingData.Utils;

public static class EventIdentity
{
    private static string Normalize(string? value) =>
        Regex.Replace(System.Net.WebUtility.HtmlDecode(value ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static HashSet<string> TitleWords(string? value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.All(char.IsDigit) && !Months.Contains(w))
            .ToHashSet();

    private static readonly HashSet<string> Months = new()
    {
        "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno",
        "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"
    };

    public static bool Same(Event a, Event b)
    {
        if (a.DateStart.Date != b.DateStart.Date) return false;
        var city = Normalize(a.City ?? a.EventDetail?.City);
        if (city.Length == 0 || city != Normalize(b.City ?? b.EventDetail?.City)) return false;
        var title = Normalize(a.PartyName);
        if (title.Length == 0) return false;

        // Due orari espliciti diversi possono indicare due appuntamenti distinti.
        if (a.DateStart.TimeOfDay != TimeSpan.Zero && b.DateStart.TimeOfDay != TimeSpan.Zero
            && a.DateStart.TimeOfDay != b.DateStart.TimeOfDay) return false;
        // Titoli identici: la durata mancante non cambia l'identit?.
        if (title == Normalize(b.PartyName)) return true;

        // Varianti del titolo richiedono anche lo stesso organizzatore.
        var organizer = Normalize(a.Organizer);
        if (organizer.Length == 0 || organizer != Normalize(b.Organizer)) return false;
        var first = TitleWords(a.PartyName);
        var second = TitleWords(b.PartyName);
        // Almeno tre parole e un titolo contenuto nell'altro, tolte le date.
        return Math.Min(first.Count, second.Count) >= 3
            && (first.IsSubsetOf(second) || second.IsSubsetOf(first));
    }

    public static List<Event> CompleteEmailTiming(IEnumerable<Event> curated, IEnumerable<Event> online)
    {
        var sources = online.ToList();
        return curated.Select(original =>
        {
            var copy = original.Copy();
            foreach (var source in sources.Where(e => Same(original, e)))
            {
                if (copy.Duration <= TimeSpan.Zero && source.Duration > TimeSpan.Zero)
                    copy.Duration = source.Duration;
                if (copy.DateStart.TimeOfDay == TimeSpan.Zero && source.DateStart.TimeOfDay != TimeSpan.Zero)
                    copy.DateStart = source.DateStart;
                if ((!copy.DateFinish.HasValue || copy.DateFinish <= copy.DateStart)
                    && source.DateFinish > copy.DateStart)
                    copy.DateFinish = source.DateFinish;
            }
            return copy;
        }).ToList();
    }

    public static List<Event> Unique(IEnumerable<Event> events)
    {
        var result = new List<Event>();
        foreach (var ev in events)
        {
            var index = result.FindIndex(other => Same(other, ev));
            if (index < 0) result.Add(ev);
            else if (Completeness(ev) > Completeness(result[index])) result[index] = ev;
        }
        return result;
    }

    private static int Completeness(Event ev) =>
        (ev.Duration > TimeSpan.Zero ? 2 : 0) + (ev.DateStart.TimeOfDay != TimeSpan.Zero ? 1 : 0);
}
