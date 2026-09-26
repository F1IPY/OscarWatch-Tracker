using Avalonia.Controls;
using Avalonia.Interactivity;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class Ft4Window : Window
{
    private bool _closeConfirmed;

    public Ft4Window()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is Ft4ViewModel vm)
            await vm.OnWindowOpenedAsync().ConfigureAwait(true);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
            return;
        if (DataContext is not Ft4ViewModel vm || !vm.IsTransmissionActive)
            return;

        e.Cancel = true;
        var l = LocalizationService.Instance;
        var stop = await SimpleConfirmDialog.ShowAsync(
            this,
            l.Get("Ft4.CloseWhileTx.Title"),
            l.Get("Ft4.CloseWhileTx.Message")).ConfigureAwait(true);
        if (!stop)
            return;

        vm.StopTransmissionNow();
        _closeConfirmed = true;
        Close();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is Ft4ViewModel vm)
            await vm.OnWindowClosedAsync().ConfigureAwait(true);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        var dialog = new Ft4SettingsWindow { DataContext = vm };
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }
}
