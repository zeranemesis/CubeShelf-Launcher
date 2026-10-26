package dev.net64.ghostship;

import android.os.Bundle;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.FrameLayout;
import java.io.File;
import io.decompshelf.sdl2.SDLActivity;

/** Embedded host; Ghostship still draws its own game, settings and touch controls. */
public final class MainActivity extends SDLActivity {
    @Override protected String[] getLibraries() { return new String[] {"SDL2", "ghostship"}; }

    @Override public File getExternalFilesDir(String type) {
        File root = new File(getFilesDir(), "native-games/sm64/current");
        root.mkdirs();
        return root;
    }

    @Override protected void onCreate(Bundle state) {
        try {
            File saves = new File(getFilesDir(), "native-games/sm64/saves");
            saves.mkdirs();
            android.system.Os.setenv("GHOSTSHIP_SAVES_DIR", saves.getAbsolutePath(), true);
            android.system.Os.setenv("SDL_IOS_ORIENTATIONS", "LandscapeLeft LandscapeRight", true);
        } catch (android.system.ErrnoException failure) { throw new IllegalStateException(failure); }
        super.onCreate(state);
        if (mBrokenLibraries) return;
        immersive();
        Button back = new Button(this);
        back.setText("Retour à DecompShelf");
        back.setTextSize(11);
        back.setAlpha(0.7f);
        back.setOnClickListener(view -> finish());
        FrameLayout.LayoutParams layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.END);
        ((ViewGroup)findViewById(android.R.id.content)).addView(back, layout);
    }

    private void immersive() {
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if (focused) immersive();
    }
    @Override public void onBackPressed() { finish(); }
    @Override protected void onDestroy() {
        boolean finishing = isFinishing();
        super.onDestroy();
        if (finishing) System.exit(0);
    }
}
