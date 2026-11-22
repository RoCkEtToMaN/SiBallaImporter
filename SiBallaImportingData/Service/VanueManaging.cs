using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;
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
    // data (yyyy-MM-dd) dell'ultima ricerca dell'indirizzo con Gemini finita senza risultato: per un po' non la ripeto
    // (scritto nel file solo se c'è, per non aggiungere una riga vuota a ogni voce)
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GeminiNonTrovato { get; set; }
    // stili di ballo imparati dalla colonna H dello sheet (solo celle non colorate come "dedotte"):
    // nell'evento = quelli della data più recente, nel default = quelli presenti in almeno metà delle righe dell'organizzatore
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stili { get; set; }
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
    /// <summary>
    /// Carica i luoghi dallo sheet: l'indirizzo scritto a mano in una riga diventa la voce verificata dell'evento
    /// (organizzatore + città + titolo). Lo sheet vince sul JSON; se lo stesso evento ha indirizzi diversi vince la data più recente.
    /// Gli eventi futuri senza indirizzo entrano come voci vuote (default + evento): quando scrivi l'indirizzo nello sheet,
    /// il giro successivo le riempie.
    /// </summary>
    public int ImportFromSheet(IEnumerable<Event> sheetEvents)
    {
        int n = 0, empty = 0;
        foreach (var e in sheetEvents.OrderBy(x => x.DateStart))
        {
            if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) continue;
            // scritta dal programma e non ancora controllata ("da verificare", "nuovo", "gemini"): indirizzo e coordinate non sono una fonte
            // "no": evento scartato da te, non è una fonte nemmeno lui
            // "storico": evento passato recuperato dalle vecchie versioni, mai controllato
            if (e.LuogoVerificato is ImportData.VerifiedPending or ImportData.VerifiedNew or ImportData.VerifiedGemini or ImportData.VerifiedNo
                or ImportData.VerifiedStorico or ImportData.VerifiedMercatino or ImportData.VerifiedWorkshop) continue;

            var t = NormalizeTitle(e.PartyName);
            if (t.Length == 0) continue;

            var address = e.EventDetail?.Address;
            if (string.IsNullOrWhiteSpace(address))
            {
                if (e.DateStart.Date >= DateTime.Today && AddEmpty(e, t)) empty++;
                continue;
            }

            var key = MakeKey(e.Organizer, e.City);
            if (!_data.TryGetValue(key, out var org))
            {
                org = new OrganizerVenues
                {
                    Default = new VenueInfo
                    {
                        Organizer = e.Organizer,
                        City = e.City,
                        Country = e.Country
                    }
                };
                _data[key] = org;
                _dirty = true;
            }

            org.Events.TryGetValue(t, out var ev);

            // copiato dal programma: non è una fonte e viene riallineato al dizionario
            if (e.LuogoVerificato == ImportData.VerifiedAuto)
            {
                if (ev?.Address is not null && Norm(ev.Address) != Norm(address))
                    Console.WriteLine($"[store] ⚠️ {e.SheetName} riga {e.SheetRow}: '{address}' è segnato '{ImportData.VerifiedAuto}', viene sostituito con '{ev.Address}'. Se l'hai corretto tu, scrivi '{ImportData.VerifiedOk}' o svuota la colonna");
                continue;
            }

            if (ev is null) org.Events[t] = ev = new VenueInfo();
            Fill(ev, address, e.Country, e.City, e.EventDetail!.Latitude, e.EventDetail.Longitude,
                 locked: e.LuogoVerificato == ImportData.VerifiedOk);
            n++;
        }
        if (empty > 0) Console.WriteLine($"[store] eventi senza indirizzo aggiunti come voci vuote: {empty}");
        return n;
    }

    // il default dell'organizzatore prende uno stile solo se ha almeno queste righe con lo stile compilato
    private const int MinRigheStiliOrganizzatore = 2;

    /// <summary>
    /// Impara gli stili dalla colonna H: per ogni evento (organizzatore + città + titolo) quelli della data più recente,
    /// per l'organizzatore quelli presenti in almeno metà delle sue righe. Salta le righe "da verificare" e le celle
    /// con lo stile dedotto dal programma e non ancora confermato. Le voci senza righe nello sheet restano come sono
    /// </summary>
    public int ImparaStili(IEnumerable<Event> sheetEvents)
    {
        var righe = sheetEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
            .Where(e => e.LuogoVerificato != ImportData.VerifiedPending && e.LuogoVerificato != ImportData.VerifiedStorico
                        && !ImportData.Scartato(e) && !e.StileDedotto)
            .Select(e => (Evento: e, Stili: SiBallaImportingData.Utils.StiliEvento.Normalizza(e.SheetStiliRaw)))
            .Where(x => x.Stili.Count > 0)
            .ToList();

        int cambiate = 0;
        foreach (var org in righe.GroupBy(x => MakeKey(x.Evento.Organizer!, x.Evento.City!)))
        {
            var primo = org.First().Evento;
            if (!_data.TryGetValue(org.Key, out var voce))
                _data[org.Key] = voce = new OrganizerVenues { Default = new VenueInfo { Organizer = primo.Organizer, City = primo.City, Country = primo.Country } };

            foreach (var ev in org.GroupBy(x => NormalizeTitle(x.Evento.PartyName)).Where(g => g.Key.Length > 0))
            {
                var stili = ev.OrderBy(x => x.Evento.DateStart).Last().Stili;
                if (!voce.Events.TryGetValue(ev.Key, out var v))
                    voce.Events[ev.Key] = v = new VenueInfo { City = primo.City, Country = primo.Country };
                if (ImpostaStili(v, stili)) cambiate++;
            }

            var n = org.Count();
            var comuni = n < MinRigheStiliOrganizzatore ? null : org
                .SelectMany(x => x.Stili)
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() * 2 >= n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (comuni is { Count: > 0 } && ImpostaStili(voce.Default, comuni)) cambiate++;
        }
        if (cambiate > 0) Console.WriteLine($"[store] stili imparati dallo sheet: {cambiate} voci aggiornate");
        return cambiate;
    }

    private bool ImpostaStili(VenueInfo v, List<string> stili)
    {
        if (v.Stili is not null && v.Stili.SequenceEqual(stili, StringComparer.OrdinalIgnoreCase)) return false;
        v.Stili = stili;
        _dirty = true;
        return true;
    }

    /// <summary>Stili dello stesso evento (organizzatore + città + titolo), null se non noti</summary>
    public List<string>? StiliEvento(Event e)
    {
        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) return null;
        if (!_data.TryGetValue(MakeKey(e.Organizer, e.City), out var org)) return null;
        var t = NormalizeTitle(e.PartyName);
        return t.Length > 0 && org.Events.TryGetValue(t, out var v) ? v.Stili : null;
    }

    /// <summary>Stili abituali dell'organizzatore (in quella città), null se non noti</summary>
    public List<string>? StiliOrganizzatore(Event e)
    {
        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) return null;
        return _data.TryGetValue(MakeKey(e.Organizer, e.City), out var org) ? org.Default.Stili : null;
    }

    public const string SourceInitial = "iniziale";   // coordinate della riga, il default aspetta il geocoding inverso

    /// <summary>
    /// Voce vuota per un evento senza indirizzo, con le coordinate iniziali della riga (colonna F).
    /// Il default dell'organizzatore prende le stesse coordinate solo se non ne ha: poi non cambia più.
    /// Non tocca le voci già compilate.
    /// </summary>
    private bool AddEmpty(Event e, string title)
    {
        var key = MakeKey(e.Organizer!, e.City!);
        if (!_data.TryGetValue(key, out var org))
        {
            org = new OrganizerVenues { Default = new VenueInfo { Organizer = e.Organizer, City = e.City, Country = e.Country } };
            _data[key] = org;
            _dirty = true;
        }

        double? lat = e.EventDetail?.Latitude, lng = e.EventDetail?.Longitude;
        var hasCoords = lat is double la && lng is double lo && !(la == 0 && lo == 0);

        if (hasCoords && IsEmpty(org.Default))
        {
            org.Default.Lat = lat;
            org.Default.Lng = lng;
            org.Default.Source = SourceInitial;
            _dirty = true;
        }

        var added = false;
        if (!org.Events.TryGetValue(title, out var ev))
        {
            org.Events[title] = ev = new VenueInfo { City = e.City, Country = e.Country };
            added = true;
            _dirty = true;
        }
        if (hasCoords && IsEmpty(ev))
        {
            ev.Lat = lat;
            ev.Lng = lng;
            ev.Source = SourceInitial;
            _dirty = true;
        }
        return added;
    }

    /// <summary>Data dell'ultima ricerca con Gemini senza risultato per questo evento, null se mai cercato (o trovato)</summary>
    public DateTime? GeminiNonTrovato(Event e)
    {
        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) return null;
        if (!_data.TryGetValue(MakeKey(e.Organizer, e.City), out var org)) return null;
        if (!org.Events.TryGetValue(NormalizeTitle(e.PartyName), out var v)) return null;
        return DateTime.TryParseExact(v.GeminiNonTrovato, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public const string SourceAgente = "agente";

    /// <summary>
    /// Luogo trovato dall'agente di verifica (confermato da Photon): va nella voce dell'evento (organizzatore + città + titolo),
    /// NON verificato, con la fonte nelle note. Diventa verificato quando scrivi "ok" sulla riga dello Sheet (ImportFromSheet).
    /// Le voci che hai già verificato non si toccano. True se ha scritto qualcosa
    /// </summary>
    public bool RegistraDaAgente(Event e, string? locale, string indirizzo, double lat, double lng, string come, string? fonte)
    {
        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) return false;
        var t = NormalizeTitle(e.PartyName);
        if (t.Length == 0) return false;

        var key = MakeKey(e.Organizer, e.City);
        if (!_data.TryGetValue(key, out var org))
            _data[key] = org = new OrganizerVenues { Default = new VenueInfo { Organizer = e.Organizer, City = e.City, Country = e.Country } };
        if (!org.Events.TryGetValue(t, out var v))
            org.Events[t] = v = new VenueInfo { City = e.City, Country = e.Country };
        if (v.Verified) return false;

        v.VenueName = string.IsNullOrWhiteSpace(locale) ? v.VenueName : locale.Trim();
        v.Address = indirizzo.Trim();
        v.GeocodedAddress = indirizzo.Trim();
        v.Lat = lat;
        v.Lng = lng;
        v.Source = SourceAgente;
        v.Notes = $"{DateTime.Today:yyyy-MM-dd} {come}" + (string.IsNullOrWhiteSpace(fonte) ? "" : $"; fonte: {fonte}");
        v.GeminiNonTrovato = null;
        _dirty = true;
        return true;
    }

    /// <summary>Ricerca con Gemini senza risultato: lo segno nella voce dell'evento (creata se manca) per non ripeterla ogni giro</summary>
    public void SegnaGeminiNonTrovato(Event e)
    {
        if (string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) return;
        var t = NormalizeTitle(e.PartyName);
        if (t.Length == 0) return;

        var key = MakeKey(e.Organizer, e.City);
        if (!_data.TryGetValue(key, out var org))
            _data[key] = org = new OrganizerVenues { Default = new VenueInfo { Organizer = e.Organizer, City = e.City, Country = e.Country } };
        if (!org.Events.TryGetValue(t, out var v))
            org.Events[t] = v = new VenueInfo { City = e.City, Country = e.Country };

        v.GeminiNonTrovato = DateTime.Today.ToString("yyyy-MM-dd");
        _dirty = true;
    }

    private static bool IsEmpty(VenueInfo v) => !v.Verified && string.IsNullOrWhiteSpace(v.Address) && !HasCoords(v);

    /// <summary>Default con le coordinate iniziali e ancora senza via.</summary>
    public List<VenueInfo> PendingReverse() =>
        _data.Values.Select(o => o.Default)
            .Where(v => v.Source == SourceInitial && string.IsNullOrWhiteSpace(v.Address) && HasCoords(v))
            .ToList();

    /// <summary>Via trovata dalle coordinate del default (address null = non trovata o centro città): le coordinate restano quelle.</summary>
    public void SetReverse(VenueInfo v, string? address, string source)
    {
        v.Address = address;
        v.GeocodedAddress = address;
        v.Source = source;
        _dirty = true;
    }

    /// <param name="locked">"ok" nello sheet: anche le coordinate sono controllate, Photon non le cambia</param>
    private void Fill(VenueInfo v, string address, string? country, string? city, double? lat, double? lng, bool locked)
    {
        var changed = v.Address is null || Norm(v.Address) != Norm(address);

        if (locked && lat is double la && lng is double lo)
        {
            if (!changed && v.Source == "manuale" && v.Lat == la && v.Lng == lo) return;
            v.Address = v.GeocodedAddress = address;
            v.Lat = la;
            v.Lng = lo;
            v.City = city;
            v.Country = country;
            v.Verified = true;
            v.Source = "manuale";
            v.VerifiedOn = DateTime.Today.ToString("yyyy-MM-dd");
            _dirty = true;
            return;
        }

        if (!changed && v.Verified) return;

        if (changed)
        {
            if (v.Address is not null)
                Console.WriteLine($"[store] indirizzo cambiato nello sheet: '{v.Address}' -> '{address}'");
            v.Address = address;
            // coordinate dello sheet: valgono finché Photon non geocodifica il nuovo indirizzo
            v.Lat = lat;
            v.Lng = lng;
            v.GeocodedAddress = null;
        }

        // scritto a mano nello sheet = verificato
        v.City = city;
        v.Country = country;
        v.Verified = true;
        v.Source = "sheet";
        v.VerifiedOn = DateTime.Today.ToString("yyyy-MM-dd");
        _dirty = true;
    }
    /// <summary>Voci con un indirizzo non ancora geocodificato (nuovo o cambiato).
    /// Il default (indirizzo della scuola) non viene geocodificato.</summary>
    public List<(OrganizerVenues Org, VenueInfo Venue)> PendingGeocode() =>
        _data.Values
            .SelectMany(o => o.Events.Values.Select(v => (o, v)))
            .Where(x => !string.IsNullOrWhiteSpace(x.v.Address)
                        && (x.v.GeocodedAddress is null || Norm(x.v.GeocodedAddress) != Norm(x.v.Address)))
            .ToList();

    public void SetCoords(VenueInfo v, double lat, double lng, string source)
    {
        v.Lat = lat; v.Lng = lng; v.Source = source;
        v.GeocodedAddress = v.Address;
        _dirty = true;
    }

    /// <summary>
    /// Porta sugli eventi dello sheet i luoghi del JSON:
    /// - riga senza indirizzo e voce verificata per lo stesso evento: copio indirizzo e coordinate (IndirizzoDalDizionario);
    /// - riga con indirizzo già geocodificato da Photon: aggiorno solo le coordinate.
    /// Restituisce gli eventi modificati (da riscrivere sullo sheet).
    /// </summary>
    public List<Event> ApplyToSheetEvents(IEnumerable<Event> sheetEvents)
    {
        var changed = new List<Event>();
        foreach (var e in sheetEvents)
        {
            if (e.EventDetail is null || string.IsNullOrWhiteSpace(e.Organizer) || string.IsNullOrWhiteSpace(e.City)) continue;
            // non la tocco: riscrivendola diventerebbe "auto" e finirebbe sul DB senza che tu l'abbia controllata
            if (e.LuogoVerificato is ImportData.VerifiedPending or ImportData.VerifiedNo or ImportData.VerifiedMercatino or ImportData.VerifiedWorkshop) continue;
            if (!_data.TryGetValue(MakeKey(e.Organizer, e.City), out var org)) continue;

            var t = NormalizeTitle(e.PartyName);
            if (t.Length == 0 || !org.Events.TryGetValue(t, out var v)) continue;

            var rowAddress = e.EventDetail.Address;
            // righe "auto" seguono sempre il dizionario: se correggi la riga sorgente, la correzione arriva anche qui.
            // Lo stesso per le righe "gemini": un indirizzo verificato da te vale più di quello trovato da Gemini
            var copyAddress = v.Verified && !string.IsNullOrWhiteSpace(v.Address)
                && (string.IsNullOrWhiteSpace(rowAddress)
                    || (e.LuogoVerificato is ImportData.VerifiedAuto or ImportData.VerifiedGemini && Norm(rowAddress) != Norm(v.Address)));
            var sameAddress = !string.IsNullOrWhiteSpace(rowAddress) && v.GeocodedAddress is not null
                              && Norm(v.GeocodedAddress) == Norm(rowAddress);
            if (!copyAddress && !sameAddress) continue;

            var modified = false;
            if (copyAddress)
            {
                e.EventDetail.Address = e.EventDetail.Location = v.Address;
                e.IndirizzoDalDizionario = true;
                modified = true;
            }

            if (v.Lat is double lat && v.Lng is double lng && !(lat == 0 && lng == 0)
                && !(e.EventDetail.Latitude is double la && e.EventDetail.Longitude is double lo
                     && Math.Abs(la - lat) < 1e-6 && Math.Abs(lo - lng) < 1e-6))
            {
                e.EventDetail.Latitude = lat;
                e.EventDetail.Longitude = lng;
                modified = true;
            }

            if (modified) changed.Add(e);
        }
        return changed;
    }

    // ---------- Applicazione a un Event ----------

    /// <summary>Se esiste un luogo verificato, aggiorna coordinate e Location dell'evento.</summary>
    public bool TryApply(Event e)
    {
        var v = Find(e);
        if (v is null) return false;

        e.EventDetail.Latitude = v.Lat;
        e.EventDetail.Longitude = v.Lng;
        if (!string.IsNullOrWhiteSpace(v.Address)) e.EventDetail.Address = v.Address;   // finisce nella colonna F

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
        // il default con le coordinate iniziali non cambia più
        else if (org.Default.Address is not null && Norm(org.Default.Address) == Norm(address)
                 && org.Default.Source?.StartsWith(SourceInitial) != true)
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
