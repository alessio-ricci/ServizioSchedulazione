using Serilog;

namespace ServizioSchedulazione
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                
                //Per leggere appsettings.json non verrebbe trovato.
                ContentRootPath = AppContext.BaseDirectory
            });

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            builder.Services.AddSerilog();

            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "ServizioSchedulazione";
            });

            //Log su Event Viewer (Registri di Windows > Applicazione).
            //Di default scrive solo Warning o superiore.
            builder.Logging.AddEventLog(settings =>
            {
                settings.SourceName = "ServizioSchedulazione";
            });

            builder.Services.AddHostedService<Worker>();

            try
            {
                var host = builder.Build();
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Il servizio si è interrotto in modo inaspettato.");
            }
            finally
            {
                // Assicura la scrittura di tutti i log rimanenti prima della chiusura
                Log.CloseAndFlush();
            }
        }
    }
}