using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Beeps.Tests.Support;

/// <summary>
/// Writes small IPv4 MaxMind DB files for the IP location tests, so they need neither the 100 MB database nor a
/// download. Only what those records use: maps, strings, arrays and unsigned integers, 24-bit search tree records.
/// </summary>
internal static class TestMmdb
{
    private static readonly byte[] MetadataMarker = [0xAB, 0xCD, 0xEF, .. "MaxMind.com"u8];

    /// <summary>A database with these networks (<c>203.0.113.0/24</c>) and records (nested dictionaries, lists, strings).</summary>
    public static byte[] Build(params (string Network, object Record)[] networks)
    {
        var data = new MemoryStream();
        var tree = new List<int[]> { new[] { -1, -1 } };
        var leaves = new List<(int Node, int Side, int Offset)>();
        foreach (var (network, record) in networks)
        {
            var offset = (int)data.Length;
            Write(data, record);
            var (address, prefix) = Parse(network);
            var node = 0;
            for (var bit = 0; bit < prefix; bit++)
            {
                var side = (address[bit / 8] >> (7 - (bit % 8))) & 1;
                if (bit == prefix - 1)
                {
                    leaves.Add((node, side, offset));
                    break;
                }

                if (tree[node][side] < 0)
                {
                    tree.Add([-1, -1]);
                    tree[node][side] = tree.Count - 1;
                }

                node = tree[node][side];
            }
        }

        var nodeCount = tree.Count;
        foreach (var (node, side, offset) in leaves)
        {
            tree[node][side] = nodeCount + 16 + offset;
        }

        var file = new MemoryStream();
        foreach (var node in tree)
        {
            foreach (var record in node)
            {
                var value = record < 0 ? nodeCount : record;
                file.Write([(byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            }
        }

        file.Write(new byte[16]);
        data.WriteTo(file);
        file.Write(MetadataMarker);
        Write(file, new Dictionary<string, object>
        {
            ["node_count"] = (uint)nodeCount,
            ["record_size"] = (ushort)24,
            ["ip_version"] = (ushort)4,
            ["database_type"] = "Test",
            ["languages"] = new List<object> { "en", "zh-CN" },
            ["binary_format_major_version"] = (ushort)2,
            ["binary_format_minor_version"] = (ushort)0,
            ["build_epoch"] = (ulong)1_700_000_000,
            ["description"] = new Dictionary<string, object> { ["en"] = "beeps tests" },
        });
        return file.ToArray();
    }

    /// <summary>A place as the database names it: <c>{ names: { en: …, zh-CN: … } }</c>.</summary>
    public static Dictionary<string, object> Place(params (string Language, string Name)[] names) =>
        new() { ["names"] = names.ToDictionary(n => n.Language, n => (object)n.Name) };

    private static (byte[] Address, int Prefix) Parse(string network)
    {
        var parts = network.Split('/');
        return (IPAddress.Parse(parts[0]).GetAddressBytes(), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Write(Stream stream, object value)
    {
        switch (value)
        {
            case string text:
                var bytes = Encoding.UTF8.GetBytes(text);
                Control(stream, 2, bytes.Length);
                stream.Write(bytes);
                break;
            case ushort number:
                Unsigned(stream, 5, number);
                break;
            case uint number:
                Unsigned(stream, 6, number);
                break;
            case ulong number:
                Unsigned(stream, 9, number);
                break;
            case IDictionary<string, object> map:
                Control(stream, 7, map.Count);
                foreach (var (key, item) in map)
                {
                    Write(stream, key);
                    Write(stream, item);
                }

                break;
            case IList<object> array:
                Control(stream, 11, array.Count);
                foreach (var item in array)
                {
                    Write(stream, item);
                }

                break;
            default:
                throw new ArgumentException($"Cannot write {value.GetType()}.", nameof(value));
        }
    }

    private static void Unsigned(Stream stream, int type, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        var significant = bytes.TrimStart((byte)0);
        Control(stream, type, significant.Length);
        stream.Write(significant);
    }

    /// <summary>The type and size byte(s); types past 7 are "extended", sizes past 28 take extra bytes (up to 284 here).</summary>
    private static void Control(Stream stream, int type, int size)
    {
        var sizeBits = size < 29 ? size : 29;
        stream.WriteByte((byte)(((type <= 7 ? type : 0) << 5) | sizeBits));
        if (type > 7)
        {
            stream.WriteByte((byte)(type - 7));
        }

        if (size >= 29)
        {
            stream.WriteByte((byte)(size - 29));
        }
    }
}
