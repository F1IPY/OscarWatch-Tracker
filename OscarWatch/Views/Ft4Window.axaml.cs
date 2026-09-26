using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class Ft4Window : Window
{
    private bool _closeConfirmed;
    private ScrollViewer? _decodeScroll;

    public Ft4Window()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        vm.Decodes.CollectionChanged += OnDecodeRowsChanged;
        await vm.OnWindowOpenedAsync().ConfigureAwait(true);
    }

    private void OnDecodeRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Newest lines are inserted at the top. The list keeps its scroll offset,
        // so each cycle appears above the viewport until the operator scrolls up.
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != 0)
            return;

        Dispatcher.UIThread.Post(ScrollDecodesToTop, DispatcherPriority.Background);
    }

    private void ScrollDecodesToTop()
    {
        if (DecodeList.ItemCount == 0)
            return;

        DecodeList.ScrollIntoView(0);
        _decodeScroll ??= DecodeList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_decodeScroll is not null)
            _decodeScroll.Offset = new Vector(_decodeScroll.Offset.X, 0);
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
        if (DataContext is not Ft4ViewModel vm)
            return;

        vm.Decodes.CollectionChanged -= OnDecodeRowsChanged;
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
