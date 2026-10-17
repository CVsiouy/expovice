using CommunityToolkit.Mvvm.ComponentModel;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Core.ViewModels;

/// <summary>
/// Base for every screen. View models are UI-thread-affine (not thread-safe): commands run on the UI thread and
/// awaits resume there, so observable properties can be set directly.
/// </summary>
public abstract partial class ViewModelBase(ILogger logger, IDialogService dialogs) : ObservableObject
{
    public const string OfflineMessage = "Can't reach Expovice. Check your connection and try again.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    protected ILogger Logger { get; } = logger;

    protected IDialogService Dialogs { get; } = dialogs;

    /// <summary>Receives navigation parameters (the page forwards Shell's query attributes here).</summary>
    public virtual void ApplyParameters(IDictionary<string, object> parameters)
    {
    }

    public virtual Task OnAppearingAsync() => Task.CompletedTask;

    public virtual void OnDisappearing()
    {
    }

    /// <summary>
    /// Runs an action, turning API/network failures into a logged warning plus a user-facing message instead of a crash.
    /// </summary>
    /// <returns>true when the action completed.</returns>
    protected async Task<bool> RunSafeAsync(Func<Task> action, bool showBusy = true)
    {
        ErrorMessage = null;
        if (showBusy)
        {
            IsBusy = true;
        }

        try
        {
            await action();
            return true;
        }
        catch (ApiException ex)
        {
            Logger.LogWarning(ex, "API call failed with {Status}", ex.Status);
            ErrorMessage = ex.UserMessage;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Network failure");
            ErrorMessage = OfflineMessage;
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient timeouts surface as TaskCanceledException.
            Logger.LogWarning(ex, "Request timed out or was cancelled");
            ErrorMessage = OfflineMessage;
        }
        finally
        {
            if (showBusy)
            {
                IsBusy = false;
            }
        }

        await Dialogs.ToastAsync(ErrorMessage ?? OfflineMessage);
        return false;
    }
}
