namespace CubeShelf.Desktop;

/// <summary>
/// What a user is told, once, before CubeShelf looks for other installations on its own: the
/// first-start wizard says it on a page of its own, and someone who passed the wizard before this
/// existed is told by a one-time message. The box stays ticked unless they untick it.
/// </summary>
internal static class MeshDiscoveryNotice
{
    public static string Title(bool english) => english ? "The CubeShelf network" : "Le réseau CubeShelf";

    public static string Body(bool english) => english
        ? "CubeShelf joins its network by itself, with no server: to find other CubeShelf installations it uses BitTorrent’s public, decentralised network as a meeting point, and installations on your local network pass each other ways in. Nothing is downloaded or shared that way, and nothing private goes through it: no pseudo, no identity, no friends. A friend code is still needed to see a friend."
        : "CubeShelf rejoint son réseau tout seul, sans serveur : pour trouver les autres CubeShelf, il se sert du réseau public et décentralisé de BitTorrent comme point de rendez-vous, et les CubeShelf de ton réseau local se passent leurs portes d’entrée. Rien n’est téléchargé ni partagé par là, et rien de privé n’y passe : ni pseudo, ni identité, ni amis. Un code ami reste nécessaire pour voir un ami.";

    public static string Costs(bool english) => english
        ? "What to know: the few BitTorrent nodes asked see your IP address and can tell a CubeShelf runs there. And on a company or school network, this traffic looks like BitTorrent to a firewall: it may be blocked or flagged. If that matters to you, untick the box; you can change it any time in Settings."
        : "À savoir : les quelques nœuds BitTorrent interrogés voient ton adresse IP et peuvent savoir qu’un CubeShelf tourne à cette adresse. Et sur un réseau d’entreprise ou d’école, ce trafic ressemble à du BitTorrent pour un pare-feu : il peut être bloqué ou signalé. Si ça compte pour toi, décoche la case ; tu peux la changer à tout moment dans Paramètres.";

    public static string Box(bool english) => english
        ? "Find other CubeShelf installations without a code (BitTorrent network and local network)"
        : "Trouver les autres CubeShelf sans code (réseau BitTorrent et réseau local)";
}
