"""Import reviewed regional/full-title matches; never use fuzzy title matching."""
import importlib.util
import json
from pathlib import Path
import urllib.parse

spec=importlib.util.spec_from_file_location('console_covers',Path(__file__).with_name('import-console-covers.py'))
covers=importlib.util.module_from_spec(spec)
spec.loader.exec_module(covers)

# Left: catalogue title. Right: the same release's full or regional box-art title.
ALIASES={
    '3ds':{'Ocarina of Time 3D':'Legend of Zelda, The - Ocarina of Time 3D'},
    'gba':{'Pokémon Emerald':'Pokemon - Emerald Version'},
    'nds':{'Pokémon Black 2':'Pokemon - Black Version 2','Pokémon White 2':'Pokemon - White Version 2'},
    'ps1':{'Bloody Roar 2':'Bloody Roar II','Crash Team Racing':'CTR - Crash Team Racing',
        "Disney's Hercules":"Disney's Hercules Action Game",'Einhänder':'Einhaender',
        'Hogs of War / Frontschweine':'Hogs of War',
        "JoJo's Bizarre Adventure: Heritage for the Future":"JoJo's Bizarre Adventure",
        'PaRappaTheRapper':'PaRappa the Rapper','Rockman Dash':'Rockman Dash - Hagane no Boukenshin',
        'Simpsons Wrestling Recompiled':'Simpsons Wrestling, The','ToyStory2':'Disney-Pixar Toy Story 2 - Buzz Lightyear to the Rescue!'},
    'saturn':{'Shining Force III Scenario 1':'Shining Force III - Scenario 1 - Outo no Kyoshin',
        'Shining Force III Scenario 2':'Shining Force III - Scenario 2 - Nerawareta Miko',
        'Shining Force III Scenario 3':'Shining Force III - Scenario 3 - Hyouheki no Jashinguu'},
    'neogeo':{'Metal Slug X':'Metal Slug X - Super Vehicle-001'},
    'xbox':{"The Nightmare Before Christmas: Oogie's Revenge":"Tim Burton's - The Nightmare Before Christmas - Oogie's Revenge"},
}

if __name__=='__main__':
    file=covers.CATALOGS/'covers.json'
    registry=json.loads(file.read_text(encoding='utf-8-sig'))
    for console,aliases in ALIASES.items():
        titles={port['Title'] for port in json.loads((covers.CATALOGS/(console+'.json')).read_text(encoding='utf-8-sig'))}
        assert set(aliases)<=titles,console
        cache=covers.ROOT/'.tools/console-cover-index'/(console+'.json')
        images=json.loads(cache.read_text(encoding='utf-8'))
        for title,alias in aliases.items():
            matches=[path for path in images if covers.normalize(Path(path).stem)==covers.normalize(alias)]
            matches.sort(key=lambda path:('(USA' not in path,'(Europe' not in path,len(path)))
            if not matches:
                print('No exact reviewed match',console,title,flush=True);continue
            path=matches[0]
            url=f'https://raw.githubusercontent.com/libretro-thumbnails/{covers.REPOS[console]}/master/'+urllib.parse.quote(path)
            relative=f'Assets/Covers/{console}/{covers.normalize(title)}.png'
            target=covers.ROOT/'src/CubeShelf.Desktop'/relative
            try:
                data=target.read_bytes() if target.exists() else covers.read(url,8_000_000)
                if not data.startswith(b'\x89PNG\r\n\x1a\n'):raise ValueError('Invalid PNG')
                target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
                record={'Console':console,'Title':title,'GameTdbId':'','Source':'libretro-thumbnails','OriginUrl':url,
                    'MatchedTitle':alias,'Front':relative,'Back':'','Spine':''}
                registry=[r for r in registry if (r['Console'],r['Title'])!=(console,title)]+[record]
                print('Added',console,title,flush=True)
            except (OSError,ValueError) as error:
                print('Unavailable',console,title,type(error).__name__,flush=True)
    assert len({(r['Console'],r['Title']) for r in registry})==len(registry)
    file.write_text(json.dumps(registry,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    available={(r['Console'],r['Title']) for r in registry if (covers.ROOT/'src/CubeShelf.Desktop'/r.get('Front','')).is_file()}
    missing_file=covers.CATALOGS/'missing-console-covers.json'
    missing=[r for r in json.loads(missing_file.read_text(encoding='utf-8')) if (r['Console'],r['Title']) not in available]
    missing_file.write_text(json.dumps(missing,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Remaining missing covers:',len(missing),flush=True)
