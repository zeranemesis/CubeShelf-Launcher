package io.decompshelf.enginehost;
import android.content.*;
import android.content.pm.*;
import android.content.res.*;
import android.os.*;
import android.view.LayoutInflater;
import java.io.File;

/** Redirects game storage to this application's private per-game directory. */
public final class GameContext extends ContextWrapper {
    GameContext(Context base){super(base);}
    @Override public File getFilesDir(){return EngineRuntime.data();}
    @Override public File getDataDir(){return EngineRuntime.data();}
    @Override public File getCacheDir(){return EngineRuntime.cache();}
    @Override public String getPackageCodePath(){return EngineRuntime.applicationInfo().sourceDir;}
    @Override public String getPackageResourcePath(){return getPackageCodePath();}
    @Override public File getFileStreamPath(String name){return child(getFilesDir(),name);}
    @Override public java.io.FileInputStream openFileInput(String name)throws java.io.FileNotFoundException{return new java.io.FileInputStream(getFileStreamPath(name));}
    @Override public java.io.FileOutputStream openFileOutput(String name,int mode)throws java.io.FileNotFoundException{return new java.io.FileOutputStream(getFileStreamPath(name),(mode&MODE_APPEND)!=0);}
    @Override public boolean deleteFile(String name){return getFileStreamPath(name).delete();}
    @Override public String[] fileList(){String[] files=getFilesDir().list();return files==null?new String[0]:files;}
    @Override public File getDir(String name,int mode){File dirs=new File(getFilesDir(),"dirs");File result=child(dirs,name);result.mkdirs();return result;}
    @Override public File getCodeCacheDir(){return getBaseContext().getCodeCacheDir();}
    @Override public File getNoBackupFilesDir(){File f=new File(getFilesDir(),"no-backup");f.mkdirs();return f;}
    @Override public File getExternalFilesDir(String type){File f=type==null?getFilesDir():child(getFilesDir(),type);f.mkdirs();return f;}
    @Override public File[] getExternalFilesDirs(String type){return new File[]{getExternalFilesDir(type)};}
    @Override public File getExternalCacheDir(){return getCacheDir();}
    @Override public File[] getExternalCacheDirs(){return new File[]{getCacheDir()};}
    @Override public File[] getExternalMediaDirs(){return new File[]{getFilesDir()};}
    @Override public File getObbDir(){File f=new File(getFilesDir(),"obb");f.mkdirs();return f;}
    @Override public File[] getObbDirs(){return new File[]{getObbDir()};}
    @Override public Resources getResources(){return EngineRuntime.resources();}
    @Override public AssetManager getAssets(){return getResources().getAssets();}
    @Override public ClassLoader getClassLoader(){return EngineRuntime.loader();}
    @Override public ApplicationInfo getApplicationInfo(){return EngineRuntime.applicationInfo();}
    @Override public Context getApplicationContext(){return EngineRuntime.session.application;}
    @Override public SharedPreferences getSharedPreferences(String name,int mode){return EngineRuntime.session.base.getSharedPreferences("engine_"+EngineRuntime.session.id()+"_"+name,mode);}
    @Override public File getDatabasePath(String name){File d=new File(getFilesDir(),"databases");d.mkdirs();return child(d,name);}
    @Override public android.database.sqlite.SQLiteDatabase openOrCreateDatabase(String name,int mode,android.database.sqlite.SQLiteDatabase.CursorFactory factory){return android.database.sqlite.SQLiteDatabase.openOrCreateDatabase(getDatabasePath(name),factory);}
    @Override public android.database.sqlite.SQLiteDatabase openOrCreateDatabase(String name,int mode,android.database.sqlite.SQLiteDatabase.CursorFactory factory,android.database.DatabaseErrorHandler handler){return android.database.sqlite.SQLiteDatabase.openOrCreateDatabase(getDatabasePath(name).getAbsolutePath(),factory,handler);}
    @Override public boolean deleteDatabase(String name){return android.database.sqlite.SQLiteDatabase.deleteDatabase(getDatabasePath(name));}
    @Override public Object getSystemService(String name){Object service=super.getSystemService(name);return LAYOUT_INFLATER_SERVICE.equals(name)?((LayoutInflater)service).cloneInContext(this):service;}
    @Override public Context createConfigurationContext(Configuration configuration){return new GameContext(super.createConfigurationContext(configuration));}
    @Override public Context createPackageContext(String name,int flags)throws PackageManager.NameNotFoundException {
        return name.equals(EngineRuntime.OWNER)||name.equals(EngineRuntime.session.archive.packageName)?this:super.createPackageContext(name,flags);
    }
    @Override public void startActivity(Intent intent){super.startActivity(EngineRuntime.intent(intent));}
    @Override public void startActivity(Intent intent,Bundle options){super.startActivity(EngineRuntime.intent(intent),options);}
    @Override public ComponentName startService(Intent intent){return super.startService(EngineRuntime.intent(intent));}
    @Override public ComponentName startForegroundService(Intent intent){return super.startForegroundService(EngineRuntime.intent(intent));}
    @Override public boolean stopService(Intent intent){return super.stopService(EngineRuntime.intent(intent));}
    @Override public boolean bindService(Intent intent,ServiceConnection connection,int flags){return super.bindService(EngineRuntime.intent(intent),connection,flags);}
    @Override public int checkPermission(String permission,int pid,int uid){return scopedStorage(permission)?PackageManager.PERMISSION_GRANTED:super.checkPermission(permission,pid,uid);}
    @Override public int checkSelfPermission(String permission){return scopedStorage(permission)?PackageManager.PERMISSION_GRANTED:super.checkSelfPermission(permission);}
    static boolean scopedStorage(String permission){return permission.equals("android.permission.READ_EXTERNAL_STORAGE")||permission.equals("android.permission.WRITE_EXTERNAL_STORAGE")||permission.equals("android.permission.MANAGE_EXTERNAL_STORAGE");}
    static File child(File root,String name){
        if(name==null||name.isEmpty()||name.contains("/")||name.contains("\\")||name.equals(".")||name.equals(".."))throw new IllegalArgumentException("Invalid game file name");
        return new File(root,name);
    }
}
