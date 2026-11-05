using System.Text;
using CubeShelf.Core.Library;

internal static class GameFileMatcherTests
{
    internal static void Run()
    {
        CatalogTitle[] catalog=[
            new("sm64-a","Super Mario 64","n64"),new("sm64-b","Super Mario 64","n64"),
            new("oot","The Legend of Zelda: Ocarina of Time","n64"),new("mm","The Legend of Zelda: Majora's Mask","n64"),
            new("mp4","Mario Party 4","gamecube"),new("mp6","Mario Party 6","gamecube"),
            new("emerald","Pokémon Emerald","gba"),new("sm64ds","Super Mario 64 DS","nds"),new("themepark","Theme Park","snes")];
        byte[] N64(string title,int order)
        {
            var rom=new byte[0x400];rom[0]=0x80;rom[1]=0x37;rom[2]=0x12;rom[3]=0x40;Encoding.ASCII.GetBytes(title.PadRight(20)).CopyTo(rom,0x20);
            if(order==1)for(var i=0;i+1<rom.Length;i+=2)(rom[i],rom[i+1])=(rom[i+1],rom[i]);
            if(order==2)for(var i=0;i+3<rom.Length;i+=4){(rom[i],rom[i+3])=(rom[i+3],rom[i]);(rom[i+1],rom[i+2])=(rom[i+2],rom[i+1]);}
            return rom;
        }
        Check(Ids("sm64.z64",N64("SUPER MARIO 64",0)).SequenceEqual(["sm64-a","sm64-b"]),"an N64 header title marks every port of that game");
        Check(Ids("a.v64",N64("SUPER MARIO 64",1)).Length==2&&Ids("a.n64",N64("SUPER MARIO 64",2)).Length==2,"byte-swapped and little-endian N64 dumps are read");
        Check(Ids("x.z64",N64("ZELDA MAJORA'S MASK",0)).SequenceEqual(["mm"]),"a shortened header title still finds its game");
        Check(Ids("Legend of Zelda, The - Ocarina of Time (USA) (Rev 2).z64",N64("THE LEGEND OF ZELDA",0)).SequenceEqual(["oot"]),
            "an ambiguous header falls back to the No-Intro file name");
        Check(Ids("dump.z64",N64("THE LEGEND OF ZELDA",0)).Length==0,"an ambiguous header with an unhelpful name matches nothing");
        var disc=new byte[0x400];disc[0x1C]=0xC2;disc[0x1D]=0x33;disc[0x1E]=0x9F;disc[0x1F]=0x3D;Encoding.ASCII.GetBytes("Mario Party 4").CopyTo(disc,0x20);
        Check(Ids("game.iso",disc).SequenceEqual(["mp4"]),"a GameCube disc header picks the exact numbered title");
        Check(Ids("Mario Party 6 (USA).rvz",[]).SequenceEqual(["mp6"]),"a compressed disc is recognised by its name");
        var gba=new byte[0x400];Encoding.ASCII.GetBytes("POKEMON EMER").CopyTo(gba,0xA0);
        Check(Ids("1986.gba",gba).SequenceEqual(["emerald"]),"a truncated GBA header title matches with accents ignored");
        Check(Ids("Super Mario 64 DS (USA).nds",[]).SequenceEqual(["sm64ds"]),"consoles stay separate for the same words");
        Check(Ids("Theme Park (USA).sfc",[]).SequenceEqual(["themepark"])&&GameFileMatcher.Normalize("The Legend")=="legend","only a leading 'The ' word is dropped");
        Check(Ids("notes.txt",[]).Length==0&&Ids("Mario Party 4.zip",[]).Length==0,"unrelated extensions are ignored");

        var root=Path.Combine(Path.GetTempPath(),"decompshelf-found-"+Guid.NewGuid().ToString("N"));
        try
        {
            async IAsyncEnumerable<GameFileMatcher.ScannedFile> Files()
            {
                yield return new("sm64.z64","C:/roms/sm64.z64",()=>Task.FromResult<Stream>(new MemoryStream(N64("SUPER MARIO 64",0))));
                yield return new("broken.z64","C:/roms/broken.z64",()=>throw new IOException("unreadable"));
                yield return new("readme.txt","C:/roms/readme.txt",()=>throw new InvalidOperationException("must not open"));
                await Task.CompletedTask;
            }
            var seen=0;var found=GameFileMatcher.ScanAsync(Files(),catalog,count=>seen=count).GetAwaiter().GetResult();
            Check(found.Select(f=>f.GameId).SequenceEqual(["sm64-a","sm64-b"])&&seen==2,"the scan reads candidate headers only and survives unreadable files");
            var store=new FoundGamesStore(root);store.Merge(found);store.Merge([new FoundGame("mp4","D:/iso/mp4.iso")]);
            var saved=new FoundGamesStore(root).Load();
            Check(saved.Count==3&&saved["sm64-a"]=="C:/roms/sm64.z64"&&saved["mp4"]=="D:/iso/mp4.iso","scans of several folders accumulate and survive restart");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}

        string[] Ids(string name,byte[] header)=>GameFileMatcher.Identify(name,header,catalog);
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
}
