using SiBallaImportingData.Model;
using SiBallaImportingData.Service;

public class AddressResolver
{
    private readonly KnownVenueStore _store;
    private readonly PhotonVenueGeocoder _geo = new();
    private readonly HashSet<string> _failed = new();   // query già fallite in questa esecuzione

    public AddressResolver(KnownVenueStore store) => _store = store;

    // ---------- Scorciatoie per le due classi ----------

    public async Task<bool> ApplyAsync(ESwingIt e, CancellationToken ct = default)
    {
        var r = await ResolveAsync(e.Organizer, e.City, e.Country, e.Title, e.Location, ct);
        if (r is null) return false;
        e.Latitude = (float)r.Value.Lat;
        e.Longitude = (float)r.Value.Lng;
        return true;
    }

    public async Task<bool> ApplyAsync(Event e, CancellationToken ct = default)
    {
        if (e.EventDetail is null) return false;
        var r = await ResolveAsync(e.Organizer, e.City, e.Country, e.PartyName, e.EventDetail.Location, ct);
        if (r is null) return false;
        e.EventDetail.Latitude = r.Value.Lat;
        e.EventDetail.Longitude = r.Value.Lng;
        return true;
    }

    // ---------- Logica comune ----------

    public async Task<(double Lat, double Lng)?> ResolveAsync(
        string? organizer, string? city, string? country, string? title, string? rawAddress,
        CancellationToken ct = default)
    {
        // 1. luogo verificato nel JSON (organizzatore + titolo)
        var known = _store.Find(organizer, city, title);
        if (known?.Lat != null && known.Lng != null)
        {
            Console.WriteLine($"[resolver] JSON verificato: {title}");
            return (known.Lat.Value, known.Lng.Value);
        }

        // 2. indirizzo scritto nell'evento
        var address = rawAddress?.Replace("·", ",").Trim();
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(city))
        {
            Console.WriteLine($"[resolver] SALTATO (indirizzo o città vuoti): '{title}' | address='{address}' | city='{city}'");
            return null;
        }

        if (string.IsNullOrWhiteSpace(organizer))
            Console.WriteLine($"[resolver] ATTENZIONE organizzatore vuoto: '{title}' -> non verrà salvato nel JSON");
        // 2a. già geocodificato in passato (JSON)
        var cached = _store.FindByAddress(organizer, city, address);
        if (cached?.Lat != null && cached.Lng != null)
        {
            Console.WriteLine($"[resolver] già nel JSON: {address}");
            return (cached.Lat.Value, cached.Lng.Value);
        }

        // 2b. già fallito in questa esecuzione: non richiamo Photon
        var query = $"{address}, {city}, {country}";
        if (_failed.Contains(query)) { Console.WriteLine($"[resolver] già fallita: {query}"); return null; };

        // 2c. Photon con controllo di distanza dalla città
        var center = OfflineGeocoder.GetCoordinates(city, country);
        var maxKm = center == (0.0, 0.0) ? double.MaxValue : 10;
        var hit = await _geo.FindAsync(query, center, maxKm, ct);
        await Task.Delay(1100, ct);                                  // Photon pubblico: una richiesta al secondo

        if (hit is null)
        {
            Console.WriteLine($"[resolver] Photon: nessun risultato entro {maxKm} km per: {query}");
            _failed.Add(query);
            return null;
        }

