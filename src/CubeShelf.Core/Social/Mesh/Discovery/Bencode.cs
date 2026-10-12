using System.Text;

namespace CubeShelf.Core.Social.Mesh.Discovery;

/// <summary>
/// The encoding BitTorrent's DHT speaks: integers, byte strings, lists and dictionaries.
///
/// Everything decoded here comes from strangers on the Internet, so the decoder is strict and
/// bounded: one datagram at most, a shallow nesting, a limited number of items, no leading zeros
/// or negative zero in integers, dictionary keys as byte strings. Anything else is refused whole.
/// </summary>
public static class Bencode
{
    public const int MaximumInput = 2048;
    public const int MaximumDepth = 8;
    public const int MaximumItems = 512;

    // ------------------------------------------------------------------ encoding

    /// <summary>
    /// Encodes <see cref="long"/>/<see cref="int"/>, <see cref="byte"/>[], <see cref="string"/> (as UTF-8),
    /// lists of those, and dictionaries keyed by string. Keys are written in byte order, as the
    /// format requires.
    /// </summary>
    public static byte[] Encode(object value)
    {
        using var stream = new MemoryStream();
        Write(stream, value);
        return stream.ToArray();
    }

    private static void Write(Stream stream, object value)
    {
        switch (value)
        {
            case int number:
                Write(stream, (long)number);
                break;
            case long number:
                WriteAscii(stream, "i" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "e");
                break;
            case byte[] bytes:
                WriteAscii(stream, bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":");
                stream.Write(bytes);
                break;
            case string text:
                Write(stream, Encoding.UTF8.GetBytes(text));
                break;
            case IDictionary<string, object> dictionary:
                stream.WriteByte((byte)'d');
                foreach (var (key, item) in dictionary.OrderBy(pair => Encoding.UTF8.GetBytes(pair.Key), ByteOrder.Instance))
                {
                    Write(stream, key);
                    Write(stream, item);
                }
                stream.WriteByte((byte)'e');
                break;
            case IEnumerable<object> list:
                stream.WriteByte((byte)'l');
                foreach (var item in list) Write(stream, item);
                stream.WriteByte((byte)'e');
                break;
            default:
                throw new ArgumentException("Type non encodable en bencode : " + value.GetType().Name, nameof(value));
        }
    }

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }

    // ------------------------------------------------------------------ decoding

    /// <summary>
    /// Decodes one value that spans the whole input, or returns null. Byte strings decode to
    /// <see cref="byte"/>[], integers to <see cref="long"/>, lists to <see cref="List{T}"/> of
    /// object, dictionaries to <see cref="Dictionary{TKey,TValue}"/> keyed by the key's bytes read
    /// as Latin-1 (so any key round-trips, and ASCII keys read as themselves).
    /// </summary>
    public static object? Decode(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty || input.Length > MaximumInput) return null;
        var reader = new Reader(input);
        var value = reader.Value(0);
        return value is not null && reader.AtEnd ? value : null;
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _input;
        private int _position;
        private int _items;

        public Reader(ReadOnlySpan<byte> input)
        {
            _input = input;
            _position = 0;
            _items = 0;
        }

        public bool AtEnd => _position == _input.Length;

        public object? Value(int depth)
        {
            if (depth > MaximumDepth || _position >= _input.Length || ++_items > MaximumItems) return null;
            var first = _input[_position];
            if (first == (byte)'i') return Integer();
            if (first is >= (byte)'0' and <= (byte)'9') return Bytes();
            if (first == (byte)'l') return List(depth);
            if (first == (byte)'d') return Dictionary(depth);
            return null;
        }

        private object? Integer()
        {
            _position++;
            var end = _input[_position..].IndexOf((byte)'e');
            if (end <= 0 || end > 20) return null;
            var digits = _input.Slice(_position, end);
            _position += end + 1;

            var negative = digits[0] == (byte)'-';
            var body = negative ? digits[1..] : digits;
            if (body.IsEmpty || (body[0] == (byte)'0' && (body.Length > 1 || negative))) return null;
            long number = 0;
            foreach (var digit in body)
            {
                if (digit is < (byte)'0' or > (byte)'9') return null;
                if (number > (long.MaxValue - (digit - '0')) / 10) return null;
                number = number * 10 + (digit - '0');
            }
            return negative ? -number : number;
        }

        private byte[]? Bytes()
        {
            var colon = _input[_position..].IndexOf((byte)':');
            if (colon <= 0 || colon > 5) return null;
            var digits = _input.Slice(_position, colon);
            if (digits.Length > 1 && digits[0] == (byte)'0') return null;
            var length = 0;
            foreach (var digit in digits)
            {
                if (digit is < (byte)'0' or > (byte)'9') return null;
                length = length * 10 + (digit - '0');
            }
            _position += colon + 1;
            if (length > _input.Length - _position) return null;
            var bytes = _input.Slice(_position, length).ToArray();
            _position += length;
            return bytes;
        }

        private List<object>? List(int depth)
        {
            _position++;
            var list = new List<object>();
            while (_position < _input.Length && _input[_position] != (byte)'e')
            {
                if (Value(depth + 1) is not { } item) return null;
                list.Add(item);
            }
            if (_position >= _input.Length) return null;
            _position++;
            return list;
        }

        private Dictionary<string, object>? Dictionary(int depth)
        {
            _position++;
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
            while (_position < _input.Length && _input[_position] != (byte)'e')
            {
                if (_input[_position] is < (byte)'0' or > (byte)'9' || Bytes() is not { } key) return null;
                if (Value(depth + 1) is not { } item) return null;
                if (!dictionary.TryAdd(Encoding.Latin1.GetString(key), item)) return null;
            }
            if (_position >= _input.Length) return null;
            _position++;
            return dictionary;
        }
    }
}
