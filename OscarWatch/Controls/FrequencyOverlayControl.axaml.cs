using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Threading;
using OscarWatch.ViewModels;

namespace OscarWatch.Controls;

public partial class FrequencyOverlayControl : UserControl
{
  private const double EdgeMarginPx = 8;

  private bool _isDragging;
  private IPointer? _dragPointer;
  private Point _dragStartPointer;
  private double _dragStartX;
  private double _dragStartY;
  private FrequencyOverlayViewModel? _subscribedVm;
  private Control? _mapHost;
  private WorldMapControl? _worldMap;
  private Window? _hostWindow;
  private int _ensureVisibleGeneration;

  public FrequencyOverlayControl()
  {
    InitializeComponent();
    PointerMoved += OnOverlayPointerMoved;
    PointerReleased += OnOverlayPointerReleased;
    PointerCaptureLost += OnOverlayPointerCaptureLost;
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    var handle = this.FindControl<Control>("DragHandle");
    if (handle is not null)
      handle.PointerPressed += OnDragHandlePointerPressed;

    SubscribeViewModel(DataContext as FrequencyOverlayViewModel);
    BindMapHosts();
    if (_hostWindow is not null)
    {
      _hostWindow.Resized += OnHostWindowResized;
      _hostWindow.PropertyChanged += OnHostWindowPropertyChanged;
      _hostWindow.Deactivated += OnHostWindowDeactivated;
      _hostWindow.PointerReleased += OnHostWindowPointerReleased;
    }

    ScheduleEnsureVisiblePosition();
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    if (_hostWindow is not null)
    {
      _hostWindow.Resized -= OnHostWindowResized;
      _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
      _hostWindow.Deactivated -= OnHostWindowDeactivated;
      _hostWindow.PointerReleased -= OnHostWindowPointerReleased;
    }

    EndDrag();

    UnbindMapHosts();
    _hostWindow = null;
    SubscribeViewModel(null);
    base.OnDetachedFromVisualTree(e);
  }

  protected override void OnDataContextChanged(EventArgs e)
  {
    base.OnDataContextChanged(e);
    SubscribeViewModel(DataContext as FrequencyOverlayViewModel);
    ScheduleEnsureVisiblePosition();
  }

  protected override void OnSizeChanged(SizeChangedEventArgs e)
  {
    base.OnSizeChanged(e);
    ScheduleEnsureVisiblePosition();
  }

  private void BindMapHosts()
  {
    UnbindMapHosts();
    // Parent is MapColumn; WorldMap lives in nested MapHost (not a sibling of this control).
    _mapHost = Parent as Control;

    if (_mapHost is Panel column)
    {
      foreach (var child in column.Children)
      {
        if (child is not Panel mapHost)
          continue;

        foreach (var nested in mapHost.Children)
        {
          if (nested is WorldMapControl map)
          {
            _worldMap = map;
            break;
          }
        }

        if (_worldMap is not null)
          break;
      }
    }

    if (_mapHost is not null)
      _mapHost.SizeChanged += OnMapHostSizeChanged;
    if (_worldMap is not null)
      _worldMap.SizeChanged += OnMapHostSizeChanged;

    _hostWindow = TopLevel.GetTopLevel(this) as Window;
  }

  private void UnbindMapHosts()
  {
    if (_mapHost is not null)
      _mapHost.SizeChanged -= OnMapHostSizeChanged;
    if (_worldMap is not null)
      _worldMap.SizeChanged -= OnMapHostSizeChanged;
    _mapHost = null;
    _worldMap = null;
  }

  private void OnHostWindowResized(object? sender, WindowResizedEventArgs e) =>
    ScheduleEnsureVisiblePosition();

  private void OnHostWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
  {
    if (e.Property == Window.WindowStateProperty)
      ScheduleEnsureVisiblePosition();
  }

  private void OnMapHostSizeChanged(object? sender, SizeChangedEventArgs e) =>
    ScheduleEnsureVisiblePosition();

