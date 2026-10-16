namespace ServizioSchedulazione
{
    public class Worker : BackgroundService
    {
        private readonly IConfiguration _configurazione;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory, IConfiguration configurazione)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _configurazione = configurazione;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var connectionString = _configurazione.GetConnectionString("DefaultConnection");
            var server = _configurazione.GetSection("ServizioSchedulazione:Server").Get<List<SettaggioServer>>() ?? new();

            if (server.Count == 0)
            {
                _logger.LogError("Nessun server configurato in ServizioSchedulazione:Server.");
                return;
            }

            //Questo crea più istanze di MonitoraggioServer, tante quante sono i server dichiarati nel config
            var monitor = server.Select(s => new MonitoraggioServer(
                s, s.NomeLog, s.FiltroPath, _loggerFactory.CreateLogger($"MonitoraggioServer.{s.Nome}"), connectionString));


            //Questo ci permette di parallelizzare il lavoro.
            await Task.WhenAll(monitor.Select(m => m.EseguiAsync(stoppingToken)));
        }
    }
}
