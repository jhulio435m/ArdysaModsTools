// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ArdysaModsTools.Linux;
using NUnit.Framework;

namespace ArdysaModsTools.Linux.Tests;

[TestFixture]
public sealed class PortTests
{
    private string workspace = null!;
    [SetUp] public void SetUp() { workspace = Path.Combine(Path.GetTempPath(), "amt-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace); }
    [TearDown] public void TearDown() => Directory.Delete(workspace, true);
    private string At(string relative) => Path.Combine(workspace, relative);
    private void Put(string path, string text)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

    [Test]
    public async Task VpkRoundTripPreservesNestedUnicodeEmptyAndExtensionlessFiles()
    {
        var source = At("source");
        Put(Path.Combine(source, "scripts/items/items_game.txt"), "\"items_game\" { }");
        Put(Path.Combine(source, "carpeta con espacios/á.txt"), "contenido español");
        Put(Path.Combine(source, "LICENSE"), "GPL"); Put(Path.Combine(source, "empty.bin"), "");
        var output = At("pack_dir.vpk");
        await VpkArchive.BuildAsync(source, output);
        var archive = new VpkArchive(output);
        Assert.That(archive.Entries.Count, Is.EqualTo(4));
        await archive.VerifyAsync(); await archive.ExtractAsync(At("extracted"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            Assert.That(File.ReadAllBytes(At("extracted/" + Path.GetRelativePath(source, file))), Is.EqualTo(File.ReadAllBytes(file)));
    }
    [Test]
    public async Task VpkRejectsCorruptPayload()
    {
        Put(At("source/file.txt"), "test"); await VpkArchive.BuildAsync(At("source"), At("pack.vpk"));
        var bytes = File.ReadAllBytes(At("pack.vpk")); bytes[^1] ^= 1; File.WriteAllBytes(At("pack.vpk"), bytes);
        Assert.ThrowsAsync<InvalidDataException>(() => new VpkArchive(At("pack.vpk")).VerifyAsync());
    }
    [Test]
    public async Task SelectedEntryExtractionChecksCrcAndPreservesExistingOutput()
    {
        Put(At("source/a.txt"), "a"); Put(At("source/nested/b.txt"), "b");
        await VpkArchive.BuildAsync(At("source"), At("pack.vpk"));
        var archive = new VpkArchive(At("pack.vpk"));
        await archive.ExtractEntryAsync("nested/b.txt", At("selected.txt"));
        Assert.That(File.ReadAllText(At("selected.txt")), Is.EqualTo("b"));
        Assert.ThrowsAsync<IOException>(() => archive.ExtractEntryAsync("a.txt", At("selected.txt")));
        Assert.That(File.ReadAllText(At("selected.txt")), Is.EqualTo("b"));
    }
    [TestCase("../escape")]
    [TestCase("/absolute")]
    [TestCase("nested\\escape")]
    public void SafePathsRejectTraversal(string relative) => Assert.Throws<InvalidDataException>(() => LinuxPaths.SafeChild(workspace, relative));
    [Test]
    public void SafePathsRejectSymbolicLinks()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Linux symlink test");
        Directory.CreateDirectory(At("outside")); Directory.CreateSymbolicLink(At("link"), At("outside"));
        Assert.Throws<IOException>(() => LinuxPaths.SafeChild(workspace, "link/file"));
    }
    [Test]
    public void WriterRejectsOutputInsideSourceAndExistingOutputs()
    {
        Put(At("source/a.txt"), "a");
        Assert.ThrowsAsync<IOException>(() => VpkArchive.BuildAsync(At("source"), At("source/output.vpk")));
        Put(At("existing.vpk"), "keep");
        Assert.ThrowsAsync<IOException>(() => VpkArchive.BuildAsync(At("source"), At("existing.vpk")));
        Assert.That(File.ReadAllText(At("existing.vpk")), Is.EqualTo("keep"));
    }
    [Test]
    public void ReaderRejectsPathTraversalInDirectoryTree()
    {
        using var tree = new MemoryStream();
        using (var writer = new BinaryWriter(tree, Encoding.UTF8, true))
        {
            foreach (var text in new[] { "txt", "..", "escape" }) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
            writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)0x7FFF); writer.Write(0u); writer.Write(0u); writer.Write(ushort.MaxValue);
            writer.Write(new byte[3]);
        }
        using (var output = new BinaryWriter(File.Create(At("evil.vpk"))))
        { output.Write(VpkArchive.Signature); output.Write(1u); output.Write((uint)tree.Length); output.Write(tree.ToArray()); }
        Assert.Throws<InvalidDataException>(() => new VpkArchive(At("evil.vpk")));
    }
    [Test]
    public async Task ReaderHandlesVersionTwoPreloadAndExternalChunk()
    {
        // CRC-32 of "123456789" is the standard check value 0xCBF43926.
        using var tree = new MemoryStream();
        using (var writer = new BinaryWriter(tree, Encoding.UTF8, true))
        {
            foreach (var text in new[] { "txt", " ", "check" }) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
            writer.Write(0xCBF43926u); writer.Write((ushort)3); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(6u); writer.Write(ushort.MaxValue); writer.Write(Encoding.ASCII.GetBytes("123")); writer.Write(new byte[3]);
        }
        using (var output = new BinaryWriter(File.Create(At("split_dir.vpk"))))
        {
            output.Write(VpkArchive.Signature); output.Write(2u); output.Write((uint)tree.Length);
            output.Write(0u); output.Write(0u); output.Write(0u); output.Write(0u); output.Write(tree.ToArray());
        }
        File.WriteAllText(At("split_000.vpk"), "456789", new UTF8Encoding(false));
        var archive = new VpkArchive(At("split_dir.vpk"));
        await using var extracted = new MemoryStream();
        await archive.CopyEntryAsync(archive.Entries.Single(), extracted);
        Assert.That(Encoding.ASCII.GetString(extracted.ToArray()), Is.EqualTo("123456789"));
    }
    [Test]
    public void DetectorFindsAnAdditionalSteamLibraryWithSpaces()
    {
        var steam = At("home/.local/share/Steam"); var library = At("Other Library");
        Put(Path.Combine(steam, "steamapps/libraryfolders.vdf"), $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{library}\" }} }}");
        Put(Path.Combine(library, "steamapps/appmanifest_570.acf"), "\"AppState\" { \"installdir\" \"dota 2 beta\" \"buildid\" \"123\" }");
        var game = Path.Combine(library, "steamapps/common/dota 2 beta");
        FakeGame(game);
        var detected = new SteamDetector(At("home")).Detect().Single();
        Assert.That(detected.Root, Is.EqualTo(game)); Assert.That(detected.BuildId, Is.EqualTo("123"));
    }
    [Test]
    public void NativePatchPreservesOriginalSearchPathsAndRejectsDoubleMount()
    {
        var original = "\"GameInfo\"\r\n{\r\n FileSystem\r\n {\r\n SearchPaths\r\n {\r\n Game dota\r\n Game core\r\n }\r\n }\r\n}";
        var patched = ModManager.PatchGameInfo(original);
        Assert.That(patched, Does.Contain("Game dota\r\n Game core"));
        Assert.That(patched.IndexOf("_ArdysaModsLinux", StringComparison.Ordinal), Is.LessThan(patched.IndexOf("Game dota", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => ModManager.PatchGameInfo(patched));
    }
    [Test]
    public void RecoveryRestoresPendingWriteAndKeepsConflictingExternalChange()
    {
        Put(At("file"), "after"); Put(At(".ardysa-linux-transaction/0.backup"), "before");
        string Digest(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var entry = new FileTransaction.Entry("file", true, Digest("before"), Digest("after"), 0);
        Put(At(".ardysa-linux-transaction/journal.json"), JsonSerializer.Serialize(new FileTransaction.Journal("pending", [entry])));
        FileTransaction.Recover(workspace, () => false);
        Assert.That(File.ReadAllText(At("file")), Is.EqualTo("before"));
        Put(At("file"), "Steam update"); Put(At(".ardysa-linux-transaction/0.backup"), "before");
        Put(At(".ardysa-linux-transaction/journal.json"), JsonSerializer.Serialize(new FileTransaction.Journal("pending", [entry])));
        Assert.Throws<IOException>(() => FileTransaction.Recover(workspace, () => false));
        Assert.That(File.ReadAllText(At("file")), Is.EqualTo("Steam update"));
        Assert.That(File.Exists(At(".ardysa-linux-transaction/0.backup")), Is.True);
    }
    [Test]
    public async Task InstallationIsOptInAndDisableRestoresOriginalGameInfo()
    {
        var root = At("game"); FakeGame(root);
        var info = Path.Combine(root, "game/dota/gameinfo.gi"); var original = File.ReadAllBytes(info);
        Put(At("mod/models/readme.txt"), "test asset"); await VpkArchive.BuildAsync(At("mod"), At("mod_dir.vpk"));
        var manager = new ModManager(() => false);
        Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallAsync(root, At("mod_dir.vpk"), false));
        await manager.InstallAsync(root, At("mod_dir.vpk"), true);
        Assert.That(manager.Status(root).Installed, Is.True);
        await manager.DisableAsync(root);
        Assert.That(File.ReadAllBytes(info), Is.EqualTo(original));
        Assert.That(manager.Status(root).Installed, Is.False);
    }
    [Test]
    public async Task SteamUpdateRebasesAndDisablingPreservesTheUpdatedGameInfo()
    {
        var root = At("game"); FakeGame(root);
        Put(At("mod/a.txt"), "asset"); await VpkArchive.BuildAsync(At("mod"), At("mod_dir.vpk"));
        var manager = new ModManager(() => false); await manager.InstallAsync(root, At("mod_dir.vpk"), true);
        var info = Path.Combine(root, "game/dota/gameinfo.gi");
        var update = "GameInfo {\nFileSystem {\nSearchPaths {\nGame dota\nGame core\n}\n}\n}\n// new Steam version\n";
        File.WriteAllText(info, update);
        await manager.ReapplyAsync(root, true); await manager.DisableAsync(root);
        Assert.That(File.ReadAllText(info), Is.EqualTo(update));
    }
    [Test]
    public async Task FpsBlockCanBeRemovedWithoutRemovingUserConfiguration()
    {
        var root = At("game"); FakeGame(root); var cfg = Path.Combine(root, "game/dota/cfg/autoexec.cfg");
        Put(cfg, "// user config\nfps_max \"144\"\n");
        var service = new PerformanceConfig(() => false); await service.SaveAsync(root, 90, 30);
        Assert.That(File.ReadAllText(cfg), Does.Contain("fps_max \"90\""));
        await service.SaveAsync(root, null, null);
        Assert.That(File.ReadAllText(cfg), Is.EqualTo("// user config\nfps_max \"144\"\n"));
    }
    [Test]
    public void DownloaderRejectsUntrustedHostsBeforeNetworkAccess()
    {
        using var downloader = new AssetDownloader();
        Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(["https://localhost/asset"], new string('0', 64), At("download")));
    }
    private void FakeGame(string root)
    {
        Put(Path.Combine(root, "game/bin/linuxsteamrt64/dota2"), "fixture");
        Put(Path.Combine(root, "game/dota/pak01_dir.vpk"), "fixture");
        Put(Path.Combine(root, "game/dota/gameinfo.gi"), "GameInfo {\nFileSystem {\nSearchPaths {\nGame dota\nGame core\n}\n}\n}\n");
    }
}
