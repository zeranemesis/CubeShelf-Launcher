using CubeShelf.Core.Library;

internal static class ConsoleMediaProfilesTests
{
    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Universal console profile assertion failed.");
    }

    public static void RegistryAndFallback()
    {
        Check(ConsoleMediaProfiles.All.Count >= 40);
        Check(ConsoleMediaProfiles.Resolve("GAMECUBE").Media == MediaFormFactor.MiniDisc);
        Check(ConsoleMediaProfiles.Resolve("n64").Media == MediaFormFactor.Cartridge);
        Check(ConsoleMediaProfiles.Resolve("ds").Media == MediaFormFactor.GameCard);
        Check(ConsoleMediaProfiles.Resolve("psp").Media == MediaFormFactor.Umd);
        Check(ConsoleMediaProfiles.Resolve("dreamcast").Media == MediaFormFactor.OpticalDisc);
        Check(ConsoleMediaProfiles.Resolve("pc-engine").Media == MediaFormFactor.HuCard);
        Check(ConsoleMediaProfiles.Resolve("windows").Media == MediaFormFactor.Digital);
        Check(!ConsoleMediaProfiles.Resolve("windows").IsPhysical);
        Check(!ConsoleMediaProfiles.TryGet("not-a-console", out var missing));
        Check(missing == ConsoleMediaProfiles.Fallback);
        Check(ConsoleMediaProfiles.Resolve(null) == ConsoleMediaProfiles.Fallback);
    }

    public static void CatalogMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-platforms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var shipped = Path.Combine(root, "shipped.json");
            var user = Path.Combine(root, "user");
            File.WriteAllText(shipped, "[{\"Id\":\"legacy-gc\",\"Title\":\"Existing title\",\"PlatformId\":\"gamecube\"}]");
            var service = new GameCatalogService(shipped, user);
            var oldGame = service.Load().Single();
            Check(oldGame.PlatformId == "gamecube");

            // Preserve user-owned play state, but refresh shipped platform metadata.
            oldGame.IsFavorite = true;
            oldGame.PlayCount = 7;
            service.Save(new[] { oldGame });
            File.WriteAllText(shipped, "[{\"Id\":\"legacy-gc\",\"Title\":\"Existing title\",\"PlatformId\":\"wii\"}]");
            var updated = service.Load().Single();
            Check(updated.PlatformId == "wii");
            Check(updated.IsFavorite && updated.PlayCount == 7);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
