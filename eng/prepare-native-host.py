"""Build private-process hosts and exact, integrity-pinned Android engine adapters."""
import argparse,hashlib,inspect,json,os,pathlib,re,shutil,subprocess,urllib.request,zipfile,xml.etree.ElementTree as ET

ROOT=pathlib.Path(__file__).resolve().parents[1]
ANDROID='http://schemas.android.com/apk/res/android';ET.register_namespace('android',ANDROID)
TOOLS='http://schemas.android.com/tools';ET.register_namespace('tools',TOOLS)
def attr(node,name,value):node.set('{'+ANDROID+'}'+name,str(value))
def readable(file):return pathlib.Path('\\\\?\\'+str(file.resolve())) if os.name=='nt' else file
def clean(directory):
 directory=directory.resolve()
 if ROOT.resolve() not in directory.parents:raise ValueError('Build cleanup outside repository')
 if directory.exists():shutil.rmtree(readable(directory))
 directory.mkdir(parents=True)
def sha(file):
 with pathlib.Path(file).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def fetch(url,file,expected=None):
 file.parent.mkdir(parents=True,exist_ok=True)
 if not file.exists():
  request=urllib.request.Request(url,headers={'User-Agent':'DecompShelf engine build'})
  with urllib.request.urlopen(request,timeout=60) as response,file.with_suffix('.download').open('wb') as output:shutil.copyfileobj(response,output)
  file.with_suffix('.download').replace(file)
 if expected and sha(file)!=expected:raise ValueError('Checksum mismatch: '+str(file))
 return file
def manifest(text):
 stack=[];result=None
 for line in text.splitlines():
  m=re.match(r'(\s*)E: (\S+)',line)
  if m:
   depth=len(m[1]);node={'Tag':m[2],'Attributes':{},'Children':[]}
   while stack and stack[-1][0]>=depth:stack.pop()
   if stack:stack[-1][1]['Children'].append(node)
   else:result=node
   stack.append((depth,node));continue
  m=re.search(r'A: (?:http://schemas.android.com/apk/res/android:)?([\w.]+)(?:\([^)]*\))?=(.*)',line)
  if m and stack:
   quoted=re.match(r'"(.*?)"',m[2]);value=quoted[1] if quoted else re.sub(r'^\(type [^)]+\)','',m[2]);stack[-1][1]['Attributes'][m[1]]=value
 return result
def full(name,pkg):return pkg+name if name.startswith('.') else name if '.' in name else pkg+'.'+name
def tool_jars(cache):
 packages=[('org.smali','baksmali','2.5.2'),('org.smali','smali','2.5.2'),('org.smali','dexlib2','2.5.2'),('org.smali','util','2.5.2'),('org.antlr','antlr','3.5.2'),('org.antlr','antlr-runtime','3.5.2'),('org.antlr','stringtemplate','3.2.1'),('antlr','antlr','2.7.7'),('com.beust','jcommander','1.64'),('com.google.guava','guava','27.1-android'),('com.google.guava','failureaccess','1.0.1'),('com.google.code.findbugs','jsr305','3.0.2')]
 lockfile=ROOT/'vendor/native-host/tools.lock.json';lock=json.loads(lockfile.read_text()) if lockfile.exists() else {}
 for group,name,version in packages:
  filename=f'{name}-{version}.jar';url='https://repo.maven.apache.org/maven2/'+group.replace('.','/')+f'/{name}/{version}/{filename}'
  if filename not in lock:raise ValueError('Unpinned build dependency: '+filename)
  fetch(url,cache/filename,lock[filename])
 return os.pathsep.join(str(cache/f'{a}-{v}.jar') for g,a,v in packages)
def invoke(args):subprocess.run([str(a) for a in args],check=True,stdout=subprocess.DEVNULL)

