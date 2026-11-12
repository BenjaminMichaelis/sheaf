using Avalonia;
using Avalonia.iOS;

using Foundation;

namespace Sheaf.Mobile.iOS;

// AvaloniaAppDelegate initializes App and dynamically selects Avalonia's built-in scene delegate.
// Info.plist enables scenes; the scene delegate owns the window and forwards URL/activity activation.
[Register("AppDelegate")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "AppDelegate is the iOS-required name for the UIApplicationDelegate.")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
