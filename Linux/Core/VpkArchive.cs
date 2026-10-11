// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace ArdysaModsTools.Linux;

public sealed record VpkEntry(string Path, uint Crc, ushort Archive, uint Offset, uint Length, byte[] Preload);

/// <summary>Source VPK v1/v2 reader and single-file v1 writer; no Windows binaries.</summary>
public sealed class VpkArchive
{
    public const uint Signature = 0x55AA1234;
    public IReadOnlyList<VpkEntry> Entries { get; }
    private readonly string directoryFile;
    private readonly long dataStart;
    private readonly long dataLength;
    public VpkArchive(string path)
    {
        directoryFile = Path.GetFullPath(path);
        using var stream = File.OpenRead(directoryFile);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadUInt32() != Signature) throw new InvalidDataException("Not a VPK archive.");
        var version = reader.ReadUInt32();
        if (version is not (1 or 2)) throw new InvalidDataException("Unsupported VPK version.");
        var treeLength = reader.ReadUInt32();
        if (treeLength > 128 * 1024 * 1024) throw new InvalidDataException("VPK directory is too large.");
        var header = version == 1 ? 12 : 28;
        dataLength = version == 1 ? stream.Length - header - treeLength : reader.ReadUInt32();
        if (version == 2) { reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); }
        dataStart = header + (long)treeLength;
        if (dataStart > stream.Length || dataLength < 0 || dataStart + dataLength > stream.Length)
            throw new InvalidDataException("Truncated VPK.");
        var tree = reader.ReadBytes((int)treeLength);
        using var treeStream = new MemoryStream(tree, false);
        using var tr = new BinaryReader(treeStream, Encoding.UTF8);
        var entries = new List<VpkEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (ReadString(tr) is { Length: > 0 } extension)
        {
            while (ReadString(tr) is { Length: > 0 } folder)
            {
                while (ReadString(tr) is { Length: > 0 } name)
                {
                    var crc = tr.ReadUInt32();
                    var preloadLength = tr.ReadUInt16();
                    var archive = tr.ReadUInt16();
                    var offset = tr.ReadUInt32();
                    var length = tr.ReadUInt32();
                    if (tr.ReadUInt16() != ushort.MaxValue) throw new InvalidDataException("Invalid VPK entry.");
                    var preload = tr.ReadBytes(preloadLength);
                    if (preload.Length != preloadLength) throw new EndOfStreamException();
                    var relative = (folder == " " ? "" : folder + "/") + name + (extension == " " ? "" : "." + extension);
                    ValidateName(relative);
                    if (!names.Add(relative)) throw new InvalidDataException("Duplicate VPK path.");
                    if (archive == 0x7FFF && (long)offset + length > dataLength)
                        throw new InvalidDataException("VPK entry exceeds embedded data.");
                    entries.Add(new(relative, crc, archive, offset, length, preload));
                    if (entries.Count > 1_000_000) throw new InvalidDataException("Too many VPK entries.");
                }
            }
        }
        if (treeStream.Position != treeStream.Length) throw new InvalidDataException("Trailing VPK directory bytes.");
        Entries = entries;
    }
    public async Task CopyEntryAsync(VpkEntry entry, Stream destination, CancellationToken ct = default)
    {
        var crc = new Crc32();
        crc.Append(entry.Preload);
        await destination.WriteAsync(entry.Preload, ct);
        if (entry.Length > 0)
        {
            var source = entry.Archive == 0x7FFF ? directoryFile : Chunk(entry.Archive);
            await using var stream = File.OpenRead(source);
            var start = entry.Archive == 0x7FFF ? dataStart + entry.Offset : entry.Offset;
            if (start + entry.Length > stream.Length) throw new InvalidDataException($"Truncated data: {entry.Path}");
            stream.Position = start;
            var buffer = new byte[128 * 1024];
            long left = entry.Length;
            while (left > 0)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                if (count == 0) throw new EndOfStreamException();
                crc.Append(buffer.AsSpan(0, count));
                await destination.WriteAsync(buffer.AsMemory(0, count), ct);
                left -= count;
            }
        }
        if (crc.Value != entry.Crc) throw new InvalidDataException($"VPK CRC mismatch: {entry.Path}");
    }
    public async Task VerifyAsync(CancellationToken ct = default)
    {
        foreach (var entry in Entries) await CopyEntryAsync(entry, Stream.Null, ct);
    }
    public async Task ExtractEntryAsync(string relative, string destination, CancellationToken ct = default)
    {
        var entry = Entries.SingleOrDefault(e => e.Path == relative)
            ?? throw new FileNotFoundException("Entry not found in VPK.", relative);
        destination = Path.GetFullPath(destination);
        LinuxPaths.EnsureNoLinks(destination);
        if (File.Exists(destination)) throw new IOException("Output already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew))
            { await CopyEntryAsync(entry, output, ct); output.Flush(true); }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task ExtractAsync(string destination, CancellationToken ct = default)
    {
        LinuxPaths.EnsureNoLinks(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Extraction destination must be empty.");
        Directory.CreateDirectory(destination);
        foreach (var entry in Entries)
        {
            var path = LinuxPaths.SafeChild(destination, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew);
            await CopyEntryAsync(entry, output, ct);
        }
    }
    public static async Task BuildAsync(string source, string destination, CancellationToken ct = default)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        LinuxPaths.EnsureNoLinks(source);
        LinuxPaths.EnsureNoLinks(destination);
        if (destination.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new IOException("Output must be outside the source directory.");
        if (File.Exists(destination)) throw new IOException("Output already exists.");
        var files = new List<(string Full, string Relative, string Extension, string Folder, string Name, uint Crc, uint Length, uint Offset)>();
        long offset = 0;
        // Recurse manually so directory symlinks are rejected before traversal.
        foreach (var path in SourceFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, path);
            ValidateName(relative);
            var extension = Path.GetExtension(relative);
            var name = Path.GetFileNameWithoutExtension(relative);
            if (name.Length == 0) { name = Path.GetFileName(relative); extension = ""; }
            var folder = Path.GetDirectoryName(relative);
            var length = new FileInfo(path).Length;
            if (length > uint.MaxValue || offset + length > uint.MaxValue)
                throw new IOException("Single-file VPK limit is 4 GiB; split packs are not yet supported by the writer.");
            var crc = new Crc32();
            var buffer = new byte[128 * 1024];
            await using (var input = File.OpenRead(path))
            {
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0) crc.Append(buffer.AsSpan(0, count));
            }
            files.Add((path, relative, extension.Length == 0 ? " " : extension[1..],
                string.IsNullOrEmpty(folder) ? " " : folder, name, crc.Value, (uint)length, (uint)offset));
            offset += length;
        }
        if (files.Count == 0) throw new IOException("Source directory is empty.");
        using var tree = new MemoryStream();
        using (var writer = new BinaryWriter(tree, Encoding.UTF8, true))
        {
            foreach (var ext in files.GroupBy(f => f.Extension).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                WriteString(writer, ext.Key);
                foreach (var folder in ext.GroupBy(f => f.Folder).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    WriteString(writer, folder.Key);
                    foreach (var file in folder.OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        WriteString(writer, file.Name);
                        writer.Write(file.Crc); writer.Write((ushort)0); writer.Write((ushort)0x7FFF);
                        writer.Write(file.Offset); writer.Write(file.Length); writer.Write(ushort.MaxValue);
                    }
                    writer.Write((byte)0);
                }
                writer.Write((byte)0);
            }
            writer.Write((byte)0);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew))
            {
                using (var header = new BinaryWriter(output, Encoding.UTF8, true))
                { header.Write(Signature); header.Write(1u); header.Write(checked((uint)tree.Length)); }
                tree.Position = 0;
                await tree.CopyToAsync(output, ct);
                foreach (var file in files)
                {
                    LinuxPaths.EnsureNoLinks(file.Full);
                    await using var input = File.OpenRead(file.Full);
                    if (input.Length != file.Length) throw new IOException("Source changed during packaging.");
                    await input.CopyToAsync(output, ct);
                }
                output.Flush(true);
            }
            await new VpkArchive(temporary).VerifyAsync(ct);
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private string Chunk(ushort archive)
    {
        if (!directoryFile.EndsWith("_dir.vpk", StringComparison.Ordinal))
            throw new InvalidDataException("Split VPK directory must end in _dir.vpk.");
        return directoryFile[..^8] + $"_{archive:000}.vpk";
    }
    private static string ReadString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            bytes.WriteByte(b);
            if (bytes.Length > 4096) throw new InvalidDataException("VPK string too long.");
        }
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }
    private static void WriteString(BinaryWriter writer, string value)
    { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
    private static void ValidateName(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains('\0')
            || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe VPK path: {path}");
    }
    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            LinuxPaths.EnsureNoLinks(path);
            if (Directory.Exists(path))
            { foreach (var file in SourceFiles(path)) yield return file; }
            else yield return path;
        }
    }
}
