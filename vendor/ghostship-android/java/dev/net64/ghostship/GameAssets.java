package dev.net64.ghostship;

/** JNI name retained to call the pinned official Ghostship extractor. */
public final class GameAssets {
    public native String nativeGenerateGameArchive(String rom, String sourceDir, String destDir);
}
