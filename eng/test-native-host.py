"""Check the bytecode adapter and the signed APK's private-engine packaging."""
import hashlib,importlib.util,json,pathlib,sys,zipfile

ROOT=pathlib.Path(__file__).resolve().parents[1]
loader=importlib.util.spec_from_file_location('native_host',ROOT/'eng/prepare-native-host.py')
host=importlib.util.module_from_spec(loader);loader.loader.exec_module(host)
descriptor={'Lgame/Main;':'Lio/decompshelf/enginebridge/Test;'}
sample='''.class public final Lgame/Main;
.method public final onCreate()V
const-class v1, Lgame/Main;
invoke-virtual {v0, v1, v2}, Landroid/content/pm/PackageManager;->getActivityInfo(Landroid/content/ComponentName;I)Landroid/content/pm/ActivityInfo;
invoke-static {}, Landroid/os/Environment;->getExternalStorageDirectory()Ljava/io/File;
'''
adapted=host.adapt_smali(sample,descriptor)
assert '.class public Lgame/Main;' in adapted and '.method public onCreate()V' in adapted
assert 'const-class v1, Lio/decompshelf/enginebridge/Test;' in adapted
assert 'invoke-static {v0, v1, v2}, Lio/decompshelf/enginehost/ModulePackages;->getActivityInfo(Landroid/content/pm/PackageManager;Landroid/content/ComponentName;I)' in adapted
assert 'Lio/decompshelf/enginehost/OwnedStorage;->getExternalStorageDirectory()' in adapted
foreground=host.adapt_foreground('invoke-virtual {p0, v0, v1}, Lgame/Setup;->startForeground(ILandroid/app/Notification;)V',['game.Setup'])
assert 'invoke-static {p0, v0, v1}, Lio/decompshelf/enginehost/ModulePackages;->startForeground(Landroid/app/Service;ILandroid/app/Notification;)V' in foreground
assert host.adapt_final_overrides('.method public final startActivityForResult(Landroid/content/Intent;ILandroid/os/Bundle;)V')=='.method public startActivityForResult(Landroid/content/Intent;ILandroid/os/Bundle;)V'
unrelated='.class public final Lgame/Other;\n.method public final value()I\n'
assert host.adapt_smali(unrelated,descriptor)==unrelated
parsed=host.manifest('E: manifest\n  A: package="org.example"\n  E: application\n    A: http://schemas.android.com/apk/res/android:screenOrientation=(type 0x10)0x6\n')
assert parsed['Attributes']['package']=='org.example' and parsed['Children'][0]['Attributes']['screenOrientation']=='0x6'
print('Native host bytecode adaptation checks passed.')

if len(sys.argv)>1:
 with zipfile.ZipFile(sys.argv[1]) as apk:
  specs=json.loads(apk.read('assets/nativehost/hosts.json'))
  ports=json.loads((ROOT/'src/CubeShelf.Mobile/Catalogs/android-ports.json').read_text(encoding='utf-8'))
  assert {s['Id'] for s in specs}=={p['Id'] for p in ports if p['Id']!='n64-sm64'}
  bridge=apk.read('assets/nativehost/bridges.dex');assert bridge.startswith(b'dex\n')
  manifest=apk.read('AndroidManifest.xml')
  def declared(name):return name.encode() in manifest or name.encode('utf-16le') in manifest
  assert declared('io.decompshelf.enginehost.HostFactory')
  authorities=set();components=set()
  for spec in specs:
   port=next(p for p in ports if p['Id']==spec['Id'])
   assert spec['ApkSha256']==port['ApkSha256'] and spec['PackageName']==port['PackageName']
   assert spec['Process']==':engine_'+spec['Id'].replace('-','_') and declared(spec['Process'])
   assert declared('io.github.zeranemesis.cubeshelf.native.engine_'+spec['Id'].replace('-','_')),'Activity task affinities must start with a letter, including 3DS game IDs'
   assert hashlib.sha256(bridge).hexdigest()==spec['BridgeSha256']
   assert any(c['Kind']=='activity' and c['Name']==spec['MainActivity'] for c in spec['Components'])
   for component in spec['Components']:
    assert component['Bridge'] not in components and declared(component['Bridge']);components.add(component['Bridge'])
    if component['Kind']=='provider':
     authority=component['Authority'];assert authority.startswith('io.github.zeranemesis.cubeshelf.native.') and authority not in authorities;authorities.add(authority)
   for code in spec['Code']:
    patch=apk.read('assets/'+code['Patch']);assert patch.startswith(b'\xd6\xc3\xc4') and hashlib.sha256(patch).hexdigest()==code['PatchSha256']
  for name in apk.namelist():
   assert not name.endswith(('stubs.jar','source.apk'))
   if name.startswith('classes') and name.endswith('.dex'):assert b'Lio/decompshelf/enginebridge/' not in apk.read(name),'Compile-only bridges leaked into the launcher class loader'
  print(f'APK contains {len(specs)} pinned hosts, {len(components)} private components and verified adapter deltas; compile-only stubs are absent.')
