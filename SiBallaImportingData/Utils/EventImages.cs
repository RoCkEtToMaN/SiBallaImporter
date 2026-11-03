using SiBalla.Service;
using SiBallaImportingData.Model;
using System.Security.Cryptography;
using System.Text;

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

            int caricate = 0, fallite = 0;
            foreach (var ev in conImmagine)
            {
                var url = ev.EventDetail.ImageUrl!;
                var image = await DownloadAsync(url);
                if (image is null)
                {
                    fallite++;
                    continue;
                }

                var fileId = FileId(ev);
                if (await appWrite.UploadImageAsync(fileId, image.Value.Bytes, fileId + image.Value.Extension, image.Value.MimeType))
                {
                    ev.EventDetail.HasImage = true;
                    caricate++;
                }
                else
                {
                    fallite++;
                    Console.WriteLine($"   immagine di '{ev.PartyName?.Trim()}' ({ev.DateStart:yyyy-MM-dd}): {url}");
                }
            }
            Console.WriteLine($"🖼️ Immagini caricate su Storage: {caricate}, non riuscite: {fallite}");
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
            var ids = await appWrite.GetImageIdsAsync();
            int mancanti = 0;
            foreach (var ev in conImmagine)
            {
                var fileId = FileId(ev);
                if (ids is not null && !ids.Contains(fileId))
                {
                    mancanti++;
                    ev.EventDetail.HasImage = false;
                    Console.WriteLine($"⚠️ {ev.SheetName} riga {ev.SheetRow}: '{ImportData.ImageHeader}' = sì ma il file {fileId} non è su Storage (data o titolo cambiati?)");
                    continue;
                }
                ev.EventDetail.ImageUrl = AppWriteService.ImageViewUrl(fileId);
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
            var ids = await appWrite.GetImageIdsAsync() ?? new HashSet<string>();
            var celle = new List<(Event Event, string Value)>();
            foreach (var ev in daCercare.Where(e => ids.Contains(FileId(e))))
                celle.Add((ev, ImportData.ImageYes));
            Console.WriteLine($"🖼️ Già su Storage: {celle.Count}");

            // stesso sito su più righe (pagina dell'organizzatore): lo leggo una volta sola per giro
            var perSito = new Dictionary<string, (byte[] Bytes, string MimeType, string Extension)?>(StringComparer.OrdinalIgnoreCase);
            int caricate = 0, senza = 0;
            foreach (var ev in daCercare.Where(e => !ids.Contains(FileId(e))).Take(max))
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

            foreach (var (ev, value) in celle.Where(x => x.Value == ImportData.ImageYes))
                ev.EventDetail.HasImage = true;
            Console.WriteLine($"🖼️ Righe dello Sheet: {caricate} immagini caricate, {senza} senza immagine sul sito ({perSito.Count} siti letti)");
            await ImportData.UpdateSheetImageCellsAsync(celle);
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
