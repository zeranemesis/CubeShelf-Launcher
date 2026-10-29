"""Fetch the named Wikimedia console photos, keeping source and license metadata."""
import hashlib
import html
import json
import re
import time
from pathlib import Path
from urllib.parse import urlsplit
import requests

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'src/CubeShelf.Desktop/Assets/Consoles'
FILES = {
    'gamecube':'GameCube-Console-Set.png','wii':'Wii-Console.png',
    'switch':'Nintendo-Switch-Console-Docked-wJoyConRB.jpg','n64':'N64-Console-Set.png',
    'nes':'NES-Console-Set.png','snes':'SNES-Mod1-Console-Set.jpg',
    'gb':'Game-Boy-FL.png','gbc':'Nintendo-Game-Boy-Color-FL.png',
    'gba':'Nintendo-Game-Boy-Advance-Purple-FL.jpg','nds':'Nintendo-DS-Fat-Blue.jpg',
    '3ds':'Nintendo-3DS-AquaOpen.jpg','wiiu':'Wii U Console and Gamepad.png',
    'virtualboy':'Virtual-Boy-Set.png','mastersystem':'Sega-Master-System-Set.png',
    'megadrive':'Sega-Genesis-Mk2-6button.jpg','gamegear':'Sega-Game-Gear-WB.png',
    '32x':'Sega-Genesis-Model2-32X.jpg','segacd':'Sega-CD-Model1-Set.jpg',
    'saturn':'Sega-Saturn-Console-Set-Mk1.png','dreamcast':'Dreamcast-Console-Set.png',
    'ps1':'PSX-Console-wController.png','ps2':'PS2-Versions.png',
    'ps3':'PS3-Fat-Console-Set.jpg','ps4':'PS4-Console-wDS4.png','psp':'Sony-PSP-1000-Body.png',
    'xbox':'Xbox-Console-Set.png','xbox360':'Xbox-360-Pro-wController.jpg',
    'neogeo':'Neo-Geo-AES-Console-Set.jpg','cdi':'Philips-CDi-220-wController-FL.jpg',
    'arcade':'Donkey Kong arcade.jpg',
}

def get(url, **kwargs):
    for attempt in range(3):
        response = requests.get(url, headers={'User-Agent':'DecompShelf/1.0 (console illustration attribution)'}, timeout=40, **kwargs)
        if response.status_code == 200:
            return response
        time.sleep(10 * (attempt + 1))
    response.raise_for_status()

def plain(value):
    return html.unescape(re.sub('<[^>]+>', '', value)).strip()

if __name__ == '__main__':
    DEST.mkdir(parents=True, exist_ok=True)
    sources=[]
    missing=[]
    data=get('https://commons.wikimedia.org/w/api.php', params={
        'action':'query','format':'json','titles':'|'.join('File:'+name for name in FILES.values()),
        'prop':'imageinfo','iiprop':'url|extmetadata','iiurlwidth':480,
    }).json()
    pages={page['title']:page for page in data['query']['pages'].values()}
    for console, filename in FILES.items():
        page=pages.get('File:'+filename,{})
        if not page.get('imageinfo'):
            missing.append((console,filename));continue
        info=page['imageinfo'][0]
        meta=info['extmetadata']
        license=plain(meta.get('LicenseShortName',{}).get('value',''))
        if not ('public domain' in license.lower() or license.lower().startswith('cc')):
            missing.append((console,'license: '+license));continue
        url=info.get('thumburl',info['url']).split('?')[0]
        suffix=Path(urlsplit(url).path).suffix.lower()
        if suffix not in ('.png','.jpg','.jpeg','.webp'):
            raise ValueError('Unexpected image format: '+url)
        name=console+suffix
        file=DEST/name
        payload=file.read_bytes() if file.exists() else get(url).content
        file.write_bytes(payload)
        sources.append({'Console':console,'File':name,'Source':info['descriptionurl'],
            'ImageUrl':url,'Author':plain(meta.get('Artist',{}).get('value','')),
            'License':license,'LicenseUrl':meta.get('LicenseUrl',{}).get('value',''),
            'Sha256':hashlib.sha256(payload).hexdigest()})
        (DEST/'sources.json').write_text(json.dumps(sources,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print(console, name, len(payload), license, flush=True)
        time.sleep(1)
    (DEST/'sources.json').write_text(json.dumps(sources,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    credits=['# Photos des consoles','', 'Photos téléchargées depuis Wikimedia Commons ; miniatures redimensionnées sans autre modification.','']
    for source in sources:
        credits.append(f"- **{source['Console']}** : [{source['Author']}]({source['Source']}) — [{source['License']}]({source['LicenseUrl'] or source['Source']}).")
    (DEST/'CREDITS.md').write_text('\n'.join(credits)+'\n',encoding='utf-8')
    print('MISSING',missing,flush=True)
