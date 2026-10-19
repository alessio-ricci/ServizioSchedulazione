namespace ServizioSchedulazione
{
    public class AttivitaInMemoria
    {
        public string? Server { get; set; }
        
        public string? IdTask { get; set; }

        //Nome Action (ad esempio app.exe)
        public string? NomeAction { get; set; }

        //Nome Action completo del path
        public string? Action { get; set; }

        //Nome task lo becchi e basta
        public string? NomeTask { get; set; }

        public DateTime? DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public string? CodiceRitorno { get; set; }
        public bool AggiornataSuDB { get; set; } = false;
        public bool Terminata => DataFine.HasValue && CodiceRitorno != null;
    }
}
