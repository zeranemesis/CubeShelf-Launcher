package io.decompshelf.enginehost;
import android.content.Context;
import java.io.File;

/** Used by rewritten legacy storage calls; it never grants external-storage access. */
public final class OwnedStorage {
    public static File getExternalStorageDirectory(){return EngineRuntime.data();}
    public static File getExternalStoragePublicDirectory(String type){File dir=GameContext.child(EngineRuntime.data(),type);dir.mkdirs();return dir;}
    public static boolean isExternalStorageManager(){return EngineRuntime.data().canRead()&&EngineRuntime.data().canWrite();}
    public static boolean isExternalStorageManager(File file){try{return file.getCanonicalPath().startsWith(EngineRuntime.data().getCanonicalPath()+File.separator)&&file.canRead();}catch(Exception e){return false;}}
    public static int checkSelfPermission(Context context,String permission){return GameContext.scopedStorage(permission)?android.content.pm.PackageManager.PERMISSION_GRANTED:context.checkSelfPermission(permission);}
}
