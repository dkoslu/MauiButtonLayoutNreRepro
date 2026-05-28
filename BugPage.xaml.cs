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

        // Heavy native view in Row 0 — WKWebView + autoplaying <video> keeps
        // a long-lived native object graph that delays page disposal, widening
        // the CA-transaction race window enough to surface the Button NRE.
        VideoView.Source = new HtmlWebViewSource
        {
            Html = """
                <!doctype html>
                <html><head><meta name='viewport' content='width=device-width,initial-scale=1'>
                <style>
                  html,body{margin:0;padding:0;background:#1a1a2e;color:#eee;font-family:-apple-system;}
                  video{width:100vw;height:100vh;object-fit:cover;}
                  .overlay{position:fixed;top:0;left:0;right:0;padding:16px;
                    background:linear-gradient(#000c,transparent);font-size:14px;text-align:center;}
                </style></head>
                <body>
                  <video autoplay muted loop playsinline
                    src='https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4'></video>
                  <div class='overlay'>Tap "Dismiss" below to trigger the crash.</div>
                </body></html>
                """
        };
    }

    private async void OnDismissClicked(object? sender, EventArgs e)
    {
        // Mirror production: the trigger fires on a worker thread (WebRTC
        // state change), then marshals back to UI via MainThread.InvokeOn
        // MainThreadAsync. That puts the IsVisible mutation + GoToAsync on
        // a fresh main-loop tick — a different CA-transaction than the
        // touch handler ran in — which is the race window the bug needs.
        await Task.Run(async () =>
        {
            await Task.Delay(50);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                FlipButton.IsVisible = false;

                // WORKAROUND — uncomment to prevent the crash:
                // if (ButtonRow.Parent is Microsoft.Maui.Controls.Grid parentGrid)
                //     parentGrid.Remove(ButtonRow);

                try { await Shell.Current.GoToAsync(".."); } catch { }
            });
        });
    }

    // Stubs — present to match the real-world button layout that triggers the bug.
    private void OnMuteClicked(object? sender, EventArgs e) { }
    private void OnFlipClicked(object? sender, EventArgs e) { }
}
