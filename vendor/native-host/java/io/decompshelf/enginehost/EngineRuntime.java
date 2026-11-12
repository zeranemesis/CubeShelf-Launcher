package io.decompshelf.enginehost;

import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.content.res.*;
import android.os.*;
import android.view.*;
import dalvik.system.DexClassLoader;
import org.json.*;
import java.io.*;
import java.security.*;
import java.util.*;

/** Hosts only the engine revisions pinned in the signed launcher. */
public final class EngineRuntime {
    public static final String OWNER="io.github.zeranemesis.cubeshelf";
    static Session session;
    public static final class Session {
        public JSONObject spec;
        public File module,data,cache;
        public Context base;
        public Application application;
        public PackageInfo archive;
        public Resources resources;
        public ClassLoader loader;
        public final Map<String,JSONObject> components=new HashMap<>();
        public String id(){return spec.optString("Id");}
        public JSONObject component(String name){
            JSONObject direct=components.get(name);
            if(direct!=null)return direct;
            for(JSONObject c:components.values())if(c.optString("Bridge").equals(name))return c;
            return null;
        }
    }

    static boolean engineProcess(){return Application.getProcessName().contains(":engine_");}
    static void attach(Context context,Application application) {
        if(session!=null){session.application=application;return;}
        try {
            String process=Application.getProcessName();
            JSONArray specs=new JSONArray(read(context.getAssets().open("nativehost/hosts.json"),1048576));
            JSONObject selected=null;
            for(int i=0;i<specs.length();i++) {
                JSONObject s=specs.getJSONObject(i);String prefix=OWNER+s.getString("Process");
                if(process.equals(prefix)||process.startsWith(prefix+"__")){selected=s;break;}
            }
            if(selected==null)throw new IOException("Unknown engine process");
            Session s=new Session();s.spec=selected;s.base=context;s.application=application;
            s.module=new File(context.getFilesDir(),"native-modules/"+s.id()+"/"+selected.getString("Revision").substring(0,20));
            s.data=new File(context.getFilesDir(),"native-games/"+s.id());
            s.cache=new File(context.getCacheDir(),"native-games/"+s.id());
            s.data.mkdirs();s.cache.mkdirs();
            File apk=new File(s.module,"source.apk");verify(apk,selected.getString("ApkSha256"),selected.getLong("ApkSize"));
            StringBuilder paths=new StringBuilder();
            for(int i=0;i<selected.getJSONArray("Code").length();i++) {
                JSONObject file=selected.getJSONArray("Code").getJSONObject(i);
                File dex=new File(s.module,"code/"+file.getString("Name"));verify(dex,file.getString("Sha256"),file.getLong("Size"));
                paths.append(dex.getAbsolutePath()).append(File.pathSeparator);
            }
            File bridges=new File(s.module,"bridges.dex");verify(bridges,selected.getString("BridgeSha256"),bridges.length());paths.append(bridges);
            for(int i=0;i<selected.getJSONArray("Native").length();i++) {
                JSONObject file=selected.getJSONArray("Native").getJSONObject(i);
                verify(new File(s.module,"lib/"+file.getString("Name")),file.getString("Sha256"),file.getLong("Size"));
            }
            s.loader=new GameLoader(paths.toString(),context.getCodeCacheDir().getAbsolutePath(),new File(s.module,"lib").getAbsolutePath(),EngineRuntime.class.getClassLoader());
            int flags=PackageManager.GET_ACTIVITIES|PackageManager.GET_SERVICES|PackageManager.GET_PROVIDERS|PackageManager.GET_RECEIVERS|PackageManager.GET_META_DATA|PackageManager.GET_SIGNATURES|PackageManager.GET_PERMISSIONS;
            s.archive=context.getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(),flags);
            if(s.archive==null||!s.archive.packageName.equals(selected.getString("PackageName")))throw new IOException("Engine identity mismatch");
            ApplicationInfo info=s.archive.applicationInfo;
            info.sourceDir=info.publicSourceDir=apk.getAbsolutePath();info.nativeLibraryDir=new File(s.module,"lib").getAbsolutePath();
            info.dataDir=s.data.getAbsolutePath();info.uid=android.os.Process.myUid();
            Resources original=context.getPackageManager().getResourcesForApplication(info);
            s.resources=new GameResources(original,info.packageName);
            JSONArray cs=selected.getJSONArray("Components");
            for(int i=0;i<cs.length();i++){JSONObject c=cs.getJSONObject(i);s.components.put(c.getString("Name"),c);}
            session=s;
            Thread.currentThread().setContextClassLoader(s.loader);
            android.system.Os.setenv("HOME",s.data.getAbsolutePath(),true);
            android.system.Os.setenv("TMPDIR",s.cache.getAbsolutePath(),true);
            android.system.Os.setenv("SDL_ANDROID_INTERNAL_STORAGE",s.data.getAbsolutePath(),true);
            android.system.Os.setenv("SDL_ANDROID_EXTERNAL_STORAGE",s.data.getAbsolutePath(),true);
            System.setProperty("user.home",s.data.getAbsolutePath());System.setProperty("user.dir",s.data.getAbsolutePath());
            Bundle metadata=info.metaData;
            if(metadata!=null)for(String key:metadata.keySet())if(key.startsWith("SDL_ENV."))android.system.Os.setenv(key.substring(8),String.valueOf(metadata.get(key)),true);
        } catch(Exception failure) {throw new IllegalStateException("Cannot open the verified DecompShelf engine",failure);}
    }

    public static Context wrap(Context base){return base instanceof GameContext?base:new GameContext(base);}
    public static Resources resources(){return session.resources;}
    public static ApplicationInfo applicationInfo(){return new ApplicationInfo(session.archive.applicationInfo);}
    public static ClassLoader loader(){return session.loader;}
    public static File data(){return session.data;}
    public static File cache(){return session.cache;}

    public static int theme(String original,int requested) {
        if(requested!=android.R.style.Theme_Black_NoTitleBar_Fullscreen)return requested;
        if(session.archive.activities!=null)for(ActivityInfo a:session.archive.activities)if(a.name.equals(original)&&a.theme!=0)return a.theme;
        return session.archive.applicationInfo.theme!=0?session.archive.applicationInfo.theme:requested;
    }

    public static Intent intent(Intent original) {
        Intent result=new Intent(original);ComponentName c=result.getComponent();
        if(c!=null&&(c.getPackageName().equals(OWNER)||c.getPackageName().equals(session.archive.packageName))) {
            JSONObject component=session.component(c.getClassName());
            if(component!=null)result.setComponent(new ComponentName(OWNER,component.optString("Bridge")));
        }
        if(Intent.ACTION_INSTALL_PACKAGE.equals(result.getAction())||"application/vnd.android.package-archive".equals(result.getType()))throw new ActivityNotFoundException("Les moteurs sont gérés par DecompShelf.");
        return result;
    }

    public static void created(Activity activity) {
        if (activity.isFinishing()) return;
        JSONObject main = session.component(session.spec.optString("MainActivity"));
        if (main != null && main.optString("Bridge").equals(activity.getClass().getName())) {
            Window window = activity.getWindow();
            window.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
            if (Build.VERSION.SDK_INT >= 30) {
                View decor = window.getDecorView();
                Runnable hideStatusBar = () -> {
                    WindowInsetsController insets = window.getInsetsController();
                    if (insets != null) {
                        insets.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                        insets.hide(WindowInsets.Type.statusBars());
                    }
                };
                decor.post(hideStatusBar);
                decor.getViewTreeObserver().addOnWindowFocusChangeListener(focused -> {
                    if (focused) decor.post(hideStatusBar);
                });
            }
        }
    }

    static void initialize(Context context) {
        if(session.archive.providers==null)return;
        for(ProviderInfo p:session.archive.providers)if(p.name.equals("androidx.startup.InitializationProvider")&&p.metaData!=null) {
            try {
                ContentProvider provider=(ContentProvider)loader().loadClass(p.name).getDeclaredConstructor().newInstance();
                provider.attachInfo(context,new ProviderInfo(p));
            } catch(Exception failure){throw new IllegalStateException("Engine initialization failed",failure);}
        }
    }

    static String read(InputStream stream,int limit)throws IOException {
        try(InputStream input=stream;ByteArrayOutputStream output=new ByteArrayOutputStream()){
            byte[] buffer=new byte[8192];int count,total=0;
            while((count=input.read(buffer))!=-1){total+=count;if(total>limit)throw new IOException("Engine metadata too large");output.write(buffer,0,count);}
            return output.toString("UTF-8");
        }
    }
    static void verify(File file,String sha,long size)throws Exception {
        if(!sha.matches("[0-9a-f]{64}")||!file.isFile()||file.length()!=size)throw new IOException("Engine file mismatch: "+file.getName());
        MessageDigest digest=MessageDigest.getInstance("SHA-256");
        try(InputStream input=new FileInputStream(file)){byte[] buffer=new byte[131072];int n;while((n=input.read(buffer))!=-1)digest.update(buffer,0,n);}
        StringBuilder actual=new StringBuilder();for(byte b:digest.digest())actual.append(String.format(java.util.Locale.ROOT,"%02x",b&255));
        if(!sha.equals(actual.toString()))throw new IOException("Engine checksum mismatch: "+file.getName());
        if(file.canWrite())throw new IOException("Executable engine file is writable");
    }

    private static final class GameLoader extends DexClassLoader {
        GameLoader(String code,String optimized,String libraries,ClassLoader parent){super(code,optimized,libraries,parent);}
        @Override protected Class<?> loadClass(String name,boolean resolve)throws ClassNotFoundException {
            if(name.startsWith("java.")||name.startsWith("javax.")||name.startsWith("android.")||name.startsWith("dalvik.")||name.startsWith("org.xml.")||name.startsWith("org.w3c.")||name.startsWith("io.decompshelf.enginehost."))return super.loadClass(name,resolve);
            synchronized(this){
                Class<?> found=findLoadedClass(name);
                if(found==null)try{found=findClass(name);}catch(ClassNotFoundException missing){found=super.loadClass(name,false);}
                if(resolve)resolveClass(found);return found;
            }
        }
    }
    private static final class GameResources extends Resources {
        final String original;
        GameResources(Resources resources,String pkg){super(resources.getAssets(),resources.getDisplayMetrics(),resources.getConfiguration());original=pkg;}
        @Override public int getIdentifier(String name,String type,String pkg){return super.getIdentifier(name,type,OWNER.equals(pkg)?original:pkg);}
    }
}
