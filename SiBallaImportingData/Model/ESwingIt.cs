using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public string? Location { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public string Styles { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