def main():
 p=argparse.ArgumentParser();p.add_argument('--sdk',type=pathlib.Path,required=True);p.add_argument('--java-home',type=pathlib.Path,required=True);p.add_argument('--only');args=p.parse_args()
 cache=ROOT/'artifacts/native-host';cache.mkdir(parents=True,exist_ok=True)
 output=ROOT/'src/CubeShelf.Android/Generated/NativeHost';assets=output/'assets/nativehost';assets.mkdir(parents=True,exist_ok=True)
 exe='.exe' if os.name=='nt' else '';java=args.java_home/'bin'/('java'+exe);javac=args.java_home/'bin'/('javac'+exe)
 sdkjar=args.sdk/'platforms/android-34/android.jar';buildtools=args.sdk/'build-tools/34.0.0'
 if not buildtools.exists():buildtools=args.sdk/'build-tools/35.0.0'
 aapt=buildtools/('aapt2'+exe);d8=buildtools/'lib/d8.jar'
 tools=tool_jars(cache/'tools')
 if os.name=='nt':
  xzip=fetch('https://github.com/jmacd/xdelta/releases/download/v3.2.2/xdelta3-3.2.2-windows-x86_64.zip',cache/'tools/xdelta3.zip','d48f7651d86b78fdca397057b46601f89131f37219675df736db70af44ec3379')
  with zipfile.ZipFile(xzip) as z:
   member=next(n for n in z.namelist() if n.endswith('xdelta3.exe'));xdelta=cache/'tools/xdelta3.exe';xdelta.write_bytes(z.read(member))
 else:
  xdelta=shutil.which('xdelta3')
  if not xdelta:raise RuntimeError('Install xdelta3 before building the Android native hosts')
 ports=json.loads((ROOT/'src/CubeShelf.Mobile/Catalogs/android-ports.json').read_text(encoding='utf-8'))
 ports=[port for port in ports if port['Id']!='n64-sm64' and not port.get('ExternalInstall',False) and (not args.only or port['Id']==args.only)]
 source_map={}
 # Existing verified research archives avoid downloading the same public APK twice.
 for folder in [ROOT.parent/'android-native-research',ROOT.parent/'.tools/android-port-audit',ROOT.parent/'android-parity-2026-11-02']:
  for file in folder.glob('*.apk'):
   digest=sha(file)
   for port in ports:
    if digest==port['ApkSha256']:source_map[port['Id']]=file
 specs=[];sources={};raw_components={}
 for port in ports:
  if not re.fullmatch(r'[A-Za-z0-9-]{1,80}',port['Id']) or pathlib.Path(port['Name']).name!=port['Name'] or '\\' in port['Name']:raise ValueError('Unsafe engine build path')
  work=cache/port['Id'];work.mkdir(exist_ok=True);apk=work/'source.apk'
  if apk.exists() and sha(apk)!=port['ApkSha256']:apk.unlink()
  if not apk.exists() and port['Id'] in source_map:shutil.copyfile(source_map[port['Id']],apk)
  if not apk.exists():
   archive=fetch(port['Url'],work/port['Name'],port['Sha256'])
   if archive.suffix=='.apk':shutil.copyfile(archive,apk)
   else:
    with zipfile.ZipFile(archive) as z:
     candidates=[z.read(n) for n in z.namelist() if n.endswith('.apk')]
     matches=[data for data in candidates if hashlib.sha256(data).hexdigest()==port['ApkSha256']]
     if len(matches)!=1:raise ValueError('APK not uniquely identified')
     apk.write_bytes(matches[0])
  if sha(apk)!=port['ApkSha256'] or apk.stat().st_size!=port['ApkSize']:raise ValueError('Engine APK mismatch')
  raw=manifest(subprocess.check_output([str(aapt),'dump','xmltree','--file','AndroidManifest.xml',str(apk)]).decode('utf-8'))
  app=next(n for n in raw['Children'] if n['Tag']=='application');pkg=raw['Attributes']['package']
  if pkg!=port['PackageName'] or app['Attributes'].get('name','android.app.Application')!='android.app.Application':raise ValueError('Unsupported engine application')
  spec=dict(Id=port['Id'],PackageName=pkg,ApkSha256=port['ApkSha256'],ApkSize=port['ApkSize'],MainActivity='',Process=':engine_'+port['Id'].replace('-','_'),Components=[],Code=[],Native=[])
  components=[n for n in app['Children'] if n['Tag'] in ['activity','service','provider','receiver']]
  for node in components:
   name=full(node['Attributes']['name'],pkg)
   if name in ['androidx.startup.InitializationProvider','androidx.profileinstaller.ProfileInstallReceiver']:continue
   bridge='io.decompshelf.enginebridge.P_'+port['Id'].replace('-','_')+'_'+str(len(spec['Components']))
   component=dict(Kind=node['Tag'],Name=name,Bridge=bridge,Authority='',OriginalAuthority=node['Attributes'].get('authorities',''))
   if node['Tag']=='provider':component['Authority']='io.github.zeranemesis.cubeshelf.native.'+port['Id']+'.p'+str(len(spec['Components']))
   for child in node['Children']:
    if child['Tag']=='intent-filter' and any(c['Tag']=='action' and c['Attributes'].get('name')=='android.intent.action.MAIN' for c in child['Children']):spec['MainActivity']=name
   spec['Components'].append(component);raw_components[bridge]=node
  if not spec['MainActivity']:raise ValueError('No engine launcher activity: '+port['Id'])
  with zipfile.ZipFile(apk) as z:
   for name in z.namelist():
    if name.startswith('lib/arm64-v8a/') and name.endswith('.so'):
     data=z.read(name);spec['Native'].append(dict(Name=name.rsplit('/',1)[1],Sha256=hashlib.sha256(data).hexdigest(),Size=len(data)))
  specs.append(spec);sources[port['Id']]=apk

 root_classes=output/'classes';clean(root_classes)
 root_sources=list((ROOT/'vendor/native-host/java').rglob('*.java'))
 invoke([javac,'--release','8','-encoding','UTF-8','-classpath',sdkjar,'-d',root_classes,*root_sources])
 hostjar=output/'native-host.jar'
 with zipfile.ZipFile(hostjar,'w',zipfile.ZIP_DEFLATED) as z:
  for file in root_classes.rglob('*.class'):z.write(file,file.relative_to(root_classes).as_posix())
 stubs=output/'stub-sources';bridges=output/'bridge-sources';clean(stubs);clean(bridges)
 stub_files=[];bridge_files=[]
 for spec in specs:
  for c in spec['Components']:
   kind=c['Kind'];base={'activity':'android.app.Activity','service':'android.app.Service','provider':'android.content.ContentProvider','receiver':'android.content.BroadcastReceiver'}[kind]
   if not c['Name'].startswith('android.'):
    pkg,name=c['Name'].rsplit('.',1);stub=stubs/(c['Name'].replace('.','/')+'.java');stub.parent.mkdir(parents=True,exist_ok=True)
    body=''
    if kind=='service':body='public android.os.IBinder onBind(android.content.Intent i){return null;}'
    if kind=='receiver':body='public void onReceive(android.content.Context c,android.content.Intent i){}'
    if kind=='provider':body='public boolean onCreate(){return true;} public android.database.Cursor query(android.net.Uri u,String[] p,String s,String[] a,String o){return null;} public String getType(android.net.Uri u){return null;} public android.net.Uri insert(android.net.Uri u,android.content.ContentValues v){return null;} public int delete(android.net.Uri u,String s,String[] a){return 0;} public int update(android.net.Uri u,android.content.ContentValues v,String s,String[] a){return 0;}'
    stub.write_text(f'package {pkg}; public class {name} extends {base} {{ {body} }}',encoding='utf-8');stub_files.append(stub)
   name=c['Bridge'].rsplit('.',1)[1];file=bridges/(name+'.java')
   body='public void attachBaseContext(android.content.Context c){super.attachBaseContext(EngineRuntime.wrap(c));}' if kind in ['activity','service'] else ''
   if kind=='activity':
    body+='public android.content.res.Resources getResources(){return EngineRuntime.resources();} public android.content.res.AssetManager getAssets(){return getResources().getAssets();} public ClassLoader getClassLoader(){return EngineRuntime.loader();} public android.content.pm.ApplicationInfo getApplicationInfo(){return EngineRuntime.applicationInfo();}'
    body+=f'public void setTheme(int value){{super.setTheme(EngineRuntime.theme("{c["Name"]}",value));}}'
    body+='public void onCreate(android.os.Bundle b){super.onCreate(b);EngineRuntime.created(this);} public void startActivityForResult(android.content.Intent i,int code,android.os.Bundle b){super.startActivityForResult(EngineRuntime.intent(i),code,b);}'
    body+='public java.io.File getFilesDir(){return EngineRuntime.data();} public java.io.File getCacheDir(){return EngineRuntime.cache();} public java.io.File getExternalFilesDir(String t){return t==null?EngineRuntime.data():new java.io.File(EngineRuntime.data(),t);}'
   elif kind=='provider':body='public void attachInfo(android.content.Context c,android.content.pm.ProviderInfo i){super.attachInfo(EngineRuntime.wrap(c),ModulePackages.getProviderInfoUnchecked(c.getPackageManager(),i));}'
   elif kind=='receiver':body='public void onReceive(android.content.Context c,android.content.Intent i){super.onReceive(EngineRuntime.wrap(c),i);}'
   file.write_text(f'package io.decompshelf.enginebridge; import io.decompshelf.enginehost.*; public class {name} extends {c["Name"]} {{ {body} }}',encoding='utf-8');bridge_files.append(file)
 stub_classes=output/'stub-classes';clean(stub_classes)
 invoke([javac,'--release','8','-encoding','UTF-8','-classpath',sdkjar,'-d',stub_classes,*dict.fromkeys(stub_files)])
 stubjar=output/'stubs.jar'
 with zipfile.ZipFile(stubjar,'w') as z:
  for file in stub_classes.rglob('*.class'):z.write(file,file.relative_to(stub_classes).as_posix())
 bridge_classes=output/'bridge-classes';clean(bridge_classes)
 invoke([javac,'--release','8','-encoding','UTF-8','-classpath',os.pathsep.join(map(str,[stubjar,sdkjar,hostjar])),'-d',bridge_classes,*bridge_files])
 bridgejar=output/'bridges.jar'
 with zipfile.ZipFile(bridgejar,'w') as z:
  for file in bridge_classes.rglob('*.class'):z.write(file,file.relative_to(bridge_classes).as_posix())
 dex=output/'dex';dex.mkdir(exist_ok=True)
 invoke([java,'-cp',d8,'com.android.tools.r8.D8','--min-api','28','--lib',sdkjar,'--lib',stubjar,'--lib',hostjar,'--output',dex,bridgejar])
 shutil.copyfile(dex/'classes.dex',assets/'bridges.dex');bridgehash=sha(assets/'bridges.dex')
 for spec in specs:
  spec['BridgeSha256']=bridgehash
  adapt_engine(spec,sources[spec['Id']],cache,assets,java,tools,xdelta)
  spec['Revision']=hashlib.sha256(json.dumps(spec,sort_keys=True).encode()).hexdigest()
 (assets/'hosts.json').write_text(json.dumps(specs,ensure_ascii=False,indent=2),encoding='utf-8')
 tree=ET.parse(ROOT/'src/CubeShelf.Android/Properties/AndroidManifest.xml');application=tree.getroot().find('application');attr(application,'appComponentFactory','io.decompshelf.enginehost.HostFactory')
 application.set('{'+TOOLS+'}replace','android:appComponentFactory')
 orientations={0:'landscape',1:'portrait',4:'sensor',6:'sensorLandscape',7:'sensorPortrait',10:'fullSensor',11:'userLandscape',12:'userPortrait',13:'fullUser',14:'locked'}
 for spec in specs:
  for c in spec['Components']:
   node=ET.SubElement(application,c['Kind']);attr(node,'name',c['Bridge']);attr(node,'exported','false');attr(node,'process',spec['Process'])
   original=raw_components[c['Bridge']];attrs=original['Attributes']
   if c['Kind']=='activity':
    attr(node,'theme','@android:style/Theme.Black.NoTitleBar.Fullscreen');attr(node,'excludeFromRecents','true');attr(node,'taskAffinity','io.github.zeranemesis.cubeshelf.native.engine_'+spec['Id'].replace('-','_'));attr(node,'enableOnBackInvokedCallback','false')
    attr(node,'configChanges','keyboard|keyboardHidden|navigation|orientation|screenSize|smallestScreenSize|screenLayout|uiMode|layoutDirection|locale|fontScale|density|touchscreen|colorMode|mcc|mnc')
    attr(node,'screenOrientation',orientations.get(int(attrs.get('screenOrientation','6'),0),'sensorLandscape'))
    for metadata in original['Children']:
     values=metadata['Attributes']
     if metadata['Tag']=='meta-data' and values.get('name') in ['android.app.lib_name','android.app.func_name']:
      child=ET.SubElement(node,'meta-data');attr(child,'name',values['name']);attr(child,'value',values['value'])
   if c['Kind']=='provider':
    attr(node,'authorities',c['Authority']);attr(node,'grantUriPermissions','true')
   if c['Kind']=='service' and attrs.get('foregroundServiceType'):
    attr(node,'foregroundServiceType','specialUse' if int(attrs['foregroundServiceType'],0)&0x40000000 else 'dataSync')
    if attrs.get('process'):attr(node,'process',spec['Process']+'__service')
    property=ET.SubElement(node,'property');attr(property,'name','android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE');attr(property,'value','User-requested preparation of game files inside DecompShelf')
 tree.write(output/'AndroidManifest.xml',encoding='utf-8',xml_declaration=True)
 print('Prepared',len(specs),'private Android engines',flush=True)

