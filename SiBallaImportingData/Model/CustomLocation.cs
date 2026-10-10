using System.Text.Json.Serialization;

namespace SiBallaImportingData.Models
{
    public class CustomLocation
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("countryCode")]
        public string CountryCode { get; set; } = string.Empty;

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }
}