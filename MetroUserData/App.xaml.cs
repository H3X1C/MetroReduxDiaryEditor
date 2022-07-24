using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace MetroUserData
{
    public partial class App : Application
    {
        public static IHost? AppHost { get; private set; }
        public static IConfigurationRoot configuration;

        public App()
        {
            // EmbeddedAppSettings 
            configuration = new ConfigurationBuilder().AddJsonStream(this.GetType().Assembly.GetManifestResourceStream("MetroUserData.appsettings.json")).Build();

            // Setup DI
            AppHost = Host.CreateDefaultBuilder().ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<MainWindow>();    // Singleton as only 1 instance of main window.
                services.AddSingleton(configuration);

                // Can add other forms as transiants
            }).Build();
        }

        // WPF entry point
        protected override async void OnStartup(StartupEventArgs e)
        {
            await AppHost!.StartAsync();

            var startupForm = AppHost.Services.GetRequiredService<MainWindow>();
            startupForm.Show();

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await AppHost!.StopAsync();
            base.OnExit(e);
        }
    }
}