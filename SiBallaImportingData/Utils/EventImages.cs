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

                var extension = mime.ToLowerInvariant() switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    "image/gif" => ".gif",
                    _ => ".jpg"
                };
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
