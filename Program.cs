namespace ServizioSchedulazione
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configura l'app per girare come Windows Service e imposta il nome
            //builder.Services.AddWindowsService(options =>
            //{
            //    options.ServiceName = "MioServizioNET8";
            //});


            builder.Services.AddHostedService<Worker>();

            var host = builder.Build();
            host.Run();
        }
    }
}