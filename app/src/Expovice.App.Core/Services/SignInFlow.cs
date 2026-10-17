using Expovice.App.Core.Abstractions;

namespace Expovice.App.Core.Services;

/// <summary>Where to go once any sign-in method (OTP, Google, Apple, restored session) succeeds. One place, so all paths agree.</summary>
public sealed class SignInFlow(UserContext user, INavigationService navigation)
{
    public async Task CompleteAsync(bool isNewUser, bool fromSplash = false)
    {
        await user.LoadAsync();
        if (isNewUser || string.IsNullOrWhiteSpace(user.Me?.Name))
        {
            // From splash there is no sign-in stack yet, so build //signin/name.
            await navigation.GoToAsync(fromSplash ? $"{Routes.SignIn}/{Routes.Name}" : Routes.Name);
            return;
        }

        await navigation.GoToAsync(Routes.Home);
    }
}
