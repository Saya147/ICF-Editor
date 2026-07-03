using System.Buffers.Binary;
using System.Text;

namespace IcfEditor;

public enum RecordKind { Pack, App, Opt, Patch, InstalledPatch }

public sealed class IcfRecord
{
    // 0x00000102 为普通记录；0x00000201 为 Reader 可显示但不参与子数据 CRC 的特殊记录。
    public uint RecordMarker { get; set; } = 0x00000102;
    public RecordKind Kind { get; set; }
    public string Version { get; set; } = "1.00.00";
    public DateTime Date { get; set; } = DateTime.Now;
    public string RequiredVersion { get; set; } = "111.01.01";
    public string SourceVersion { get; set; } = "1.00.00";
    public DateTime SourceDate { get; set; } = new(2000, 1, 1);
    public string SourceRequiredVersion { get; set; } = "111.01.01";

    public string FileName(string systemId, string appId) => Kind switch
    {
        RecordKind.Pack => $"{appId}_{Version}_{Date:yyyyMMddHHmmss}_0.pack",
        RecordKind.App => $"{systemId}_{Version}_{Date:yyyyMMddHHmmss}_0.app",
        RecordKind.Opt => $"{systemId}_{Version}_{Date:yyyyMMddHHmmss}_0.opt",
        RecordKind.Patch => $"{systemId}_{Version}_{Date:yyyyMMddHHmmss}_1_{SourceVersion}.app",
        _ => $"[已安装状态/Reader隐藏] {systemId}_{Version}_{Date:yyyyMMddHHmmss}_1_{SourceVersion}.app"
    };
}

public sealed class IcfDocument
{
    public string SystemId { get; set; } = "SDEZ";
    public string AppId { get; set; } = "ACA";
    public List<IcfRecord> Records { get; } = new();

