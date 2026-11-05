package io.decompshelf.enginehost;
import android.content.*;
import android.content.pm.*;
import android.content.res.XmlResourceParser;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import org.json.JSONObject;
import java.io.File;

/** Preserves module metadata without registering another Android package. */
public final class ModulePackages {
    // System UI loads icons from the installed launcher, not the engine resource archive.
    public static void startForeground(android.app.Service service,int id,android.app.Notification notification) {
        service.startForeground(id,android.app.Notification.Builder.recoverBuilder(service,notification).setSmallIcon(android.R.drawable.stat_sys_download).build());
    }
    public static void startForeground(android.app.Service service,int id,android.app.Notification notification,int type) {
        service.startForeground(id,android.app.Notification.Builder.recoverBuilder(service,notification).setSmallIcon(android.R.drawable.stat_sys_download).build(),type);
    }
    static boolean own(String name){return name.equals(EngineRuntime.OWNER)||name.equals(EngineRuntime.session.archive.packageName);}
    public static ApplicationInfo getApplicationInfo(PackageManager manager,String name,int flags)throws PackageManager.NameNotFoundException{return own(name)?EngineRuntime.applicationInfo():manager.getApplicationInfo(name,flags);}
    public static PackageInfo getPackageInfo(PackageManager manager,String name,int flags)throws PackageManager.NameNotFoundException{return own(name)?EngineRuntime.session.archive:manager.getPackageInfo(name,flags);}
    public static ActivityInfo getActivityInfo(PackageManager manager,ComponentName name,int flags)throws PackageManager.NameNotFoundException {
        JSONObject component=EngineRuntime.session.component(name.getClassName());
        if(component!=null&&EngineRuntime.session.archive.activities!=null)for(ActivityInfo info:EngineRuntime.session.archive.activities)if(info.name.equals(component.optString("Name")))return new ActivityInfo(info);
        return manager.getActivityInfo(name,flags);
    }
    public static ServiceInfo getServiceInfo(PackageManager manager,ComponentName name,int flags)throws PackageManager.NameNotFoundException {
        JSONObject component=EngineRuntime.session.component(name.getClassName());
        if(component!=null&&EngineRuntime.session.archive.services!=null)for(ServiceInfo info:EngineRuntime.session.archive.services)if(info.name.equals(component.optString("Name")))return new ServiceInfo(info);
        return manager.getServiceInfo(name,flags);
    }
    static ProviderInfo provider(JSONObject component) {
        if(component==null||EngineRuntime.session.archive.providers==null)return null;
        for(ProviderInfo info:EngineRuntime.session.archive.providers)if(info.name.equals(component.optString("Name"))){ProviderInfo result=new ProviderInfo(info);result.authority=component.optString("Authority");return result;}
        return null;
    }
    public static ProviderInfo getProviderInfo(PackageManager manager,ComponentName name,int flags)throws PackageManager.NameNotFoundException {
        if(name.getClassName().equals("androidx.startup.InitializationProvider")&&EngineRuntime.session.archive.providers!=null)
            for(ProviderInfo p:EngineRuntime.session.archive.providers)if(p.name.equals(name.getClassName()))return new ProviderInfo(p);
        ProviderInfo info=provider(EngineRuntime.session.component(name.getClassName()));return info!=null?info:manager.getProviderInfo(name,flags);
    }
    public static ProviderInfo getProviderInfoUnchecked(PackageManager manager,ProviderInfo info) {
        ProviderInfo original=provider(EngineRuntime.session.component(info.name));
        return original!=null?original:info;
    }
    public static ProviderInfo resolveContentProvider(PackageManager manager,String authority,int flags) {
        for(JSONObject component:EngineRuntime.session.components.values())if(component.optString("Authority").equals(authority)||component.optString("OriginalAuthority").equals(authority))return provider(component);
        return manager.resolveContentProvider(authority,flags);
    }
    public static Intent getLaunchIntentForPackage(PackageManager manager,String name) {
        if(!own(name))return manager.getLaunchIntentForPackage(name);
        JSONObject main=EngineRuntime.session.component(EngineRuntime.session.spec.optString("MainActivity"));
        return new Intent().setComponent(new ComponentName(EngineRuntime.OWNER,main.optString("Bridge"))).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
    }
    public static Drawable getDrawable(PackageManager manager,String name,int id,ApplicationInfo info){return own(name)?EngineRuntime.resources().getDrawable(id,null):manager.getDrawable(name,id,info);}
    public static CharSequence getText(PackageManager manager,String name,int id,ApplicationInfo info){return own(name)?EngineRuntime.resources().getText(id):manager.getText(name,id,info);}
    public static XmlResourceParser getXml(PackageManager manager,String name,int id,ApplicationInfo info){return own(name)?EngineRuntime.resources().getXml(id):manager.getXml(name,id,info);}
    public static Uri getUriForFile(Context context,String authority,File file){return getUriForFile(context,authority,file,null);}
    public static Uri getUriForFile(Context context,String authority,File file,String displayName){
        try {
            String mapped=authority;
            String source=EngineRuntime.session.archive.packageName;
            for(JSONObject component:EngineRuntime.session.components.values()) {
                String original=component.optString("OriginalAuthority");
                if(original.equals(authority)||original.startsWith(source)&&authority.equals(EngineRuntime.OWNER+original.substring(source.length())))mapped=component.optString("Authority");
            }
            Class<?> provider=EngineRuntime.loader().loadClass("androidx.core.content.FileProvider");
            if(displayName==null)return (Uri)provider.getMethod("getUriForFile",Context.class,String.class,File.class).invoke(null,context,mapped,file);
            return (Uri)provider.getMethod("getUriForFile",Context.class,String.class,File.class,String.class).invoke(null,context,mapped,file,displayName);
        }catch(Exception failure){throw new IllegalStateException("Cannot share the game file",failure);}
    }
}
