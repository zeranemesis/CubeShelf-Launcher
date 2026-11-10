package com.mariopartyrd.partyboard.quest;

/** Phone integration: the separate Meta Quest/OpenXR host is not enabled. */
public final class QuestVr {
    private QuestVr() { }
    public static boolean isHeadset() { return false; }
    public static boolean isStarted() { return false; }
    public static boolean start(android.app.Activity activity, org.libsdl.app.SDLSurface surface) { return false; }
    public static void stop() { }
    public static float[] refreshRates() { return new float[0]; }
    public static void setFrameRate(float rate) { }
    public static boolean onRequestPermissionsResult(int request, int[] results) { return false; }
}
