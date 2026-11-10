package io.decompshelf.enginehost;
import android.app.*;
import android.content.*;
import android.os.Bundle;
import org.json.JSONObject;

public final class HostFactory extends AppComponentFactory {
    private AppComponentFactory normal(ClassLoader loader){
        try{return (AppComponentFactory)loader.loadClass("androidx.core.app.CoreComponentFactory").getDeclaredConstructor().newInstance();}
        catch(Exception ignored){return new AppComponentFactory();}
    }
    private Object component(String name)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        JSONObject info=EngineRuntime.session.component(name);
        if(info==null)throw new ClassNotFoundException("Unregistered engine component: "+name);
        try{return EngineRuntime.loader().loadClass(info.getString("Bridge")).getDeclaredConstructor().newInstance();}
        catch(Exception failed){throw new IllegalStateException("Cannot create integrated game component",failed);}
    }
    @Override public Application instantiateApplication(ClassLoader loader,String name)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        return EngineRuntime.engineProcess()?new HostApplication():normal(loader).instantiateApplication(loader,name);
    }
    @Override public Activity instantiateActivity(ClassLoader loader,String name,Intent intent)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        return EngineRuntime.engineProcess()?(Activity)component(name):normal(loader).instantiateActivity(loader,name,intent);
    }
    @Override public Service instantiateService(ClassLoader loader,String name,Intent intent)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        return EngineRuntime.engineProcess()?(Service)component(name):normal(loader).instantiateService(loader,name,intent);
    }
    @Override public BroadcastReceiver instantiateReceiver(ClassLoader loader,String name,Intent intent)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        return EngineRuntime.engineProcess()?(BroadcastReceiver)component(name):normal(loader).instantiateReceiver(loader,name,intent);
    }
    @Override public ContentProvider instantiateProvider(ClassLoader loader,String name)throws InstantiationException,IllegalAccessException,ClassNotFoundException {
        // .NET inserts one bootstrap provider per process; these engines run Java/JNI only.
        if(EngineRuntime.engineProcess()&&name.matches("mono\\.MonoRuntimeProvider(?:_\\d+)?"))return new EngineOnlyProvider();
        return EngineRuntime.engineProcess()?(ContentProvider)component(name):normal(loader).instantiateProvider(loader,name);
    }
    public static final class EngineOnlyProvider extends ContentProvider {
        public boolean onCreate(){return true;}
        public android.database.Cursor query(android.net.Uri u,String[] p,String s,String[] a,String o){return null;}
        public String getType(android.net.Uri u){return null;}
        public android.net.Uri insert(android.net.Uri u,ContentValues values){throw new UnsupportedOperationException();}
        public int delete(android.net.Uri u,String s,String[] a){throw new UnsupportedOperationException();}
        public int update(android.net.Uri u,ContentValues values,String s,String[] a){throw new UnsupportedOperationException();}
    }
}
