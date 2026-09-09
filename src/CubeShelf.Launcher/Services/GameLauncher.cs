namespace CubeShelf.Launcher.Services;

public sealed class GameLauncher
{
    private readonly GameDefinition _game;
    private readonly ModManager? _mods;

    public GameLauncher(
        GameDefinition game,
        ModManager? mods)
    {
        _game = game;
        _mods = mods;
    }

    public Process StartGame()
    {
        var exe = _game.ExecutableFullPath;

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException(
                $"L'exécutable de « {_game.Title} » n'est pas configuré.",
                exe);
        }

        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false
        };

        if (_mods is not null)
        {
            var activeList = _mods.WriteActiveList();

            psi.Environment["PARTYBOARD_MOD_LIST"] =
                Path.GetFullPath(activeList);
        }

        if (!string.IsNullOrWhiteSpace(_game.GameRootFullPath))
        {
            psi.Environment["PARTYBOARD_GAME_ROOT"] =
                _game.GameRootFullPath;
        }

        if (_game.HasDiscImage)
        {
            // PartyBoard can consume this variable once its file-selection path
            // is wired to CubeShelf. The launcher already persists ISO/RVZ.
            psi.Environment["PARTYBOARD_DISC_IMAGE"] =
                _game.DiscImageFullPath;
        }

        return Process.Start(psi) ??
            throw new InvalidOperationException("Impossible de démarrer PartyBoard.");
    }
}
