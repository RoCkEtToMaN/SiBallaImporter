using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiBallaImportingData.Model
{
    public class ExcelTemplate
    {
        public int Id { get; set; }
        public DateTime Inizio { get; set; }
        public DateTime Fine { get; set; }
        public string? Giorni { get; set; }
        public int N { get; set; }
        public string? NomeEvento { get; set; }
        public string? Luogo { get; set; }
        public List<StileEvento>? Stile { get; set; }
        public string? Organizzatore { get; set; }
        public string? Riferimento { get; set; }
        public int Ordine { get; set; }
        public bool Gratis { get; set; }
      
    }

    public enum TipoEvento
    {
        Serate,
        Workshop
    }
    public enum StileEvento
    {
        Lindy,
        Blues,
        Balboa,
        Boogie,
        Shag,
        SoloJazz,
        Aerials,
        Afrodance
    }

    public class Eventi: ExcelTemplate
    {

    }
    public class Workshop: ExcelTemplate
    {

    }
}