def adapt_engine(spec,apk,cache,assets,java,tools,xdelta):
 work=cache/spec['Id'];patches=assets/'patches'/spec['Id'];patches.mkdir(parents=True,exist_ok=True)
 descriptor={"L"+c['Name'].replace('.','/')+';':"L"+c['Bridge'].replace('.','/')+';' for c in spec['Components']}
 services=[c['Name'] for c in spec['Components'] if c['Kind']=='service']
 with zipfile.ZipFile(apk) as z:
  for member in z.namelist():
   if not re.fullmatch(r'classes\d*\.dex',member):continue
   source=work/member;source.write_bytes(z.read(member));smali=work/(member+'.smali');stamp=work/(member+'.adapted.sha')
   target=work/('adapted-'+member)
   key=adapter_key(sha(source),descriptor)
   if services:key=hashlib.sha256((key+inspect.getsource(adapt_foreground)+json.dumps(services)).encode()).hexdigest()
   if spec['Id']=='snes-alttp':key=hashlib.sha256((key+inspect.getsource(adapt_final_overrides)).encode()).hexdigest()
   if not(target.exists() and stamp.exists() and stamp.read_text()==key):
    clean(smali)
    invoke([java,'-Xmx1g','-cp',tools,'org.jf.baksmali.Main','disassemble',source,'-o',smali,'--jobs','1'])
    for file in smali.rglob('*.smali'):
     file=readable(file)
     text=file.read_text(encoding='utf-8');adapted=adapt_smali(text,descriptor)
     if services:adapted=adapt_foreground(adapted,services)
     if spec['Id']=='snes-alttp':adapted=adapt_final_overrides(adapted)
     if adapted!=text:file.write_text(adapted,encoding='utf-8')
    invoke([java,'-Xmx1g','-cp',tools,'org.jf.smali.Main','assemble',smali,'-o',target,'--jobs','1','--api','34']);stamp.write_text(key)
   patch=patches/(member+'.vcdiff')
   invoke([xdelta,'-f','-S','none','-e','-s',source,target,patch])
   spec['Code'].append(dict(Name=member,SourceSha256=sha(source),Sha256=sha(target),Size=target.stat().st_size,Patch='nativehost/patches/'+spec['Id']+'/'+member+'.vcdiff',PatchSha256=sha(patch)))
 print('Adapted',spec['Id'],flush=True)

