using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace OpenTalkIt.Android;

[Activity(
    Label = "OpenTalkIt",
    Theme = "@style/OpenTalkIt.Theme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
