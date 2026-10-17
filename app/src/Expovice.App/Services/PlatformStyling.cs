using Microsoft.Maui.Handlers;
#if ANDROID
using Android.Content.Res;
using Microsoft.Maui.Platform;
#endif

namespace Expovice.App.Services;

/// <summary>
/// Strips the native underline/border from Entry, Editor and Picker: the design draws its own pill (.input) around them.
/// </summary>
public static class PlatformStyling
{
    public static void RemoveNativeInputChrome()
    {
        EntryHandler.Mapper.AppendToMapping("ExpoviceNoChrome", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Colors.Transparent.ToPlatform());
#elif IOS
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });
        EditorHandler.Mapper.AppendToMapping("ExpoviceNoChrome", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Colors.Transparent.ToPlatform());
#endif
        });
        PickerHandler.Mapper.AppendToMapping("ExpoviceNoChrome", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = ColorStateList.ValueOf(Colors.Transparent.ToPlatform());
#elif IOS
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });
    }
}
