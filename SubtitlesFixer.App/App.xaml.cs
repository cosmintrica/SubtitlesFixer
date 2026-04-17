using System;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace SubtitlesFixer.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        InitializeComponent();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Previne crash-uri neasteptate — afiseaza eroarea in loc sa inchida aplicatia
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowError(e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Current?.Dispatcher.BeginInvoke(() => ShowError(e.Exception));
    }

    private static void ShowError(Exception ex)
    {
        var msg = ex is AggregateException agg
            ? agg.InnerException?.Message ?? agg.Message
            : ex.Message;

        System.Windows.MessageBox.Show(
            $"A aparut o eroare neasteptata:\n\n{msg}\n\nAplicatia va continua sa ruleze.",
            "Subtitles Fixer — Eroare",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

}
