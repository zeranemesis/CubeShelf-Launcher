namespace CubeShelf.Assistance;

/// <summary>An in-memory RGB snapshot. Never stored in the player's profile.</summary>
public sealed record GameFrame(int Width, int Height, byte[] Rgb)
{
    public void Validate()
    {
        if (Width is < 1 or > 1280 || Height is < 1 or > 1280 || Rgb is null || Rgb.Length != Width * Height * 3)
            throw new InvalidDataException("Capture du jeu invalide.");
    }
}
