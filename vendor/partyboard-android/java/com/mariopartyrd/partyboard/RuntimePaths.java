package com.mariopartyrd.partyboard;

/** CubeShelf: resources, settings and saves belong to the containing app. */
public final class RuntimePaths {
    private RuntimePaths() { }
    public static java.io.File files(android.content.Context context) {
        java.io.File directory = new java.io.File(context.getApplicationContext().getFilesDir(), "partyboard");
        if (!directory.isDirectory() && !directory.mkdirs() && !directory.isDirectory()) {
            throw new IllegalStateException("Unable to create PartyBoard storage");
        }
        return directory;
    }
}
