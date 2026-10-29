"""Download console wordmarks from Wikimedia Commons and record their provenance."""
import hashlib
import json
import time
from pathlib import Path
import importlib.util
_spec=importlib.util.spec_from_file_location("console_photos",Path(__file__).with_name("import-console-photos.py"))
_photos=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_photos)
get,plain=_photos.get,_photos.plain

FILES={'gamecube': 'Nintendo GameCube symbol logo.svg', 'wii': 'Wii.svg', 'switch': 'Nintendo Switch logo.svg', 'n64': 'Nintendo 64 (logo).svg', 'nes': 'NES logo.svg', 'snes': 'Super Nintendo Entertainment System logo.svg', 'gb': 'Nintendo Game Boy Logo.svg', 'gbc': 'Game Boy Color logo.svg', 'gba': 'Game Boy Advance logo.svg', 'nds': 'Nintendo DS Logo.svg', '3ds': 'Nintendo 3DS logo.svg', 'wiiu': 'WiiU.svg', 'virtualboy': 'Virtualboy logo.svg', 'mastersystem': 'Master System Logo.svg', 'megadrive': 'JP MegaDrive Logo.gif', 'gamegear': 'Game gear eu logo.svg', '32x': 'Sega 32X logo.svg', 'segacd': 'Sega CD Logo.svg', 'saturn': 'Sega Saturn USA logo.svg', 'dreamcast': 'Dreamcast logo.svg', 'ps1': 'PlayStation logo.svg', 'ps2': 'PlayStation 2 logo.svg', 'ps3': 'PlayStation 3 logo.svg', 'ps4': 'PlayStation 4 logo.svg', 'psp': 'PSP Logo.svg', 'xbox': 'Xbox Logo 2001.svg', 'xbox360': 'Xbox 360 logo.svg', 'neogeo': 'Neo Geo logo.svg', 'cdi': 'Philips CD-i logo.svg'}

if __name__ == '__main__':
    destination=Path(__file__).resolve().parents[1]/'src/CubeShelf.Desktop/Assets/Brand/Consoles'
    destination.mkdir(parents=True,exist_ok=True)
    data=get('https://commons.wikimedia.org/w/api.php',params={'action':'query','format':'json',
        'titles':'|'.join('File:'+name for name in FILES.values()),'prop':'imageinfo','iiprop':'url|extmetadata','iiurlwidth':256}).json()
    pages={p['title']:p for p in data['query']['pages'].values()}
    FILES.pop('n64',None)  # The existing vector N64 symbol is already used by the app.
    sources=[]
    for console,name in FILES.items():
        page=pages.get('File:'+name,{})
        if not page.get('imageinfo'):raise ValueError('Missing logo: '+name)
        info=page['imageinfo'][0];meta=info['extmetadata']
        url=info.get('thumburl',info['url']).split('?')[0]
        file=destination/(console+('.gif' if url.lower().endswith('.gif') else '.png'))
        payload=file.read_bytes() if file.exists() else get(url).content
        if not payload.startswith((b'\x89PNG\r\n\x1a\n',b'GIF87a',b'GIF89a')):raise ValueError('Unexpected raster format: '+url)
        file.write_bytes(payload)
        license=plain(meta.get('LicenseShortName',{}).get('value',''))
        if not license:raise ValueError('Missing license: '+name)
        sources.append({'Console':console,'File':file.name,'Source':info['descriptionurl'],'ImageUrl':url,
            'Author':plain(meta.get('Artist',{}).get('value','')),'License':license,
            'LicenseUrl':meta.get('LicenseUrl',{}).get('value',''),'Sha256':hashlib.sha256(payload).hexdigest()})
        (destination/'sources.json').write_text(json.dumps(sources,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print(console,len(payload),license,flush=True)
        time.sleep(1)
    credits=['# Logos des consoles','', 'Logos issus de Wikimedia Commons, redimensionnés sans autre modification. Les marques appartiennent à leurs propriétaires respectifs.','']
    for source in sources:
        credits.append(f"- **{source['Console']}** : [{source['Author']}]({source['Source']}) — [{source['License']}]({source['LicenseUrl'] or source['Source']}).")
    (destination/'CREDITS.md').write_text('\n'.join(credits)+'\n',encoding='utf-8')
