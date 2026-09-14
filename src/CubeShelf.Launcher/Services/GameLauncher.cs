using CubeShelf.Core.Platform;

namespace CubeShelf.Launcher.Services;

public sealed class GameLauncher
{
    private readonly GameDefinition _game;
    private readonly ModManager? _mods;
    private readonly IProcessLauncher _processLauncher;

    public GameLauncher(
        GameDefinition game,
        ModManager? mods,
        IProcessLauncher? processLauncher = null)
    {
        _game = game;
        _mods = mods;
        _processLauncher = processLauncher ?? new ProcessLauncher();
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

        var environment = new Dictionary<string, string?>();

        if (_mods is not null)
        {
            var activeList = _mods.WriteActiveList();

            environment["PARTYBOARD_MOD_LIST"] = Path.GetFullPath(activeList);
        }

        if (!string.IsNullOrWhiteSpace(_game.GameRootFullPath))
        {
            environment["PARTYBOARD_GAME_ROOT"] = _game.GameRootFullPath;
        }

        if (_game.HasDiscImage)
        {
            // PartyBoard can consume this variable once its file-selection path
            // is wired to CubeShelf. The launcher already persists ISO/RVZ.
            environment["PARTYBOARD_DISC_IMAGE"] = _game.DiscImageFullPath;
        }

        return _processLauncher.Start(
            exe,
            Path.GetDirectoryName(exe)!,
            environment);
    }
}
