using Android.App.Roles;
using Android.Content;
using Android.Provider;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace CubeShelf.Android;

public sealed partial class MainActivity
{
    private const string AndroidHomePromptAnswered = "android_home_prompt_answered";
    private AlertDialog? _androidHomePrompt;

    private void OfferAndroidHome()
    {
        if (_androidHomePrompt is not null || IsFinishing || IsDestroyed) return;
        var preferences = GetPreferences(FileCreationMode.Private);
        if (preferences is null || preferences.GetBoolean(AndroidHomePromptAnswered, false)) return;

        void RememberAnswer() => preferences.Edit()?.PutBoolean(AndroidHomePromptAnswered, true)?.Apply();

        if (OperatingSystem.IsAndroidVersionAtLeast(29)
            && GetSystemService(RoleService) is RoleManager roles && roles.IsRoleHeld(RoleManager.RoleHome))
        {
            RememberAnswer();
            return;
        }

        using var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Ouvrir DecompShelf au démarrage ?");
        builder.SetMessage("Choisis DecompShelf comme accueil Android pour afficher ta bibliothèque au démarrage et quand tu appuies sur Accueil. Tu peux revenir à ton accueil actuel dans les paramètres.");
        builder.SetNegativeButton("Garder mon accueil", (_, _) => RememberAnswer());
        builder.SetPositiveButton("Choisir DecompShelf", (_, _) =>
        {
            RememberAnswer();
            RequestAndroidHome();
        });
        var prompt = builder.Create();
        prompt.CancelEvent += (_, _) => RememberAnswer();
        prompt.DismissEvent += (_, _) => _androidHomePrompt = null;
        _androidHomePrompt = prompt;
        prompt.Show();
    }

    private void RequestAndroidHome()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29)
            && GetSystemService(RoleService) is RoleManager roles && roles.IsRoleAvailable(RoleManager.RoleHome))
        {
            try
            {
                StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleHome), 41);
                return;
            }
            catch (ActivityNotFoundException) { }
        }
        OpenAndroidHomeSettings();
    }

    private void OpenAndroidHomeSettings()
    {
        try { StartActivity(new Intent(Settings.ActionHomeSettings).AddFlags(ActivityFlags.NewTask)); }
        catch (ActivityNotFoundException) { StartActivity(new Intent(Settings.ActionSettings).AddFlags(ActivityFlags.NewTask)); }
    }
}