  private void SubscribeViewModel(FrequencyOverlayViewModel? vm)
  {
    if (_subscribedVm == vm)
      return;

    if (_subscribedVm is not null)
    {
      _subscribedVm.OverlayLayoutChanged -= OnOverlayLayoutChanged;
      _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    _subscribedVm = vm;
    if (vm is not null)
    {
      vm.OverlayLayoutChanged += OnOverlayLayoutChanged;
      vm.PropertyChanged += OnViewModelPropertyChanged;
    }
  }

  private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(FrequencyOverlayViewModel.IsCollapsed)
        or nameof(FrequencyOverlayViewModel.OverlayMinWidth)
        or nameof(FrequencyOverlayViewModel.OverlayMaxWidth))
      ScheduleEnsureVisiblePosition();
  }

  private void OnOverlayLayoutChanged(object? sender, EventArgs e) =>
    ScheduleEnsureVisiblePosition();

  /// <summary>Run after layout and again after render (maximize needs the second pass).</summary>
  private void ScheduleEnsureVisiblePosition()
  {
    var generation = Interlocked.Increment(ref _ensureVisibleGeneration);

    void RunIfCurrent()
    {
      if (generation != _ensureVisibleGeneration)
        return;
      EnsureVisiblePosition();
    }

    Dispatcher.UIThread.Post(RunIfCurrent, DispatcherPriority.Loaded);
    Dispatcher.UIThread.Post(RunIfCurrent, DispatcherPriority.Render);
  }

  private Visual? GetCoordinateHost() => _mapHost;

  private bool TryGetHostSize(out double width, out double height)
  {
    width = 0;
    height = 0;

    if (_mapHost is not { Bounds.Width: > 0, Bounds.Height: > 0 } host)
      return false;

    width = host.Bounds.Width;
    height = host.Bounds.Height;
    return true;
  }

  /// <summary>
  /// Drag limits must use arranged <see cref="Visual.Bounds"/>, not <see cref="Layoutable.DesiredSize"/>.
  /// DesiredSize is often larger than the laid-out control and creates a dead zone at the bottom of the map.
  /// </summary>
  private Size MeasureOverlaySizeForClamp()
  {
    if (Bounds.Width > 0 && Bounds.Height > 0)
      return Bounds.Size;

    var vm = DataContext as FrequencyOverlayViewModel;
    var minW = vm?.OverlayMinWidth ?? 380;
    var collapsed = vm?.IsCollapsed == true;
    var w = DesiredSize.Width > 0 ? DesiredSize.Width : (collapsed ? 320 : 380);
    var h = DesiredSize.Height > 0 ? DesiredSize.Height : (collapsed ? 44 : 280);
    return new Size(Math.Max(w, minW), h);
  }

  private void EnsureVisiblePosition()
  {
    if (_isDragging || DataContext is not FrequencyOverlayViewModel vm)
      return;

    if (!TryGetHostSize(out var hostWidth, out var hostHeight))
      return;

    var overlay = MeasureOverlaySizeForClamp();
    vm.EnsureOverlayWithinHost(
      hostWidth,
      hostHeight,
      overlay.Width,
      overlay.Height);
  }

  private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (DataContext is not FrequencyOverlayViewModel vm)
      return;

    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      return;

    if (e.Source is Visual source)
    {
      if (CollapseToggle is not null && IsDescendantOf(source, CollapseToggle))
        return;
      if (OperatingStyleHeader is not null && IsDescendantOf(source, OperatingStyleHeader))
        return;
      if (DopplerStrategyRow is not null && IsDescendantOf(source, DopplerStrategyRow))
        return;
    }

    var host = GetCoordinateHost();
    if (host is null)
      return;

    _isDragging = true;
    _dragPointer = e.Pointer;
    _dragStartPointer = e.GetPosition(host);
    _dragStartX = vm.OverlayX;
    _dragStartY = vm.OverlayY;
    e.Pointer.Capture(this);
    e.Handled = true;
  }

  private void OnHostWindowDeactivated(object? sender, EventArgs e) =>
    EndDrag(persistPosition: true);

  private void OnHostWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (_isDragging)
      EndDrag(persistPosition: true);
  }

  private void OnCollapseToggleTapped(object? sender, TappedEventArgs e)
  {
    if (DataContext is FrequencyOverlayViewModel vm)
      vm.ToggleCollapseCommand.Execute(null);
    e.Handled = true;
  }

  private static bool IsDescendantOf(Visual? node, Visual ancestor)
  {
    while (node is not null)
    {
      if (ReferenceEquals(node, ancestor))
        return true;
      node = node.GetVisualParent() as Visual;
    }

    return false;
  }

  private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
  {
    if (!_isDragging || DataContext is not FrequencyOverlayViewModel vm)
      return;

    if (!TryGetHostSize(out var hostWidth, out var hostHeight))
      return;

    var host = GetCoordinateHost();
    if (host is null)
      return;

    var pos = e.GetPosition(host);
    var delta = pos - _dragStartPointer;
    var overlay = MeasureOverlaySizeForClamp();
    var (newX, newY) = ClampPosition(
      _dragStartX + delta.X,
      _dragStartY + delta.Y,
      hostWidth,
      hostHeight,
      overlay.Width,
      overlay.Height);
    vm.SetOverlayPosition(newX, newY);
    e.Handled = true;
  }

  private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e) =>
    EndDrag(persistPosition: true);

  private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
    EndDrag();

  /// <summary>
  /// Release pointer capture from frequency-panel drags. Stale capture can steal wheel input
  /// from other apps while OscarWatch stays visible in the background.
  /// </summary>
  private void EndDrag(bool persistPosition = false)
  {
    var wasDragging = _isDragging;
    _isDragging = false;
    _dragPointer?.Capture(null);
    _dragPointer = null;

    if (!wasDragging || !persistPosition || DataContext is not FrequencyOverlayViewModel vm)
      return;

    EnsureVisiblePosition();
    vm.PersistOverlayPosition();
    ScheduleEnsureVisiblePosition();
  }

  private void OnOffsetSpinValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
  {
    if (DataContext is not FrequencyOverlayViewModel vm || sender is not NumericUpDown spin)
      return;

    var khz = (double)(spin.Value ?? 0m);
    if (Math.Abs(vm.ActiveOffsetKHz - khz) < 0.0001)
      return;
    vm.ActiveOffsetKHz = khz;
  }

  private void OnOffsetStepClick(object? sender, RoutedEventArgs e)
  {
    if (DataContext is not FrequencyOverlayViewModel vm || sender is not Button btn)
      return;

    if (btn.Tag is string tag && int.TryParse(tag, out var deltaHz))
      vm.AdjustActiveOffsetHz(deltaHz);
  }

  private void OnPassbandTuneSliderWheelChanged(object? sender, PointerWheelEventArgs e)
  {
    if (DataContext is not FrequencyOverlayViewModel vm)
      return;

    var deltaHz = e.Delta.Y != 0 ? (int)Math.Round(e.Delta.Y * 100) : 0;
    if (deltaHz == 0)
      return;

    vm.AdjustPassbandTuneOffsetHz(deltaHz);
    e.Handled = true;
  }

  private void OnPassbandTuneResetClick(object? sender, RoutedEventArgs e)
  {
    if (DataContext is FrequencyOverlayViewModel vm)
      vm.ResetPassbandTuneOffset();
  }

  private static (double X, double Y) ClampPosition(
    double x,
    double y,
    double hostWidth,
    double hostHeight,
    double overlayWidth,
    double overlayHeight)
  {
    var maxX = Math.Max(EdgeMarginPx, hostWidth - overlayWidth - EdgeMarginPx);
    var maxY = Math.Max(EdgeMarginPx, hostHeight - overlayHeight - EdgeMarginPx);
    return (Math.Clamp(x, EdgeMarginPx, maxX), Math.Clamp(y, EdgeMarginPx, maxY));
  }
}
