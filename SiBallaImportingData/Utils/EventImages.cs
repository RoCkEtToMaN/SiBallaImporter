using SiBalla.Service;
using SiBallaImportingData.Model;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;

namespace SiBallaImportingData.Utils
{
    // Immagini degli eventi: il file sta su Appwrite Storage, nello Sheet solo "sì" nella colonna "Immagine".
    // Il nome del file si ricava da data + titolo della riga, quindi se li cambi nello Sheet il collegamento si perde
    public static class EventImages
    {
        private const long MaxBytes = 10 * 1024 * 1024;

        private static readonly HttpClient _client = CreateClient();
        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
            return client;
        }

        // id del file su Storage (max 36 caratteri a-z0-9): "ev" + hash di "yyyy-MM-dd|titolo normalizzato"
        public static string FileId(Event e)
        {
            var key = $"{e.DateStart:yyyy-MM-dd}|{KnownVenueStore.NormalizeTitle(e.PartyName)}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return "ev" + hash[..32];
        }

        private static string ThumbnailFileId(string fileId)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("thumbnail|" + fileId))).ToLowerInvariant();
            return "th" + hash[..32];
        }

        private static async Task<bool> EnsureThumbnailAsync(string fileId, byte[]? originalBytes, HashSet<string> knownFiles, AppWriteService appWrite)
        {
            var thumbnailId = ThumbnailFileId(fileId);
            if (knownFiles.Contains(thumbnailId)) return true;

            if (originalBytes is null)
            {
                var original = await DownloadAsync(AppWriteService.ImageViewUrl(fileId));
                if (original is not { } downloaded) return false;
                originalBytes = downloaded.Bytes;
            }

            var thumbnailBytes = await CreateThumbnailAsync(originalBytes);
            if (thumbnailBytes is null) return false;

            if (!await appWrite.UploadImageAsync(thumbnailId, thumbnailBytes, thumbnailId + ".webp", "image/webp"))
                return false;

            knownFiles.Add(thumbnailId);
            Console.WriteLine($"🖼️ Miniatura creata per {fileId}: {originalBytes.Length / 1024} KB → {thumbnailBytes.Length / 1024} KB");
            return true;
        }

        private static async Task<byte[]?> CreateThumbnailAsync(byte[] sourceBytes)
        {
            try
            {
                using var input = new MemoryStream(sourceBytes);
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(input);
                image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(900, 900),
                    Mode = ResizeMode.Max
                }));

                using var output = new MemoryStream();
                await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = 82 });
                return output.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Miniatura non creata: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Eventi trovati online che stanno per finire nello Sheet: scarica l'immagine e la carica su Storage.
        /// Se va a buon fine segna EventDetail.HasImage, così AppendToGoogleSheet scrive "sì" nella colonna "Immagine"
        /// </summary>
        public static async Task UploadAsync(IEnumerable<Event> events, AppWriteService appWrite)
        {
            var conImmagine = events.Where(x => !string.IsNullOrWhiteSpace(x.EventDetail?.ImageUrl)).ToList();
            if (conImmagine.Count == 0) return;

            // senza bucket non provo nemmeno: le righe vanno nello Sheet senza "sì"
            if (!await appWrite.EnsureImagesBucketAsync())
            {
                Console.WriteLine($"🖼️ Immagini non caricate ({conImmagine.Count} eventi): bucket non disponibile");
                return;
            }

            // file già su Storage (evento caricato in un giro precedente, o già visto in questo giro): non lo riscarico.
            // Se l'elenco non è disponibile provo a caricare tutto (il 409 lo gestisce UploadImageAsync)
            var ids = new HashSet<string>((await appWrite.GetImageFilesAsync())?.Keys ?? Enumerable.Empty<string>());

            int caricate = 0, fallite = 0, presenti = 0;
            foreach (var ev in conImmagine)
            {
                var fileId = FileId(ev);
                if (ids.Contains(fileId))
                {
                    ev.EventDetail.HasImage = true;
                    presenti++;
                    continue;
                }

                var url = ev.EventDetail.ImageUrl!;
                var image = await DownloadAsync(url);
                if (image is null)
                {
                    fallite++;
                    continue;
                }

                if (await appWrite.UploadImageAsync(fileId, image.Value.Bytes, fileId + image.Value.Extension, image.Value.MimeType))
                {
                    ev.EventDetail.HasImage = true;
                    ids.Add(fileId);
                    caricate++;
                }
                else
                {
                    fallite++;
                    Console.WriteLine($"   immagine di '{ev.PartyName?.Trim()}' ({ev.DateStart:yyyy-MM-dd}): {url}");
                }
            }
            Console.WriteLine($"🖼️ Immagini caricate su Storage: {caricate}, già presenti: {presenti}, non riuscite: {fallite}");
        }

        /// <summary>
        /// Eventi dello Sheet prima di andare sul DB: con "sì" nella colonna "Immagine" metto in ImageUrl il link di Storage.
        /// Se il file non c'è (es. data o titolo cambiati nello Sheet) tolgo HasImage, così l'app non mostra un'immagine rotta
        /// </summary>
        public static async Task ApplyToSheetEventsAsync(IEnumerable<Event> events, AppWriteService appWrite)
        {
            var conImmagine = events.Where(x => x.EventDetail?.HasImage == true).ToList();
            if (conImmagine.Count == 0) return;

            // null = elenco non disponibile: mi fido dello Sheet
            var ids = await appWrite.GetImageFilesAsync();
            var knownFiles = new HashSet<string>(ids?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            int mancanti = 0;
            foreach (var ev in conImmagine)
            {
                var logo = ImportData.IsLogo(ev.SheetImageRaw);
                var fileId = logo ? LogoFileId(ev.Organizer, ev.City) : FileId(ev);
                if (ids is not null && !ids.ContainsKey(fileId))
                {
                    mancanti++;
                    ev.EventDetail.HasImage = false;
                    Console.WriteLine(logo
                        ? $"⚠️ {ev.SheetName} riga {ev.SheetRow}: '{ImportData.ImageHeader}' = logo ma il logo di '{ev.Organizer?.Trim()}' ({ev.City}) non è su Storage (manca data/loghi/{LogoSlug(ev.Organizer, ev.City)}.jpg?)"
                        : $"⚠️ {ev.SheetName} riga {ev.SheetRow}: '{ImportData.ImageHeader}' = sì ma il file {fileId} non è su Storage (data o titolo cambiati?)");
                    continue;
                }
                ev.EventDetail.ImageUrl = AppWriteService.ImageViewUrl(fileId);
                ev.EventDetail.ImageThumbnailUrl = await EnsureThumbnailAsync(fileId, null, knownFiles, appWrite)
                    ? AppWriteService.ImageViewUrl(ThumbnailFileId(fileId))
                    : null;
            }
            Console.WriteLine($"🖼️ Eventi dello Sheet con immagine: {conImmagine.Count - mancanti}, file mancanti: {mancanti}");
        }

        /// <summary>
        /// Righe future dello Sheet con la colonna "Immagine" vuota: se il file è già su Storage scrive "sì",
        /// altrimenti prende l'immagine di anteprima (og:image) del sito in "Riferimento", la carica e scrive "sì";
        /// se il sito non ne ha scrive "no", così al giro dopo non la ricerca. Al massimo max righe per giro
        /// </summary>
        public static async Task BackfillSheetAsync(IEnumerable<Event> sheetEvents, AppWriteService appWrite, int max)
        {
            var daCercare = sheetEvents
                .Where(e => e.DateStart.Date >= DateTime.Today && e.SheetImageColumn is not null && string.IsNullOrWhiteSpace(e.SheetImageRaw))
                .OrderBy(e => e.DateStart)
                .ToList();
            Console.WriteLine($"\n===== Immagini per le righe dello Sheet: {daCercare.Count} righe future senza valore in '{ImportData.ImageHeader}' (max {max} per giro) =====");
            if (daCercare.Count == 0 || !await appWrite.EnsureImagesBucketAsync()) return;

            // file già caricati (es. evento trovato online e scartato perché nello Sheet c'era già con un nome scritto diverso)
            var ids = await appWrite.GetImageFilesAsync() ?? new Dictionary<string, string>();
            var celle = new List<(Event Event, string Value)>();
            foreach (var ev in daCercare.Where(e => ids.ContainsKey(FileId(e))))
                celle.Add((ev, ImportData.ImageYes));
            Console.WriteLine($"🖼️ Già su Storage: {celle.Count}");

            // stesso sito su più righe (pagina dell'organizzatore): lo leggo una volta sola per giro
            var perSito = new Dictionary<string, (byte[] Bytes, string MimeType, string Extension)?>(StringComparer.OrdinalIgnoreCase);
            int caricate = 0, senza = 0;
            foreach (var ev in daCercare.Where(e => !ids.ContainsKey(FileId(e))).Take(max))
            {
                var site = NormalizeSite(ev.EventDetail?.WebSite);
                if (site is null)
                {
                    celle.Add((ev, ImportData.ImageNo));
                    senza++;
                    continue;
                }

                if (!perSito.TryGetValue(site, out var image))
                {
                    var imageUrl = await FindPreviewImageAsync(site);
                    image = imageUrl is null ? null : await DownloadAsync(imageUrl);
                    perSito[site] = image;
                    Console.WriteLine($"{(image is null ? "❌" : "✅")} {site}{(imageUrl is null ? "" : " -> " + imageUrl)}");
                    await Task.Delay(500);
                }

                var fileId = FileId(ev);
                if (image is not null && await appWrite.UploadImageAsync(fileId, image.Value.Bytes, fileId + image.Value.Extension, image.Value.MimeType))
                {
                    celle.Add((ev, ImportData.ImageYes));
                    caricate++;
                }
                else if (image is null)
                {
                    celle.Add((ev, ImportData.ImageNo));
                    senza++;
                }
                // caricamento fallito: cella vuota, riprovo al prossimo giro
            }

            // in memoria come sullo sheet: il passaggio dei loghi parte dalle righe "no"
            foreach (var (ev, value) in celle)
            {
                ev.SheetImageRaw = value;
                if (value == ImportData.ImageYes)
                    ev.EventDetail.HasImage = true;
            }
            Console.WriteLine($"🖼️ Righe dello Sheet: {caricate} immagini caricate, {senza} senza immagine sul sito ({perSito.Count} siti letti)");
            await ImportData.UpdateSheetImageCellsAsync(celle);
        }

        // formati accettati per i file messi nel repo (data/immagini, data/loghi)
        private static readonly Dictionary<string, string> FolderMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp"
        };

        // ---------- Immagini degli eventi messe a mano (data/immagini) ----------

        public const string ManualFolder = "data/immagini";

        /// <summary>
        /// Immagini in data/immagini, una per evento, nome "data_titolo" (es. 2026-11-20_swing-sul-mare.jpg):
        /// le carica su Storage al posto di quella trovata online (di nuovo se il file cambia) e scrive "sì"
        /// nella colonna "Immagine" delle righe future con quella data e quel titolo
        /// </summary>
        public static async Task ApplyManualImagesAsync(IEnumerable<Event> sheetEvents, AppWriteService appWrite)
        {
            var dir = DictionaryRepoSync.RepoPath(ManualFolder);
            if (dir is null || !Directory.Exists(dir)) return;

            var files = Directory.GetFiles(dir).Where(f => FolderMimeTypes.ContainsKey(Path.GetExtension(f))).ToList();
            Console.WriteLine($"\n===== Immagini messe a mano: {files.Count} file in {ManualFolder} =====");
            if (files.Count == 0) return;

            var righe = sheetEvents.Where(e => e.DateStart.Date >= DateTime.Today && e.SheetImageColumn is not null).ToList();
            var daCaricare = new List<(string Path, List<Event> Righe)>();
            foreach (var path in files)
            {
                var nome = Path.GetFileNameWithoutExtension(path);
                var sep = nome.IndexOf('_');
                if (sep < 0 || !DateTime.TryParseExact(nome[..sep], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                {
                    Console.WriteLine($"⚠️ {Path.GetFileName(path)}: il nome deve essere data_titolo, es. 2026-11-20_swing-sul-mare.jpg");
                    continue;
                }
                if (data < DateTime.Today) continue;   // evento passato: il file si può cancellare

                // titolo normalizzato come per l'id del file: maiuscole, accenti, trattini e numeri non contano
                var titolo = KnownVenueStore.NormalizeTitle(nome[(sep + 1)..]);
                var trovate = righe.Where(e => e.DateStart.Date == data && KnownVenueStore.NormalizeTitle(e.PartyName) == titolo).ToList();
                if (trovate.Count == 0)
                {
                    Console.WriteLine($"⚠️ {Path.GetFileName(path)}: nessuna riga dello Sheet il {data:dd/MM/yyyy} con titolo '{titolo}'");
                    continue;
                }
                daCaricare.Add((path, trovate));
            }
            if (daCaricare.Count == 0 || !await appWrite.EnsureImagesBucketAsync()) return;

            // il nome su Storage contiene l'hash del contenuto: se sostituisci il file nella cartella lo ricarico
            var storage = await appWrite.GetImageFilesAsync() ?? new Dictionary<string, string>();
            var celle = new List<(Event Event, string Value)>();
            int caricate = 0;
            foreach (var (path, trovate) in daCaricare)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                if (bytes.Length == 0 || bytes.Length > MaxBytes)
                {
                    Console.WriteLine($"⚠️ {Path.GetFileName(path)}: file vuoto o troppo grande ({bytes.Length / 1024} KB)");
                    continue;
                }
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var fileId = FileId(trovate[0]);   // stessa data e stesso titolo normalizzato -> stesso id per tutte le righe
                var name = $"{fileId}-{Convert.ToHexString(SHA256.HashData(bytes))[..8].ToLowerInvariant()}{ext}";
                if (!storage.TryGetValue(fileId, out var current) || current != name)
                {
                    // c'era l'immagine trovata online (o una versione precedente del file): la sostituisco
                    if (current is not null)
                    {
                        await appWrite.DeleteImageAsync(fileId);
                        var thumbnailId = ThumbnailFileId(fileId);
                        await appWrite.DeleteImageAsync(thumbnailId);
                        storage.Remove(thumbnailId);
                    }
                    if (!await appWrite.UploadImageAsync(fileId, bytes, name, FolderMimeTypes[ext]))
                        continue;
                    caricate++;
                    Console.WriteLine($"✅ Immagine caricata: {Path.GetFileName(path)}");
                }

                foreach (var ev in trovate)
                {
                    ev.EventDetail.HasImage = true;
                    if (!string.Equals(ev.SheetImageRaw?.Trim(), ImportData.ImageYes, StringComparison.OrdinalIgnoreCase))
                    {
                        ev.SheetImageRaw = ImportData.ImageYes;
                        celle.Add((ev, ImportData.ImageYes));
                    }
                }
            }
            Console.WriteLine($"🖼️ Immagini messe a mano: {caricate} caricate, {celle.Count} righe dello Sheet aggiornate");
            await ImportData.UpdateSheetImageCellsAsync(celle);
        }

        // ---------- Loghi degli organizzatori (data/loghi) ----------

        public const string LogoFolder = "data/loghi";

        // nome del file del logo: organizzatore e città normalizzati come nel dizionario dei luoghi, es. "simone-scanzoni_roma"
        public static string LogoSlug(string? organizer, string? city) =>
            KnownVenueStore.MakeKey(organizer ?? "", city ?? "").Replace(' ', '-').Replace('|', '_');

        public static string LogoFileId(string? organizer, string? city) => LogoFileId(LogoSlug(organizer, city));

        /// <summary>
        /// Loghi in data/loghi (un file per organizzatore, es. simone-scanzoni_roma.jpg): li carica su Storage (di nuovo se il file cambia)
        /// e alle righe future con "no" in "Immagine" di quell'organizzatore scrive "logo"
        /// </summary>
        public static async Task ApplyLogosAsync(IEnumerable<Event> sheetEvents, AppWriteService appWrite)
        {
            var dir = DictionaryRepoSync.RepoPath(LogoFolder);
            if (dir is null || !Directory.Exists(dir)) return;

            var mimeTypes = FolderMimeTypes;
            var loghi = Directory.GetFiles(dir)
                .Where(f => mimeTypes.ContainsKey(Path.GetExtension(f)))
                .GroupBy(f => Path.GetFileNameWithoutExtension(f).Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());
            Console.WriteLine($"\n===== Loghi degli organizzatori: {loghi.Count} file in {LogoFolder} =====");
            if (loghi.Count == 0 || !await appWrite.EnsureImagesBucketAsync()) return;

            // il nome su Storage contiene l'hash del contenuto: se sostituisci il file nella cartella lo ricarico
            var files = await appWrite.GetImageFilesAsync() ?? new Dictionary<string, string>();
            var pronti = new HashSet<string>();
            foreach (var (slug, path) in loghi)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var name = $"{slug}-{Convert.ToHexString(SHA256.HashData(bytes))[..8].ToLowerInvariant()}{ext}";
                var fileId = LogoFileId(slug);
                if (files.TryGetValue(fileId, out var current))
                {
                    if (current == name)
                    {
                        pronti.Add(slug);
                        continue;
                    }
                    await appWrite.DeleteImageAsync(fileId);
                    var thumbnailId = ThumbnailFileId(fileId);
                    await appWrite.DeleteImageAsync(thumbnailId);
                    files.Remove(thumbnailId);
                }
                if (await appWrite.UploadImageAsync(fileId, bytes, name, mimeTypes[ext]))
                {
                    pronti.Add(slug);
                    Console.WriteLine($"✅ Logo caricato: {Path.GetFileName(path)}");
                }
            }

            var celle = sheetEvents
                .Where(e => e.DateStart.Date >= DateTime.Today && e.SheetImageColumn is not null && e.SheetImageRaw?.Trim().ToLowerInvariant() == ImportData.ImageNo)
                .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City) && pronti.Contains(LogoSlug(e.Organizer, e.City)))
                .Select(e => (Event: e, Value: ImportData.ImageLogo))
                .ToList();
            foreach (var (ev, value) in celle)
            {
                ev.SheetImageRaw = value;
                ev.EventDetail.HasImage = true;
            }
            Console.WriteLine($"🖼️ Righe con il logo dell'organizzatore: {celle.Count}");
            await ImportData.UpdateSheetImageCellsAsync(celle);
        }

        // id dal nome del file (già normalizzato)
        private static string LogoFileId(string slug)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug))).ToLowerInvariant();
            return "lg" + hash[..32];
        }

        // primo sito della cella "Riferimento" (a volte più link separati da ';'), con https:// se manca
        private static string? NormalizeSite(string? webSite)
        {
            var site = webSite?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(site) || site.Contains(' ')) return null;
            if (!site.StartsWith("http", StringComparison.OrdinalIgnoreCase)) site = "https://" + site;
            return Uri.TryCreate(site, UriKind.Absolute, out _) ? site : null;
        }

        private static readonly string[] GenericImages =
        {
            "static.fillout.com/fillout-og-image",
            "dice.fm/static/images/dice-fan-social",
        };

        // link a gruppi WhatsApp/Telegram o a moduli Google: l'anteprima non è dell'evento né dell'organizzatore
        private static bool IsChatLink(Uri uri) =>
            new[] { "whatsapp.com", "whatsapp.net", "wa.me", "t.me", "telegram.org", "forms.gle" }
                .Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
            || (uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/forms", StringComparison.OrdinalIgnoreCase));

        // immagine di anteprima dichiarata dalla pagina stessa (og:image, poi twitter:image)
        private static async Task<string?> FindPreviewImageAsync(string site)
        {
            try
            {
                using var response = await _client.GetAsync(site);
                if (!response.IsSuccessStatusCode) return null;

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(await response.Content.ReadAsStringAsync());
                var content = doc.DocumentNode
                    .SelectNodes("//meta[@property='og:image' or @property='og:image:url' or @property='og:image:secure_url' or @name='twitter:image']")
                    ?.Select(m => m.GetAttributeValue("content", "").Trim())
                    .FirstOrDefault(c => c.Length > 0);
                if (content is null) return null;

                // può essere relativo alla pagina (dopo eventuali redirect)
                var baseUri = response.RequestMessage?.RequestUri ?? new Uri(site);
                if (IsChatLink(baseUri) || !Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(content), out var abs) || IsChatLink(abs))
                    return null;
                // immagine standard della piattaforma (moduli, biglietterie), uguale per tutti gli eventi
                if (GenericImages.Any(g => abs.ToString().Contains(g, StringComparison.OrdinalIgnoreCase)))
                    return null;
                return abs.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sito non letto '{site}': {ex.Message}");
                return null;
            }
        }

        private static async Task<(byte[] Bytes, string MimeType, string Extension)?> DownloadAsync(string url)
        {
            try
            {
                using var response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Immagine non scaricata ({(int)response.StatusCode}): {url}");
                    return null;
                }

                var mime = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Non è un'immagine ({mime}): {url}");
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0 || bytes.Length > MaxBytes)
                {
                    Console.WriteLine($"Immagine vuota o troppo grande ({bytes.Length / 1024} KB): {url}");
                    return null;
                }

                // solo i formati che l'app sa mostrare (niente avif, svg, ico...)
                string? extension = mime.ToLowerInvariant() switch
                {
                    "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    "image/gif" => ".gif",
                    _ => null
                };
                if (extension is null)
                {
                    Console.WriteLine($"Formato immagine non supportato ({mime}): {url}");
                    return null;
                }
                return (bytes, mime, extension);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore download immagine '{url}': {ex.Message}");
                return null;
            }
        }
    }
}
