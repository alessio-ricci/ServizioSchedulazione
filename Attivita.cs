using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ServizioSchedulazione
{
    public record Attivita
    {
        public long RecordId { get; set; }
        public string? IdTask { get; set; }
        public string? EventId { get; set; }
        public DateTime? DataCreazione { get; set; }
        public string? Nome { get; set; }
        public string? CodiceRitorno { get; set; }
        public string? NomeAction { get; set; }
        
    }
}
