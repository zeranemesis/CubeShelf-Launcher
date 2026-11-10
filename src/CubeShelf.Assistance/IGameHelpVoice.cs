namespace CubeShelf.Assistance;

/// <summary>Local platform speech only, explicitly started by the player.</summary>
public interface IGameHelpVoice : IDisposable
{
    string? UnavailableReason { get; }
    Task<string> ListenAsync(CancellationToken cancellation);
    Task SpeakAsync(string text, CancellationToken cancellation);
    void Stop();
}
