package io.decompshelf.enginehost;
import android.app.Application;
import android.content.Context;
public final class HostApplication extends Application {
    @Override protected void attachBaseContext(Context base){EngineRuntime.attach(base,this);super.attachBaseContext(EngineRuntime.wrap(base));}
    @Override public void onCreate(){super.onCreate();EngineRuntime.initialize(this);}
}
