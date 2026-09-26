using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OscarWatch.Core.Ft4;
using OscarWatch.Ft4;

namespace OscarWatch.ViewModels;

/// <summary>One decode list row, with a background that tracks the current QSO partner.</summary>
public sealed partial class Ft4DecodeRowViewModel : ObservableObject
{
    public Ft4DecodeRowViewModel(Ft4DecodedMessage message)
    {
        Message = message;
    }

    public Ft4DecodedMessage Message { get; }

    [ObservableProperty] private IBrush _rowBackground = Brushes.Transparent;

    private string? _appliedColourHex;
    private bool _highlightApplied;

    public void RefreshHighlight(
        string? myCall,
        string? partnerCall,
        string callingMeColour,
        string replyingColour,
        string newCallColour,
        string newGridColour,
        IReadOnlySet<string>? workedCalls,
        IReadOnlySet<string>? workedGridFields)
    {
        var kind = Ft4DecodeHighlight.Classify(
            Message,
            myCall,
            partnerCall,
            workedCalls,
            workedGridFields);
        var hex = kind switch
        {
            Ft4DecodeHighlightKind.Replying => replyingColour,
            Ft4DecodeHighlightKind.CallingMe => callingMeColour,
            Ft4DecodeHighlightKind.NewCall => newCallColour,
            Ft4DecodeHighlightKind.NewGrid => newGridColour,
            _ => null
        };

        // Replacing the brush when the colour has not changed repaints every row.
        if (_highlightApplied && string.Equals(_appliedColourHex, hex, StringComparison.OrdinalIgnoreCase))
            return;

        _highlightApplied = true;
        _appliedColourHex = hex;
        RowBackground = Ft4DecodeRowBackgroundConverter.BrushFromHex(hex);
    }
}