def adapter_key(source_sha,descriptor):
 recipe=inspect.getsource(adapt_smali)+inspect.getsource(adapt_calls)
 return hashlib.sha256((source_sha+json.dumps(descriptor,sort_keys=True)+recipe).encode()).hexdigest()

def adapt_smali(text,descriptor):
 first=re.search(r'^\.class (.*?)(L[^;]+;)',text,re.M)
 if first and first[2] in descriptor:
  text=re.sub(r'^(\.class|\.method) (.*?)\bfinal\s+',r'\1 \2',text,flags=re.M)
 return adapt_calls(text,descriptor)

def adapt_foreground(text,services):
 for name in services:
  for signature in ['(ILandroid/app/Notification;)V','(ILandroid/app/Notification;I)V']:
   pattern=r'invoke-virtual(/range)? (\{[^}]+\}), L'+re.escape(name.replace('.','/'))+';->startForeground'+re.escape(signature)
   text=re.sub(pattern,lambda m:'invoke-static'+(m[1] or '')+' '+m[2]+', Lio/decompshelf/enginehost/ModulePackages;->startForeground(Landroid/app/Service;'+signature[1:],text)
 return text

def adapt_final_overrides(text):
 # R8 finalized inherited Activity methods in the pinned Zelda3 package.
 methods=['attachBaseContext(Landroid/content/Context;)V','getResources()Landroid/content/res/Resources;','getAssets()Landroid/content/res/AssetManager;','getClassLoader()Ljava/lang/ClassLoader;','getApplicationInfo()Landroid/content/pm/ApplicationInfo;','setTheme(I)V','onCreate(Landroid/os/Bundle;)V','startActivityForResult(Landroid/content/Intent;ILandroid/os/Bundle;)V','getFilesDir()Ljava/io/File;','getCacheDir()Ljava/io/File;','getExternalFilesDir(Ljava/lang/String;)Ljava/io/File;']
 pattern=r'^(\.method [^\n]*?)\bfinal\s+([^\n]*(?:'+'|'.join(re.escape(m) for m in methods)+r'))$'
 return re.sub(pattern,r'\1\2',text,flags=re.M)

