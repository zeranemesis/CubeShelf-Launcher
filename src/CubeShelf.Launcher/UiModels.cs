namespace CubeShelf.Launcher;

public sealed class UiMod : INotifyPropertyChanged
{
    private int _priority = 100;

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string ProfileUrl { get; init; } = "";
    public string Description { get; init; } = "";
    public string VersionLabel { get; init; } = "";
    public string LatestUpdateSummary { get; init; } = "";
    public string ThumbnailUrl { get; init; } = "";
    public IReadOnlyList<string> Images { get; init; } = Array.Empty<string>();

    public bool Installed { get; set; }
    public bool Enabled { get; set; }
    public bool UpdateAvailable { get; set; }

    public int Priority
    {
        get => _priority;
        set { _priority = value; OnChanged(); }
    }

    public string StatusText =>
        !Installed ? "Non installé" :
        Enabled ? "Installé • activé" : "Installé • désactivé";

    public string InstallText =>
        !Installed ? "Installer" :
        UpdateAvailable ? "Mettre à jour" : "Réinstaller";

    public string ToggleText => Enabled ? "Désactiver" : "Activer";
    public string UpdateText => UpdateAvailable ? "Mise à jour disponible" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private void OnChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
