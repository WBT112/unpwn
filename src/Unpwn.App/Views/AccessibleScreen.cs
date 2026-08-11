using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace Unpwn.App.Views;

/// <summary>
/// Provides a consistent focus contract for navigated screens without moving
/// recovery logic into code-behind. Mark the preferred entry control with the
/// <c>initial-focus</c> class and validation summaries with
/// <c>focus-on-visible</c>.
/// </summary>
public class AccessibleScreen : UserControl
{
    private readonly List<Control> _visibilityFocusTargets = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        foreach (var target in this.GetLogicalDescendants()
                     .OfType<Control>()
                     .Where(control => control.Classes.Contains("focus-on-visible")))
        {
            target.PropertyChanged += FocusTarget_OnPropertyChanged;
            _visibilityFocusTargets.Add(target);
        }

        Dispatcher.UIThread.Post(FocusBestEntryTarget, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var target in _visibilityFocusTargets)
        {
            target.PropertyChanged -= FocusTarget_OnPropertyChanged;
        }

        _visibilityFocusTargets.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void FocusTarget_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty &&
            sender is Control { IsVisible: true, IsEnabled: true } target)
        {
            Dispatcher.UIThread.Post(
                () => target.Focus(NavigationMethod.Tab),
                DispatcherPriority.Loaded);
        }
    }

    private void FocusBestEntryTarget()
    {
        var controls = this.GetLogicalDescendants().OfType<Control>();
        var target = controls.FirstOrDefault(control =>
                         control.Classes.Contains("focus-on-visible") &&
                         control.IsVisible &&
                         control.IsEnabled) ??
                     controls.FirstOrDefault(control =>
                         control.Classes.Contains("initial-focus") &&
                         control.IsVisible &&
                         control.IsEnabled);

        target?.Focus(NavigationMethod.Tab);
    }
}
