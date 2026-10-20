using Microsoft.Extensions.Hosting.WindowsServices;

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

            var host = builder.Build();
            host.Run();
        }
    }
}