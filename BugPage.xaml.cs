namespace MauiButtonLayoutNreRepro;

/// <summary>
/// Reproduces: NullReferenceException in Microsoft.Maui.Controls.Button.LayoutButton
/// during Shell GoToAsync navigation on iOS (net9.0-ios, MAUI 9.0.x).
///
/// Crash path (from Sentry + native crash dump):
///   Native:   CA::Transaction::flush_as_runloop_observer
///               -> WrapperView.LayoutSubviews
///                 -> xamarin_process_managed_exception_gchandle
///   Managed:  System.NullReferenceException: Object reference not set to an instance of an object.
///               at Microsoft.Maui.Controls.Button.LayoutButton(...)
///
/// Root cause: Shell's outgoing navigation animation keeps the page's native views
/// in the iOS layout tree for the duration of the slide-out transition. At the next
/// CA transaction flush the QuartzCore run-loop observer fires LayoutSubviews on every
/// UIView still attached — including the WrapperViews that back each Button. By that
/// point MAUI has begun tearing down the page's managed object graph, leaving
/// Button.LayoutButton with at least one null reference it does not guard against.
///
/// Workaround (see OnDismissClicked): detach the button container from its parent
/// Grid before calling GoToAsync. This removes the buttons from the native UIView
/// hierarchy so the layout pass never reaches them.
/// </summary>
public partial class BugPage : ContentPage
{
    public BugPage()
    {
        InitializeComponent();
    }

    private async void OnDismissClicked(object? sender, EventArgs e)
    {
        // -----------------------------------------------------------------------
        // WORKAROUND — uncomment to prevent the crash:
        //
        // if (ButtonRow.Parent is Microsoft.Maui.Controls.Grid parentGrid)
        //     parentGrid.Remove(ButtonRow);
        //
        // Removing ButtonRow from the Grid before GoToAsync takes it out of the
        // iOS UIView hierarchy, so WrapperView.LayoutSubviews is never called on
        // the buttons during Shell's outgoing animation.
        // -----------------------------------------------------------------------

        // BUG: buttons are still attached here. Shell starts the slide-out
        // animation, and the next CA transaction flush calls LayoutSubviews on
        // their WrapperViews → Button.LayoutButton NRE → SIGABRT on device.
        await Shell.Current.GoToAsync("..");
    }

    // Stubs — present to match the real-world button layout that triggers the bug.
    private void OnMuteClicked(object? sender, EventArgs e) { }
    private void OnFlipClicked(object? sender, EventArgs e) { }
}
