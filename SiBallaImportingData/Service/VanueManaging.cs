using SiBallaImportingData.Model;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public class VenueInfo
{
    public string? Organizer { get; set; }   // solo nel "default"
    public string? City { get; set; }
    public string? Country { get; set; }
    public int? EventCount { get; set; }
    public string? VenueName { get; set; }
    public string? Address { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public string? GeocodedAddress { get; set; } // indirizzo a cui corrispondono Lat/Lng (null = coordinate non da Photon)
    public bool Verified { get; set; }           // true solo quando lo controlli tu a mano
    public string? Source { get; set; }      // "manuale", "photon", "ai", ...
    public string? VerifiedOn { get; set; }
    public string? Notes { get; set; }
}

public class OrganizerVenues
{
    public VenueInfo Default { get; set; } = new();
    public Dictionary<string, VenueInfo> Events { get; set; } = new();
}

public class KnownVenueStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // niente \u00E0 nel file
    };

    private readonly string _path;
    private readonly Dictionary<string, OrganizerVenues> _data;
    private bool _dirty;

    private KnownVenueStore(string path, Dictionary<string, OrganizerVenues> data)
    {
        _path = path;
        _data = data;
    }

    // ---------- Caricamento / salvataggio ----------

    public static KnownVenueStore Load(string path)
    {
        if (!File.Exists(path))
            return new KnownVenueStore(path, new());

        var json = File.ReadAllText(path, Encoding.UTF8);
        var data = JsonSerializer.Deserialize<Dictionary<string, OrganizerVenues>>(json, JsonOpts) ?? new();
        return new KnownVenueStore(path, data);
    }

    public void Save()
    {
        if (!_dirty) { Console.WriteLine("[store] Save: nulla da scrivere"); return; }

        // scrittura atomica: se il programma si interrompe, non rovini il file originale
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOpts), Encoding.UTF8);
        File.Move(tmp, _path, overwrite: true);
        _dirty = false;
        Console.WriteLine($"[store] Salvato in {_path}");
    }

    public int AddPlaceholders(IEnumerable<Event> events)
    {
        int added = 0;

        var groups = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
            .GroupBy(e => MakeKey(e.Organizer!, e.City!));

        foreach (var g in groups)
        {
            var first = g.First();
            var site = g.Select(e => e.EventDetail?.WebSite).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            var note = string.IsNullOrWhiteSpace(site) ? "da compilare" : $"da compilare - sito: {site}";

            if (!_data.TryGetValue(g.Key, out var org))
            {
                org = new OrganizerVenues
                {
                    Default = new VenueInfo
                    {
                        Organizer = first.Organizer,
                        City = first.City,
                        Country = first.Country,
                        EventCount = g.Count(),
                        Notes = note
                    }
                };
                _data[g.Key] = org;
                added++; _dirty = true;
            }

            if (org.Default.Verified) continue;           // luogo già confermato

            // più titoli diversi: una voce da compilare per ciascuno
            var byTitle = g.GroupBy(e => NormalizeTitle(e.PartyName)).Where(x => x.Key.Length > 0).ToList();
            if (byTitle.Count < 2) continue;

            foreach (var t in byTitle)
            {
                if (org.Events.ContainsKey(t.Key)) continue;
                var tSite = t.Select(e => e.EventDetail?.WebSite).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                org.Events[t.Key] = new VenueInfo
                {
                    EventCount = t.Count(),
                    Notes = string.IsNullOrWhiteSpace(tSite) ? "da compilare" : $"da compilare - sito: {tSite}"
                };
                added++; _dirty = true;
            }
        }
        return added;
    }

    // ---------- Ricerca ----------

    /// <summary>Restituisce il luogo verificato per questo evento, o null.</summary>
    public VenueInfo? Find(string? organizer, string? city, string? title)
    {
        if (string.IsNullOrWhiteSpace(organizer) || string.IsNullOrWhiteSpace(city)) return null;
        if (!_data.TryGetValue(MakeKey(organizer, city), out var org)) return null;

        var t = NormalizeTitle(title);
        if (t.Length > 0 && org.Events.TryGetValue(t, out var ev) && ev.Verified && HasCoords(ev))
            return ev;

        return org.Default.Verified && HasCoords(org.Default) ? org.Default : null;
    }

    public VenueInfo? Find(ESwingIt e) => Find(e.Organizer, e.City, e.Title);
    public VenueInfo? Find(Event e) => Find(e.Organizer, e.City, e.PartyName);
    /// <summary>Carica i luoghi dallo sheet: le righe con indirizzo diventano "verificate" (lo sheet vince sul JSON).</summary>
    public int ImportFromSheet(IEnumerable<Event> sheetEvents)
    {
        int n = 0;
        foreach (var e in sheetEvents)
        {
            var address = e.EventDetail?.Address;
            if (string.IsNullOrWhiteSpace(address)) continue;
            if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) continue;

            var t = NormalizeTitle(e.PartyName);
            if (t.Length == 0) continue;

            var key = MakeKey(e.Organizer, e.City);
            if (!_data.TryGetValue(key, out var org))
            {
                // il default resta vuoto: l'indirizzo dell'organizzatore lo metti tu a mano
                org = new OrganizerVenues
                {
                    Default = new VenueInfo
                    {
                        Organizer = e.Organizer,
                        City = e.City,
                        Country = e.Country,
                        Notes = "da compilare: indirizzo dell'organizzatore"
                    }
                };
                _data[key] = org;
                _dirty = true;
            }

            if (!org.Events.TryGetValue(t, out var ev)) org.Events[t] = ev = new VenueInfo();
            Fill(ev, address, e.Country, e.City, e.EventDetail!.Latitude, e.EventDetail.Longitude);
            n++;
        }
        return n;
    }

    private void Fill(VenueInfo v, string address,string country, string city, double? lat, double? lng)
    {
        // verificato a mano: il JSON vince, segnalo solo se lo sheet dice altro
        if (v.Verified)
        {
            if (v.Address is not null && Norm(v.Address) != Norm(address))
                Console.WriteLine($"[store] ATTENZIONE luogo verificato, lo sheet ha un indirizzo diverso: '{v.Address}' vs '{address}'");
            return;
        }

        v.Address = address;
        v.City = city;
        v.Country = country;
        v.Source ??= "sheet";

        // le coordinate dello sheet sono spesso il centro città: le uso solo finché Photon non trova l'indirizzo
        if (v.Lat is null && lat is double la && lng is double lo)
        {
            v.Lat = la;
            v.Lng = lo;
        }

        _dirty = true;
    }
    /// <summary>Carica i luoghi dallo sheet: le righe con indirizzo diventano "verificate" (lo sheet vince sul JSON).</summary>
    //public int ImportFromSheet(IEnumerable<Event> sheetEvents)
    //{
    //    int n = 0;
    //    foreach (var e in sheetEvents)
    //    {
    //        var address = e.EventDetail?.Address;
    //        if (string.IsNullOrWhiteSpace(address)) continue;       // senza indirizzo: coordinate solo approssimate
    //        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) continue;

    //        var key = MakeKey(e.Organizer, e.City);
    //        if (!_data.TryGetValue(key, out var org))
    //        {
    //            org = new OrganizerVenues { Default = new VenueInfo { Organizer = e.Organizer, City = e.City, Country = e.Country } };
    //            _data[key] = org;
    //        }

    //        // primo indirizzo (o stesso indirizzo) -> default; un indirizzo diverso -> eccezione per titolo
    //        VenueInfo target;
    //        if (string.IsNullOrWhiteSpace(org.Default.Address) || Norm(org.Default.Address) == Norm(address))
    //            target = org.Default;
    //        else
    //        {
    //            var t = NormalizeTitle(e.PartyName);
    //            if (t.Length == 0) continue;
    //            if (!org.Events.TryGetValue(t, out target!)) org.Events[t] = target = new VenueInfo();
    //        }

    //        var addressChanged = target.Address is not null && Norm(target.Address) != Norm(address);
    //        target.Address = address;
    //        target.Verified = true;
    //        target.Source = "sheet";

    //        if (e.EventDetail!.Latitude is double la && e.EventDetail.Longitude is double lo)
    //        { target.Lat = la; target.Lng = lo; }
    //        else if (addressChanged)
    //        { target.Lat = null; target.Lng = null; }                // indirizzo cambiato: da geocodificare di nuovo

    //        _dirty = true;
    //        n++;
    //    }
    //    return n;
    //}

    /// <summary>Eventi non verificati con un indirizzo non ancora geocodificato (nuovo o cambiato).
    /// Il default (indirizzo della scuola) non viene geocodificato.</summary>
    public List<(OrganizerVenues Org, VenueInfo Venue)> PendingGeocode() =>
        _data.Values
            .SelectMany(o => o.Events.Values.Select(v => (o, v)))
            .Where(x => !x.v.Verified
                        && !string.IsNullOrWhiteSpace(x.v.Address)
                        && (x.v.GeocodedAddress is null || Norm(x.v.GeocodedAddress) != Norm(x.v.Address)))
            .ToList();

    public void SetCoords(VenueInfo v, double lat, double lng, string source)
    {
        v.Lat = lat; v.Lng = lng; v.Source = source;
        v.GeocodedAddress = v.Address;
        _dirty = true;
    }

    /// <summary>
    /// Porta sugli eventi dello sheet le coordinate del JSON quando sono affidabili e diverse:
    /// luogo verificato a mano, oppure trovato da Photon per lo stesso indirizzo scritto nello sheet.
    /// Restituisce gli eventi modificati (da riscrivere sullo sheet).
    /// </summary>
    public List<Event> ApplyToSheetEvents(IEnumerable<Event> sheetEvents)
    {
        var changed = new List<Event>();
        foreach (var e in sheetEvents)
        {
            if (e.EventDetail is null || string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) continue;
            if (!_data.TryGetValue(MakeKey(e.Organizer, e.City), out var org)) continue;

            var t = NormalizeTitle(e.PartyName);
            if (t.Length == 0 || !org.Events.TryGetValue(t, out var v)) continue;
            if (v.Lat is not double lat || v.Lng is not double lng || (lat == 0 && lng == 0)) continue;

            var reliable = v.Verified
                || (v.GeocodedAddress is not null && !string.IsNullOrWhiteSpace(e.EventDetail.Address)
                    && Norm(v.GeocodedAddress) == Norm(e.EventDetail.Address));
            if (!reliable) continue;

            if (e.EventDetail.Latitude is double la && e.EventDetail.Longitude is double lo
                && Math.Abs(la - lat) < 1e-6 && Math.Abs(lo - lng) < 1e-6) continue;   // già uguali

            e.EventDetail.Latitude = lat;
            e.EventDetail.Longitude = lng;
            changed.Add(e);
        }
        return changed;
    }
    /// <summary>Luoghi verificati con indirizzo ma senza coordinate: da geocodificare.</summary>
    //public List<(OrganizerVenues Org, VenueInfo Venue)> PendingGeocode() =>
    //    _data.Values
    //        .SelectMany(o => new[] { o.Default }.Concat(o.Events.Values).Select(v => (o, v)))
    //        .Where(x => x.v.Verified && !HasCoords(x.v) && !string.IsNullOrWhiteSpace(x.v.Address))
    //        .ToList();

    //public void SetCoords(VenueInfo v, double lat, double lng, string source)
    //{
    //    v.Lat = lat; v.Lng = lng; v.Source = source;
    //    _dirty = true;
    //}
    // ---------- Aggiornamento ----------

    /// <summary>
    /// Registra un luogo trovato automaticamente (Photon, AI...) come NON verificato.
    /// Non sovrascrive mai un record già verificato.
    /// </summary>
    public void RegisterFound(string? organizer, string? city, string? country, string? title,
                          double lat, double lng, string source,
                          string? venueName = null, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(organizer) || string.IsNullOrWhiteSpace(city)) return;

        var key = MakeKey(organizer, city);
        if (!_data.TryGetValue(key, out var org))
        {
            org = new OrganizerVenues
            {
                Default = new VenueInfo { Organizer = organizer, City = city, Country = country }
            };
            _data[key] = org;
        }

        var target = org.Default;
        var t = NormalizeTitle(title);
        if (t.Length > 0 && org.Events.TryGetValue(t, out var ev)) target = ev;

        if (target.Verified) return;

        target.Lat = lat;
        target.Lng = lng;
        target.Source = source;
        target.VenueName ??= venueName;
        target.Address ??= address;
        _dirty = true;
    }

    public void RegisterFound(Event e, double lat, double lng, string source,
                              string? venueName = null, string? address = null)
        => RegisterFound(e.Organizer, e.City, e.Country, e.PartyName, lat, lng, source, venueName, address);

    // ---------- Applicazione a un Event ----------

    /// <summary>Se esiste un luogo verificato, aggiorna coordinate e Location dell'evento.</summary>
    public bool TryApply(Event e)
    {
        var v = Find(e);
        if (v is null) return false;

        e.EventDetail.Latitude = v.Lat;
        e.EventDetail.Longitude = v.Lng;

        var place = string.Join(", ", new[] { v.VenueName, v.Address }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (place.Length > 0) e.EventDetail.Location = place;

        return true;
    }

    /// <summary>Cerca un indirizzo già geocodificato per questo organizzatore (anche non verificato).</summary>
    public VenueInfo? FindByAddress(string? organizer, string? city, string? address)
    {
        if (string.IsNullOrWhiteSpace(organizer) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(address))
            return null;
        if (!_data.TryGetValue(MakeKey(organizer, city), out var org)) return null;

        var a = Norm(address);
        return new[] { org.Default }.Concat(org.Events.Values)
            .FirstOrDefault(v => HasCoords(v) && v.Address is not null && Norm(v.Address) == a);
    }

    /// <summary>Registra un indirizzo geocodificato. Il primo indirizzo diventa il default dell'organizzatore;
    /// se ne arriva uno diverso, va nell'evento specifico (per titolo).</summary>
    public void RegisterAddress(string? organizer, string? city, string? country, string? title,
                                string address, double lat, double lng, string source)
    {
        if (string.IsNullOrWhiteSpace(organizer) || string.IsNullOrWhiteSpace(city)) return;

        var key = MakeKey(organizer, city);
        if (!_data.TryGetValue(key, out var org))
        {
            org = new OrganizerVenues { Default = new VenueInfo { Organizer = organizer, City = city, Country = country } };
            _data[key] = org;
        }

        VenueInfo target;
        if (!HasCoords(org.Default) && string.IsNullOrWhiteSpace(org.Default.Address))
            target = org.Default;
        else if (org.Default.Address is not null && Norm(org.Default.Address) == Norm(address))
            target = org.Default;
        else
        {
            var t = NormalizeTitle(title);
            if (t.Length == 0) return;
            if (!org.Events.TryGetValue(t, out target!)) org.Events[t] = target = new VenueInfo();
        }

        if (target.Verified) return;      // non tocco quello che hai controllato a mano

        target.Address = address;
        target.GeocodedAddress = address;
        target.Lat = lat;
        target.Lng = lng;
        target.Source = source;
        _dirty = true;
    }

    private static bool HasCoords(VenueInfo v) => v.Lat is not null && v.Lng is not null;

    // ---------- Normalizzazione (identica allo script che ha generato il JSON) ----------

    public static string MakeKey(string organizer, string city) => Norm(organizer) + "|" + Norm(city);

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = Norm(title);
        t = Regex.Replace(t, @"\b(19|20)\d{2}\b", " ");                               // anni
        t = Regex.Replace(t, @"\b\d+\s*(a|o)?\s*(edizione|edition|ed)\b", " ");       // "3a edizione"
        t = Regex.Replace(t, @"\b(edizione|edition|nd|rd|th)\b", " ");
        t = Regex.Replace(t, @"\b\d+\b", " ");                                        // numeri isolati
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    private static string Norm(string s)
    {
        var d = WebUtility.HtmlDecode(s).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}