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

    /// <summary>Set for a shaded receive line when the operator chose a text colour. Otherwise the row keeps the theme text.</summary>
    [ObservableProperty] private IBrush? _rowForeground;

    public bool HasRowForeground => RowForeground is not null;

    partial void OnRowForegroundChanged(IBrush? value) => OnPropertyChanged(nameof(HasRowForeground));

    private string? _appliedColourHex;
    private string? _appliedTextHex;
    private bool _highlightApplied;

    public void RefreshHighlight(
        string? myCall,
        string? partnerCall,
        string callingMeColour,
        string replyingColour,
        string newCallColour,
        string newGridColour,
        string cqColour,
        IReadOnlySet<string>? workedCalls,
        IReadOnlySet<string>? workedGridFields,
        string? callingMeText = null,
        string? replyingText = null,
        string? newCallText = null,
        string? newGridText = null,
        string? cqText = null)
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
            Ft4DecodeHighlightKind.Cq => cqColour,
            _ => null
        };
        var textHex = kind switch
        {
            Ft4DecodeHighlightKind.Replying => Ft4DecodeHighlight.NormalizeColour(replyingText),
            Ft4DecodeHighlightKind.CallingMe => Ft4DecodeHighlight.NormalizeColour(callingMeText),
            Ft4DecodeHighlightKind.NewCall => Ft4DecodeHighlight.NormalizeColour(newCallText),
            Ft4DecodeHighlightKind.NewGrid => Ft4DecodeHighlight.NormalizeColour(newGridText),
            Ft4DecodeHighlightKind.Cq => Ft4DecodeHighlight.NormalizeColour(cqText),
            _ => null
        };

        // Replacing the brush when the colour has not changed repaints every row.
        if (_highlightApplied
            && string.Equals(_appliedColourHex, hex, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_appliedTextHex, textHex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _highlightApplied = true;
        _appliedColourHex = hex;
        _appliedTextHex = textHex;
        RowBackground = Ft4DecodeRowBackgroundConverter.BrushFromHex(hex);
        RowForeground = textHex is null ? null : Ft4DecodeRowBackgroundConverter.BrushFromHex(textHex);
    }
}
