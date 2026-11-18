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
        if (!SamePlace(a, b)) return false;
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

    // Stessa città, anche se nello Sheet è stata riscritta: "Montreal" = "Montreal (administrative region)",
    // "Ipswich" = "Ipswich, Massachusetts", "Sherbrooke" = "Estrie" con le stesse coordinate.
    // Prima del 18/11/2026 bastava che la città cambiasse per far riaggiungere la riga a ogni giro.
    private static bool SamePlace(Event a, Event b)
    {
        var first = Normalize(a.City ?? a.EventDetail?.City);
        var second = Normalize(b.City ?? b.EventDetail?.City);
        if (first.Length > 0 && second.Length > 0
            && (first == second || first.StartsWith(second + " ") || second.StartsWith(first + " ")))
            return true;
        return DistanceKm(a.EventDetail, b.EventDetail) is <= 5;
    }

    private static double? DistanceKm(EventDetail? a, EventDetail? b)
    {
        if (a?.Latitude is not double lat1 || a.Longitude is not double lng1
            || b?.Latitude is not double lat2 || b.Longitude is not double lng2) return null;
        // 0;0 = coordinate mancanti, non un luogo
        if ((lat1 == 0 && lng1 == 0) || (lat2 == 0 && lng2 == 0)) return null;
        double Rad(double x) => x * Math.PI / 180;
        var h = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2)
            + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lng2 - lng1) / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(h));
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
