namespace CubeShelf.Assistance;

public sealed record GameHelpGuide(string Source, string Facts)
{
    public static GameHelpGuide? For(string game) => game.Equals("Super Mario 64", StringComparison.OrdinalIgnoreCase)
        ? new("Manuel Nintendo · Super Mario 64 (N64), pages 14–15",
            "Le château permet d’accéder à quinze parcours principaux et à des parcours supplémentaires. " +
            "On rejoint la plupart des parcours en sautant dans leurs tableaux. Certains accès sont différents. " +
            "Les portes marquées d’une étoile demandent un nombre déterminé d’étoiles. " +
            "Trouver davantage d’étoiles permet d’explorer de nouvelles zones du château. " +
            "Il n’est pas nécessaire d’obtenir chaque étoile avant de poursuivre. " +
            "Depuis le château ou ses abords, START permet de consulter les étoiles obtenues par parcours.")
        : null;
    public const string SourceUrl = "https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_SuperMario64_EN.pdf";
}
