using System;
using Velopack;

namespace SubtitlesFixer.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Handling Velopack hooks (install/update/uninstall)
            VelopackApp.Build().Run();

            // Native WPF startup
            var app = new App();
            app.InitializeComponent();
            
            var mainWindow = new MainWindow();
            app.Run(mainWindow);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Aplicatia nu a putut porni:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Eroare Critica la Pornire",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