        Console.WriteLine($"[resolver] Photon OK: {query} -> {hit.Value.Lat}, {hit.Value.Lng}");
        _store.RegisterAddress(organizer, city, country, title, address, hit.Value.Lat, hit.Value.Lng, "photon-address");
        return (hit.Value.Lat, hit.Value.Lng);
    }

    /// <summary>Da chiamare una volta all'avvio, dopo ImportFromSheet.</summary>
    public async Task GeocodePendingAsync(CancellationToken ct = default)
    {
        var pending = _store.PendingGeocode();
        Console.WriteLine($"[sheet] indirizzi da geocodificare: {pending.Count}");

        foreach (var (org, v) in pending)
        {
            var city = v.City ?? org.Default.City;
            var country = v.Country ?? org.Default.Country;
            var address = v.Address!.Replace("·", ",").Trim();

            var hit = await GeocodeAsync(address, city, country, ct);
            if (hit is null) { Console.WriteLine($"[sheet] ⚠️ Photon non trova l'indirizzo, restano le coordinate dello sheet: {address}, {city}"); continue; }

            // coordinate dello sheet vicine a quelle di Photon: probabilmente prese a mano, più precise
            if (v.Lat is double la && v.Lng is double lo && DistanceKm(la, lo, hit.Value.Lat, hit.Value.Lng) < 0.3)
            {
                Console.WriteLine($"[sheet] Photon conferma le coordinate dello sheet: {address}, {city}");
                _store.SetCoords(v, la, lo, "sheet");
                continue;
            }

            Console.WriteLine($"[sheet] Photon OK: {address}, {city} -> {hit.Value.Lat}, {hit.Value.Lng}");
            _store.SetCoords(v, hit.Value.Lat, hit.Value.Lng, "sheet+photon");
        }
    }

    /// <summary>
    /// Default con le coordinate iniziali: cerco la via di quelle coordinate (le coordinate non cambiano).
    /// Se sono il centro della città (coordinate di ripiego, non del locale) la via non avrebbe senso: la salto.
    /// </summary>
    public async Task ReverseGeocodeDefaultsAsync(CancellationToken ct = default)
    {
        var pending = _store.PendingReverse();
        Console.WriteLine($"[default] coordinate iniziali di cui cercare la via: {pending.Count}");

        int found = 0, center = 0;
        foreach (var v in pending)
        {
            var c = OfflineGeocoder.GetCoordinates(v.City, v.Country);
            if (c != (0.0, 0.0) && DistanceKm(v.Lat!.Value, v.Lng!.Value, c.Item1, c.Item2) < 0.2)
            {
                _store.SetReverse(v, null, "iniziale (centro città)");
                center++;
                continue;
            }

            var street = await _geo.ReverseAsync(v.Lat!.Value, v.Lng!.Value, ct);
            await Task.Delay(1100, ct);                              // Photon pubblico: una richiesta al secondo
            _store.SetReverse(v, street, street is null ? "iniziale (via non trovata)" : "iniziale+photon-reverse");
            if (street is not null) found++;
        }

        if (pending.Count > 0)
            Console.WriteLine($"[default] via trovata: {found}, centro città (saltate): {center}, non trovata: {pending.Count - found - center}");
    }

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double x) => x * Math.PI / 180;
        var h = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lng2 - lng1) / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(h));
    }

    // parole troppo generiche per dire che due nomi di locale sono lo stesso posto
    private static readonly HashSet<string> ParoleGeneriche = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "del", "della", "dei", "di", "da", "la", "il", "lo", "le", "al", "alla", "des", "les", "der", "die", "das",
        "club", "bar", "pub", "cafe", "caffe", "sala", "teatro", "circolo", "spazio", "centro", "ristorante", "hotel",
        "jazz", "swing", "dance", "ballo", "scuola", "studio", "live", "music", "musica", "arci", "asd", "aps"
    };

    public static List<string> ParoleSignificative(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new();
        var senzaAccenti = new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Split(senzaAccenti, @"[^a-z0-9]+")
            .Where(w => w.Length >= 3 && !ParoleGeneriche.Contains(w))
            .ToList();
    }

    /// <summary>
    /// Cerca un locale per nome con Photon (entro 10 km dalla città). Vale solo se il nome trovato ha almeno una parola
    /// significativa in comune con quello cercato, così non si prende per buono il centro città o un posto qualsiasi.
    /// Restituisce coordinate, nome trovato e via (geocoding inverso, null se non c'è)
    /// </summary>
    public async Task<(double Lat, double Lng, string? Nome, string? Via)?> CercaLocaleAsync(string nome, string? city, string? country, CancellationToken ct = default)
    {
        var cercate = ParoleSignificative(nome);
        if (cercate.Count == 0) return null;

        var center = OfflineGeocoder.GetCoordinates(city, country);
        var maxKm = center == (0.0, 0.0) ? double.MaxValue : 10;
        var hit = await _geo.FindAsync($"{nome}, {city}, {country}", center, maxKm, ct);
        await Task.Delay(1100, ct);                                  // Photon pubblico: una richiesta al secondo
        if (hit is null || !ParoleSignificative(hit.Value.Name).Intersect(cercate).Any()) return null;

        var via = await _geo.ReverseAsync(hit.Value.Lat, hit.Value.Lng, ct);
        await Task.Delay(1100, ct);
        // il reverse a volte restituisce di nuovo il nome del locale: non è una via
        if (via is not null && ParoleSignificative(via).Intersect(cercate).Any()) via = null;

        return (hit.Value.Lat, hit.Value.Lng, hit.Value.Name, via);
    }

    /// <summary>Photon con controllo di distanza (10 km dal centro città). Non salva nulla nel JSON.</summary>
    public async Task<(double Lat, double Lng)?> GeocodeAsync(string address, string? city, string? country, CancellationToken ct = default)
    {
        var center = OfflineGeocoder.GetCoordinates(city, country);
        var maxKm = center == (0.0, 0.0) ? double.MaxValue : 10;

        var hit = await _geo.FindAsync($"{address}, {city}, {country}", center, maxKm, ct);
        await Task.Delay(1100, ct);                                  // Photon pubblico: una richiesta al secondo

        return hit is null ? null : (hit.Value.Lat, hit.Value.Lng);
    }
}