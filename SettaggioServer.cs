using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServizioSchedulazione
{
    public class SettaggioServer
    {
        public string UserName { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Host { get; set; } = ".";
        public string NomeLog { get; set; } = "Microsoft-Windows-TaskScheduler/Operational";
        public string FiltroPath { get; set; } = "\\MieiTest\\";
        public string Password { get; set; } = "";
    }
}
