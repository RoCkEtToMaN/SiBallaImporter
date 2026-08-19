using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SiBallaImportingData.Utils
{
    public class ZippoResponse
    {
        [JsonPropertyName("places")]
        public List<ZippoPlace> Places { get; set; }
    }

    public class ZippoPlace
    {
        [JsonPropertyName("place name")]
        public string PlaceName { get; set; }
    }
}
