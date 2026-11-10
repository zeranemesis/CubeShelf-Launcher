namespace CubeShelf.Assistance;

public enum CompanionState { Idle, Listening, Thinking, Speaking, Ready, Error }

public readonly record struct CompanionPose(int Frame, double Lift, double Pulse);

/// <summary>Presentation only: displaying the companion never instantiates the language model.</summary>
public static class Companion
{
    public static Stream OpenArtwork() => typeof(Companion).Assembly.GetManifestResourceStream(
        "CubeShelf.Assistance.Assets.companion-states.png") ?? throw new InvalidDataException("Illustration du compagnon manquante.");

    public static string Message(CompanionState state) => state switch
    {
        CompanionState.Listening => "Je t’écoute…",
        CompanionState.Thinking => "Je prépare ton indice…",
        CompanionState.Speaking => "Voici mon conseil…",
        CompanionState.Ready => "À toi de jouer !",
        CompanionState.Error => "On peut réessayer.",
        _ => "Dis-moi où tu es bloqué."
    };

    public static bool Moves(CompanionState state, double elapsed, bool reduceMotion) => !reduceMotion &&
        (state is CompanionState.Listening or CompanionState.Thinking or CompanionState.Speaking || elapsed < .9);

    public static CompanionPose Pose(CompanionState state, double elapsed, bool reduceMotion)
    {
        var frame = state switch
        {
            CompanionState.Thinking => 1,
            CompanionState.Listening => 2,
            CompanionState.Speaking => !reduceMotion && (int)(elapsed * 4) % 2 == 0 ? 0 : 3,
            CompanionState.Ready => 3,
            _ => 0
        };
        if (!Moves(state, elapsed, reduceMotion)) return new(frame, 0, 0);
        var wave = Math.Sin(elapsed * Math.PI * 2);
        return new(frame, wave * (state == CompanionState.Thinking ? 2.5 : 1.5), (wave + 1) * .5);
    }
}
