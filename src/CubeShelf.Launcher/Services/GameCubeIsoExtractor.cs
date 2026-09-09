using System.Buffers.Binary;

namespace CubeShelf.Launcher.Services;

public static class GameCubeIsoExtractor
{
    private sealed record FstEntry(bool IsDirectory, int NameOffset, uint OffsetOrParent, uint SizeOrNext);

    public static async Task ExtractFilesAsync(string isoPath, string destinationFilesDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationFilesDirectory);

        await using var stream = new FileStream(
            isoPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, useAsync: true);

        if (stream.Length < 0x42C)
            throw new InvalidDataException("Image GameCube invalide : en-tête trop court.");

        var header = new byte[0x42C];
        await ReadExactlyAsync(stream, header, cancellationToken);

        var fstOffset = ReadBe32(header, 0x424);
        var fstSize = ReadBe32(header, 0x428);

        if (fstOffset == 0 || fstSize == 0 || (long)fstOffset + fstSize > stream.Length)
            throw new InvalidDataException("FST GameCube invalide.");

        stream.Position = fstOffset;
        var root = new byte[12];
        await ReadExactlyAsync(stream, root, cancellationToken);

        var rootWord = ReadBe32(root, 0);
        var entryCount = ReadBe32(root, 8);

        if ((rootWord & 0xFF000000) == 0 || entryCount == 0 || entryCount > 500000)
            throw new InvalidDataException("Table de fichiers GameCube invalide.");

        var tableBytes = checked((int)entryCount * 12);
        var fst = new byte[tableBytes];
        stream.Position = fstOffset;
        await ReadExactlyAsync(stream, fst, cancellationToken);

        var entries = new FstEntry[entryCount];
        for (int i=0;i<entryCount;i++)
        {
            var baseOffset = i * 12;
            var word = ReadBe32(fst, baseOffset);

            entries[i]=new FstEntry(
                (word & 0xFF000000)!=0,
                (int)(word & 0x00FFFFFF),
                ReadBe32(fst, baseOffset + 4),
                ReadBe32(fst, baseOffset + 8));
        }

        var stringsOffset=fstOffset+(uint)tableBytes;
        var stringsLength=(int)Math.Max(0, Math.Min(stream.Length-stringsOffset, (long)fstSize-tableBytes));
        var strings=new byte[stringsLength];
        stream.Position=stringsOffset;
        await ReadExactlyAsync(stream, strings, cancellationToken);

        string Name(int offset)
        {
            if (offset<0 || offset>=strings.Length) return "_invalid";
            int end=offset;
            while(end<strings.Length && strings[end]!=0) end++;
            var name=Encoding.UTF8.GetString(strings,offset,end-offset);
            foreach(var c in Path.GetInvalidFileNameChars()) name=name.Replace(c,'_');
            return string.IsNullOrWhiteSpace(name) || name=="." || name==".." ? "_" : name;
        }

        var files=new List<(string Rel,uint Offset,uint Size)>();

        void Walk(int start,int end,string dir)
        {
            int i=start;
            while(i<end && i<entries.Length)
            {
                var e=entries[i];
                var n=Name(e.NameOffset);
                if(e.IsDirectory)
                {
                    int next=checked((int)e.SizeOrNext);
                    if(next<=i || next>entries.Length) throw new InvalidDataException("Arborescence FST invalide.");
                    Walk(i+1,next,Path.Combine(dir,n));
                    i=next;
                }
                else
                {
                    files.Add((Path.Combine(dir,n),e.OffsetOrParent,e.SizeOrNext));
                    i++;
                }
            }
        }
        Walk(1, checked((int)entryCount), "");

        long total=files.Sum(x=>(long)x.Size), done=0;
        var buffer=new byte[1024*1024];

        foreach(var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest=Path.GetFullPath(Path.Combine(destinationFilesDirectory,file.Rel));
            var rootPath=Path.GetFullPath(destinationFilesDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!dest.StartsWith(rootPath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chemin FST non sûr.");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            stream.Position=file.Offset;

            await using var output=new FileStream(dest,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true);
            long remaining=file.Size;

            while(remaining>0)
            {
                int read=await stream.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,remaining)), cancellationToken);
                if(read<=0) throw new EndOfStreamException();
                await output.WriteAsync(buffer.AsMemory(0,read), cancellationToken);
                remaining-=read; done+=read;
                if(total>0) progress?.Report((double)done/total);
            }
        }

        progress?.Report(1.0);
    }


    private static uint ReadBe32(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(
            new ReadOnlySpan<byte>(buffer, offset, 4));

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset=0;
        while(offset<buffer.Length)
        {
            int read=await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if(read<=0) throw new EndOfStreamException();
            offset+=read;
        }
    }
}
