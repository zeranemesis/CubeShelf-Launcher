package dev.net64.ghostship;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.os.Bundle;
import android.os.IBinder;
import android.os.Process;
import android.os.ResultReceiver;
import java.io.File;
import java.util.concurrent.atomic.AtomicBoolean;

/** A separate process reclaims Torch's retained extraction data after each attempt. */
public final class ExtractionService extends Service {
    private boolean running;
    private final AtomicBoolean delivered = new AtomicBoolean();
    private ResultReceiver receiver;

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || "cancel".equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (running) return START_NOT_STICKY;
        running = true;
        receiver = intent.getParcelableExtra("receiver");
        NotificationManager manager = getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("sm64-prepare", "Préparation Super Mario 64", NotificationManager.IMPORTANCE_LOW));
        Intent cancel = new Intent(this, ExtractionService.class).setAction("cancel");
        PendingIntent cancelAction = PendingIntent.getService(this, 0, cancel, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification = new Notification.Builder(this, "sm64-prepare")
            .setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle("DecompShelf · Super Mario 64")
            .setContentText("Préparation de ta copie du jeu…").setOngoing(true)
            .addAction(new Notification.Action.Builder(null, "Annuler", cancelAction).build()).build();
        startForeground(6401, notification);
        new Thread(() -> {
            String error = null;
            try {
                File root = new File(getFilesDir(), "native-games/sm64");
                String attempt = intent.getStringExtra("attempt");
                if (attempt == null || !attempt.matches("extracting-[a-f0-9]{32}")) throw new IllegalStateException("Préparation invalide.");
                File target = new File(root, attempt);
                File rom = new File(root, "baserom.us.z64");
                if (!rom.isFile() || !target.isDirectory()) throw new IllegalStateException("Préparation incomplète. Réimporte ta ROM.");
                System.loadLibrary("SDL2");
                System.loadLibrary("ghostship");
                error = new GameAssets().nativeGenerateGameArchive(rom.getAbsolutePath(), target.getAbsolutePath(), target.getAbsolutePath());
                if (error == null && new File(target, "sm64.o2r").length() == 0) error = "L’extraction n’a produit aucune donnée de jeu.";
            } catch (Throwable failure) {
                android.util.Log.e("DecompShelf.SM64", "Extraction failed", failure);
                error = "Impossible de préparer Super Mario 64 : " + failure.getMessage();
            }
            Bundle result = new Bundle();
            result.putString("error", error);
            if (delivered.compareAndSet(false, true) && receiver != null) receiver.send(error == null ? 0 : 1, result);
            stopForeground(STOP_FOREGROUND_REMOVE);
            stopSelf();
        }, "sm64-extraction").start();
        return START_NOT_STICKY;
    }

    @Override public void onDestroy() {
        if (delivered.compareAndSet(false, true) && receiver != null) {
            Bundle result = new Bundle();
            result.putString("error", "Préparation annulée. Tes sauvegardes sont conservées.");
            receiver.send(1, result);
        }
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
        // The activity and Avalonia UI run in other processes and remain alive.
        Process.killProcess(Process.myPid());
    }
}
