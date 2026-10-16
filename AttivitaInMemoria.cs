namespace ServizioSchedulazione
{
    public class AttivitaInMemoria
    {
        public string? Server { get; set; }
        public string? IdTask { get; set; }
        public string? Nome { get; set; }
        public DateTime? DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public string? CodiceRitorno { get; set; }
        public bool AggiornataSuDB { get; set; } = false;
        public bool Terminata => DataFine.HasValue && CodiceRitorno != null;
    }
}
