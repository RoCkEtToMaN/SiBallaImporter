using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SiBallaImportingData.Model
{
    public class ESwingIt
    {
        public int Id { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public string? EventLink { get; set; } // LINK_EVENTO (indice 10), usato per recuperare l'immagine
        public string? Location { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public float? Latitude { get; set; }
        public float? Longitude { get; set; }
        public string? StylesString { get; set; }
        public string? Organizer { get; set; }
        //public EventDetail? Styles { get; set; }
        public TimeSpan Duration { get; set; }
        [JsonPropertyName("image")]
        public string ExternalImage { get; set; }
        [JsonPropertyName("local_image")]
        public string LocalImage { get; set; }

        // Proprietà di comodo per ottenere l'URL assoluto pronto al download
        public string FullImageUrl
        {
            get
            {
                // Dà la precedenza all'immagine in cache locale del sito
                if (!string.IsNullOrWhiteSpace(LocalImage))
                {
                    return LocalImage.StartsWith("http")
                        ? LocalImage
                        : $"https://e-swing.it{LocalImage}";
                }
                // Fallback sull'immagine esterna (es. Facebook)
                return ExternalImage ?? string.Empty;
            }
        }
    }
}