    public static IcfDocument Load(string path)
    {
        byte[] encrypted = File.ReadAllBytes(path);
        if (encrypted.Length < 128 || encrypted.Length % 16 != 0) throw new InvalidDataException("文件长度不是有效的 ICF 格式。");
        byte[] data = NativeCodec.Crypt(encrypted, false);
        uint length = Read32(data, 4), count = Read32(data, 0x10);
        if (length != data.Length || 0x40 + count * 0x40 > data.Length) throw new InvalidDataException("ICF 文件头或记录数量无效。");
        if (Read32(data, 0) != Crc32(data.AsSpan(4))) throw new InvalidDataException("主数据 CRC 校验失败。");
        uint sub = 0;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = data.AsSpan(0x40 + i * 0x40, 0x40);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) == 0x00000102) sub ^= Crc32(record);
        }
        if (sub != Read32(data, 0x20)) throw new InvalidDataException("子数据 CRC 校验失败。");

        string ids = Encoding.ASCII.GetString(data, 0x18, 7);
        var doc = new IcfDocument { SystemId = ids[..4].TrimEnd('\0'), AppId = ids[4..].TrimEnd('\0') };
        for (int i = 0; i < count; i++) doc.Records.Add(ParseRecord(data.AsSpan(0x40 + i * 0x40, 0x40)));
        return doc;
    }

    public void Save(string path)
    {
        if (SystemId.Length != 4 || AppId.Length != 3 || !SystemId.All(IsAscii) || !AppId.All(IsAscii))
            throw new InvalidDataException("游戏 ID 必须为 4 个 ASCII 字符，系统 ID 必须为 3 个 ASCII 字符。");
        int length = 0x40 + Records.Count * 0x40;
        byte[] data = new byte[length];
        Write32(data, 4, (uint)length); Write32(data, 0x10, (uint)Records.Count);
        Encoding.ASCII.GetBytes(SystemId + AppId).CopyTo(data, 0x18);
        uint sub = 0;
        for (int i = 0; i < Records.Count; i++)
        {
            byte[] record = BuildRecord(Records[i]);
            record.CopyTo(data, 0x40 + i * 0x40);
            if (Records[i].RecordMarker == 0x00000102) sub ^= Crc32(record);
        }
        Write32(data, 0x20, sub);
        Write32(data, 0, Crc32(data.AsSpan(4)));
        File.WriteAllBytes(path, NativeCodec.Crypt(data, true));
    }

    private static IcfRecord ParseRecord(ReadOnlySpan<byte> r)
    {
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
        RecordKind kind = flags switch { 0 => RecordKind.Pack, 1 => RecordKind.App, 2 => RecordKind.Opt, 0x101 => RecordKind.Patch, 0x201 => RecordKind.InstalledPatch, _ => throw new InvalidDataException($"不支持的记录类型：0x{flags:X8}") };
        var item = new IcfRecord
        {
            RecordMarker = BinaryPrimitives.ReadUInt32LittleEndian(r),
            Kind = kind,
            Version = kind == RecordKind.Opt ? Encoding.ASCII.GetString(r[0x20..0x24]).TrimEnd('\0') : ReadVersion(r[0x20..]),
            Date = ReadDate(r[0x24..]),
            RequiredVersion = kind == RecordKind.Opt ? "" : ReadVersion(r[0x2C..])
        };
        if (item.Kind is RecordKind.Patch or RecordKind.InstalledPatch)
        {
            item.SourceVersion = ReadVersion(r[0x30..]); item.SourceDate = ReadDate(r[0x34..]);
            item.SourceRequiredVersion = ReadVersion(r[0x3C..]);
        }
        return item;
    }

    private static byte[] BuildRecord(IcfRecord x)
    {
        byte[] r = new byte[0x40]; Write32(r, 0, x.RecordMarker);
        Write32(r, 4, x.Kind switch { RecordKind.Pack => 0u, RecordKind.App => 1u, RecordKind.Opt => 2u, RecordKind.Patch => 0x101u, _ => 0x201u });
        if (x.Kind == RecordKind.Opt)
        {
            if (x.Version.Length is < 1 or > 4 || !x.Version.All(IsAscii)) throw new FormatException("Opt 标识应为 1-4 个 ASCII 字符，例如 A005。");
            Encoding.ASCII.GetBytes(x.Version).CopyTo(r, 0x20); WriteDate(r, 0x24, x.Date);
        }
        else
        {
            WriteVersion(r, 0x20, x.Version); WriteDate(r, 0x24, x.Date); WriteVersion(r, 0x2C, x.RequiredVersion);
        }
        if (x.Kind is RecordKind.Patch or RecordKind.InstalledPatch)
        {
            WriteVersion(r, 0x30, x.SourceVersion); WriteDate(r, 0x34, x.SourceDate); WriteVersion(r, 0x3C, x.SourceRequiredVersion);
        }
        return r;
    }

    private static string ReadVersion(ReadOnlySpan<byte> x) => $"{x[2]}.{x[1]:00}.{x[0]:00}";
    private static void WriteVersion(byte[] b, int o, string value)
    {
        string[] p = value.Split('.');
        if (p.Length != 3 || !byte.TryParse(p[0], out byte major) || !byte.TryParse(p[1], out byte minor) || !byte.TryParse(p[2], out byte patch))
            throw new FormatException($"版本号“{value}”无效，应为 1.56.00 格式且每段为 0-255。");
        b[o] = patch; b[o + 1] = minor; b[o + 2] = major;
    }
    private static DateTime ReadDate(ReadOnlySpan<byte> x)
    {
        try { return new DateTime(BinaryPrimitives.ReadUInt16LittleEndian(x), x[2], x[3], x[4], x[5], x[6]); }
        catch { return new DateTime(2000, 1, 1); }
    }
    private static void WriteDate(byte[] b, int o, DateTime d)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)d.Year);
        b[o + 2] = (byte)d.Month; b[o + 3] = (byte)d.Day; b[o + 4] = (byte)d.Hour; b[o + 5] = (byte)d.Minute; b[o + 6] = (byte)d.Second;
    }
    private static uint Read32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Write32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    private static bool IsAscii(char c) => c is >= ' ' and <= '~';

    private static uint Crc32(ReadOnlySpan<byte> data) => NativeCodec.Crc32(data);
}