def adapt_calls(text,descriptor):
 for original,bridge in descriptor.items():
  text=re.sub(r'(const-class\s+\w+,\s*)'+re.escape(original),lambda m:m[1]+bridge,text)
 # Legacy external-storage APIs now address the owned, per-game storage only.
 text=text.replace('Landroid/os/Environment;->getExternalStorageDirectory()', 'Lio/decompshelf/enginehost/OwnedStorage;->getExternalStorageDirectory()')
 text=text.replace('Landroid/os/Environment;->getExternalStoragePublicDirectory(', 'Lio/decompshelf/enginehost/OwnedStorage;->getExternalStoragePublicDirectory(')
 text=text.replace('Landroid/os/Environment;->isExternalStorageManager(', 'Lio/decompshelf/enginehost/OwnedStorage;->isExternalStorageManager(')
 text=text.replace('Landroidx/core/content/ContextCompat;->checkSelfPermission(', 'Lio/decompshelf/enginehost/OwnedStorage;->checkSelfPermission(')
 text=text.replace('Landroidx/core/content/FileProvider;->getUriForFile(', 'Lio/decompshelf/enginehost/ModulePackages;->getUriForFile(')
 methods={'getApplicationInfo':'(Ljava/lang/String;I)Landroid/content/pm/ApplicationInfo;','getPackageInfo':'(Ljava/lang/String;I)Landroid/content/pm/PackageInfo;','getActivityInfo':'(Landroid/content/ComponentName;I)Landroid/content/pm/ActivityInfo;','getServiceInfo':'(Landroid/content/ComponentName;I)Landroid/content/pm/ServiceInfo;','getProviderInfo':'(Landroid/content/ComponentName;I)Landroid/content/pm/ProviderInfo;','resolveContentProvider':'(Ljava/lang/String;I)Landroid/content/pm/ProviderInfo;','getLaunchIntentForPackage':'(Ljava/lang/String;)Landroid/content/Intent;','getDrawable':'(Ljava/lang/String;ILandroid/content/pm/ApplicationInfo;)Landroid/graphics/drawable/Drawable;','getText':'(Ljava/lang/String;ILandroid/content/pm/ApplicationInfo;)Ljava/lang/CharSequence;','getXml':'(Ljava/lang/String;ILandroid/content/pm/ApplicationInfo;)Landroid/content/res/XmlResourceParser;'}
 for name,signature in methods.items():
  pattern=r'invoke-virtual(/range)? (\{[^}]+\}), Landroid/content/pm/PackageManager;->'+re.escape(name+signature)
  text=re.sub(pattern,lambda m:'invoke-static'+(m[1] or '')+' '+m[2]+', Lio/decompshelf/enginehost/ModulePackages;->'+name+'(Landroid/content/pm/PackageManager;'+signature[1:],text)
 return text

if __name__=='__main__':main()
