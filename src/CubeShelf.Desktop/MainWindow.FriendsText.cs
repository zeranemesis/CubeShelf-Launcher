using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// What the friends code in Core says, in the language of the screen, and the one place a change
/// to the friends list is made so that a file held by another program costs a message, not the app.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Core words its friend-code errors in French, its own language, and keeps doing so: they are
    /// also what its tests read. In English they are matched here; anything unmatched still says
    /// what matters, that the code is not usable, rather than showing French.
    /// </summary>
    private static readonly Dictionary<string, string> FriendCodeErrorsInEnglish = new(StringComparer.Ordinal)
    {
        ["Code ami vide."] = "The friend code is empty.",
        ["Ce n’est pas un code ami CubeShelf."] = "This is not a CubeShelf friend code.",
        ["Code ami illisible : le texte a été modifié ou tronqué."] = "Unreadable friend code: the text was changed or cut off.",
        ["Code ami incomplet : il manque des caractères à la fin."] = "Incomplete friend code: characters are missing at the end.",
        ["Code ami corrompu : la somme de contrôle ne correspond pas."] = "Damaged friend code: the checksum does not match.",
        ["Version de code ami non prise en charge."] = "This friend code version is not supported.",
        ["Code ami incohérent : la longueur annoncée ne correspond pas."] = "Inconsistent friend code: the stated length does not match.",
        ["Clé publique CubeShelf invalide."] = "The friend code holds an invalid CubeShelf key."
    };

    private string FriendCodeErrorText(string french)
    {
        if (!UiLocalization.IsEnglish(_preferences.Language)) return french;
        return FriendCodeErrorsInEnglish.TryGetValue(french, out var english)
            ? english
            : "This friend code cannot be used.";
    }

    private string FriendAddRefusalText(FriendAddRefusal refusal) => refusal switch
    {
        FriendAddRefusal.OwnCode => P7("Ce code ami est le tien.", "This friend code is your own."),
        FriendAddRefusal.AlreadyFriend => P7("Cet ami est déjà dans ta liste.", "This friend is already in your list."),
        FriendAddRefusal.Blocked => P7("Tu as bloqué cette personne. Débloque-la d’abord si c’est voulu.",
                                        "You blocked this person. Unblock them first if that is what you want."),
        _ => ""
    };

    /// <summary>
    /// Makes a change to the friends list. When friends.json cannot be written -- an antivirus or
    /// a backup holding it, a full disk -- this says so and returns false; nothing was changed.
    /// Without it the error reached the window's last-chance handler, which closes CubeShelf.
    /// </summary>
    private bool TryChangeFriends(Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowToastParity(P7("Amis", "Friends"),
                P7("La liste d’amis est utilisée par un autre programme (antivirus, sauvegarde…). Rien n’a changé : réessaie dans un instant.",
                   "The friends list is in use by another program (antivirus, backup…). Nothing changed: try again in a moment."));
            return false;
        }
    }
}
