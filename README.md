# MauiButtonLayoutNreRepro

Minimal reproduction of a `NullReferenceException` inside
`Microsoft.Maui.Controls.Button.LayoutButton` that crashes the app
with `SIGABRT` during `Shell.GoToAsync("..")` on physical iOS devices.

The managed NRE is thrown inside a CoreAnimation layout commit and
crosses the native boundary as a fatal `NSException`, so the process
aborts. The crash does **not** reproduce in the iOS Simulator — the
layout-pass timing is different there.

## Verified crash environment

| | |
|-|-|
| MAUI | 9.0.120 |
| .NET | 9 (SDK 10.0.201 host, iOS SDK pack `Microsoft.iOS.Sdk.net9.0_26.4/26.4.9015`) |
| Xcode | 26.5 |
| Device | iPhone 17e, iOS 26.5 |
| Configuration | Release (full AOT) |

Confirmed crash stack (top frames):

```
xamarin_process_managed_exception            ← managed NRE: Button.LayoutButton
Microsoft.Maui.Platform.WrapperView.LayoutSubviews
-[UIView layoutSublayersOfLayer:]
CA::Layer::perform_update_
CA::Layer::layout_and_display_if_needed
CA::Context::commit_transaction
CA::Transaction::flush_as_runloop_observer
_UIApplicationFlushCATransaction
```

## The repro recipe — three required ingredients

The bug only fires when **all three** of the following are present
together. Drop any one and the page tears down too cleanly to hit
the race.

1. **A heavy native disposable in the page body.** `BugPage` puts a
   `WebView` with an autoplaying remote `<video>` in Row 0. The
   WKWebView keeps a long-lived native object graph (separate
   WebContent process) that delays page disposal, widening the race
   window. In production this was a custom WebRTC video view.

2. **A `Button` layout-affecting property mutation immediately
   before the navigation.** `OnDismissClicked` sets
   `FlipButton.IsVisible = false`. The property change schedules a
   layout pass on the button's `WrapperView`; that pass is what
   eventually fires inside the Shell animation and trips the NRE.

3. **The mutation + `GoToAsync("..")` dispatched from a worker
   thread back to the UI thread via
   `MainThread.InvokeOnMainThreadAsync(async () => {...})`** — not
   from the touch handler's own tick. The marshal puts both
   operations on a fresh main-loop tick / `CATransaction`, which is
   the gap the bug needs. Running the same code inline in the
   click handler (i.e. without the worker-thread bounce) does
   **not** crash.

The production trigger is an event from a WebRTC engine
(`CallService.StateChanged`) firing on a worker thread, marshaled
back to UI inside an `async void` event handler. `BugPage` simulates
that with `Task.Run` + a 50 ms delay + `MainThread.InvokeOnMainThreadAsync`.

## Build and run on a physical iOS device

Prerequisites: macOS, .NET 9 SDK, Xcode 26+, `dotnet workload install maui`,
`brew install libimobiledevice` (for `idevice_id`, `idevicecrashreport`),
a developer-signed Apple ID with at least one valid provisioning
profile that matches `com.companyname.mauibuttonlayoutnrerepro` (a
wildcard team profile is fine).

### 1. Build for the device

```bash
dotnet build -f net9.0-ios -c Release \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:ValidateXcodeVersion=false \
  -p:CodesignKey="Apple Development: Your Name (TEAMID)" \
  -p:CodesignProvision="iOS Team Provisioning Profile: *"
```

Notes:

- `-p:RuntimeIdentifier=ios-arm64` is required — on Apple Silicon
  Macs the default RID is `iossimulator-arm64`, which builds for
  the simulator (and won't reproduce the bug).
- `-p:ValidateXcodeVersion=false` is required when your installed
  Xcode is newer than the version the iOS SDK pack expects (e.g.
  iOS SDK 26.4 vs. Xcode 26.5).

The output bundle is at
`bin/Release/net9.0-ios/ios-arm64/MauiButtonLayoutNreRepro.app`.

### 2. Install and launch

**Do not use `dotnet build -t:Run`.** The bundled `mlaunch` tool
talks to devices via the deprecated `usbmuxd` mux protocol, which
Apple's modern device tunnel no longer accepts on iOS 17+ paired
with Xcode 15+. It will hang at
`Please connect the device ':v2:udid=...'` even when the device is
clearly attached and `idevice_id -l` reports it. Use Apple's
CoreDevice CLI instead:

```bash
# Discover the device's CoreDevice identifier (different from the UDID)
xcrun devicectl list devices

# Install and launch
DEVICE=<id from previous step>
xcrun devicectl device install app --device "$DEVICE" \
  bin/Release/net9.0-ios/ios-arm64/MauiButtonLayoutNreRepro.app
xcrun devicectl device process launch --device "$DEVICE" \
  com.companyname.mauibuttonlayoutnrerepro
```

### 3. Trigger the crash

1. On the device, tap **Go to Bug Page**.
2. Wait a beat for the WebView's video to start playing.
3. Tap **Dismiss**.

The app aborts with `SIGABRT` during the Shell back-navigation.

### 4. Pull the crash report

```bash
mkdir -p /tmp/crashreports
idevicecrashreport /tmp/crashreports
ls /tmp/crashreports/Library/Logs/CrashReporter/MauiButtonLayoutNreRepro-*.ips
```

`idevicesyslog -p MauiButtonLayoutNreRepro` is **not** sufficient
on its own — the fatal `NSException` is posted under
SpringBoard/UIKit process names, so the per-process filter
swallows it.

## Workaround

Detach the button container from its parent before `GoToAsync` so
the buttons aren't in the UIView hierarchy when the outgoing
animation runs its layout pass. Uncomment the marked block in
[`BugPage.xaml.cs`](BugPage.xaml.cs):

```csharp
if (ButtonRow.Parent is Microsoft.Maui.Controls.Grid parentGrid)
    parentGrid.Remove(ButtonRow);
await Shell.Current.GoToAsync("..");
```

This is the fix shipped in the production source the repro was
extracted from.
