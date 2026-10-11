// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArdysaModsTools.Linux;

public sealed record InstalledMod(string VpkSha256, string PatchedGameInfoSha256,
    string OriginalGameInfoBase64, string? BuildId, DateTimeOffset InstalledAt);
public sealed record ModState(bool Installed, bool GameRunning, bool PatchChanged, bool BuildChanged,
    bool RecoveryRequired, InstalledMod? Details);

public sealed class ModManager(Func<bool>? isGameRunning = null)
{
    private readonly Func<bool> gameRunning = isGameRunning ?? SteamDetector.GameRunning;
    public const string ModsDirectory = "game/_ArdysaModsLinux";
    public const string VpkPath = ModsDirectory + "/pak01_dir.vpk";
    public const string StatePath = ModsDirectory + "/state.json";
    public const string GameInfoPath = "game/dota/gameinfo.gi";
    private const string Marker = "// ArdysaModsTools Linux experimental mount";
    private static readonly Regex SearchPaths = new(@"(?m)^\s*""?SearchPaths""?\s*(?:\r?\n\s*)?\{", RegexOptions.CultureInvariant);

    public ModState Status(string root)
    {
        var state = ReadState(root);
        var changed = state is not null && FileTransaction.Hash(LinuxPaths.SafeChild(root, GameInfoPath)) != state.PatchedGameInfoSha256;
        return new(state is not null, gameRunning(), changed,
            state is not null && state.BuildId != SteamDetector.BuildId(root),
            File.Exists(LinuxPaths.SafeChild(root, ".ardysa-linux-transaction/journal.json")), state);
    }
    public async Task InstallAsync(string root, string vpk, bool experimental, CancellationToken ct = default)
    {
        if (!experimental) throw new InvalidOperationException("Linux mounting is unverified. Enable experimental mode explicitly.");
        RequireGame(root);
        using var transaction = new FileTransaction(root, gameRunning);
        var existing = ReadState(root);
        if (existing is null && Directory.Exists(LinuxPaths.SafeChild(root, ModsDirectory))
            && Directory.EnumerateFileSystemEntries(LinuxPaths.SafeChild(root, ModsDirectory)).Any())
            throw new IOException("Unmanaged files exist in the Linux mods directory. Inspect them before installing.");
        var info = LinuxPaths.SafeChild(root, GameInfoPath);
        var current = await File.ReadAllBytesAsync(info, ct);
        byte[] original;
        if (existing is not null && FileTransaction.Hash(info) == existing.PatchedGameInfoSha256)
            original = Convert.FromBase64String(existing.OriginalGameInfoBase64);
        else
        {
            if (Encoding.UTF8.GetString(current).Contains(Marker, StringComparison.Ordinal))
                throw new IOException("Game configuration changed while mods were active. Restore or inspect it before patching.");
            original = current; // Steam replaced the file: rebase onto the new version, not the old backup.
        }
        var patched = PatchGameInfo(Encoding.UTF8.GetString(original));
        var workspace = Path.Combine(Path.GetTempPath(), "amt-linux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            // Normalize split input into a validated standalone archive; never copy a _dir without its chunks.
            var extracted = Path.Combine(workspace, "content");
            await new VpkArchive(vpk).ExtractAsync(extracted, ct);
            var stagedVpk = Path.Combine(workspace, "pak01_dir.vpk");
            await VpkArchive.BuildAsync(extracted, stagedVpk, ct);
            var stagedInfo = Path.Combine(workspace, "gameinfo.gi");
            await File.WriteAllTextAsync(stagedInfo, patched, new UTF8Encoding(false), ct);
            var state = new InstalledMod(FileTransaction.Hash(stagedVpk), FileTransaction.Hash(stagedInfo),
                Convert.ToBase64String(original), SteamDetector.BuildId(root), DateTimeOffset.UtcNow);
            var stagedState = Path.Combine(workspace, "state.json");
            await File.WriteAllTextAsync(stagedState, JsonSerializer.Serialize(state), ct);
            RequireGame(root);
            await transaction.ApplyAsync([new(VpkPath, stagedVpk), new(GameInfoPath, stagedInfo), new(StatePath, stagedState)], ct);
        }
        finally { Directory.Delete(workspace, true); }
    }
    public async Task DisableAsync(string root, CancellationToken ct = default)
    {
        RequireGame(root);
        using var transaction = new FileTransaction(root, gameRunning);
        var state = ReadState(root) ?? throw new IOException("No managed Linux mod installation was found.");
        var info = LinuxPaths.SafeChild(root, GameInfoPath);
        var currentText = await File.ReadAllTextAsync(info, ct);
        var restoreInfo = FileTransaction.Hash(info) == state.PatchedGameInfoSha256;
        if (!restoreInfo && currentText.Contains(Marker, StringComparison.Ordinal))
            throw new IOException("Game configuration changed. Refusing to overwrite it with an old backup.");
        var installed = LinuxPaths.SafeChild(root, VpkPath);
        if (!File.Exists(installed) || FileTransaction.Hash(installed) != state.VpkSha256)
            throw new IOException("Installed VPK changed. Inspect it before removing mods.");
        var temporary = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(temporary, Convert.FromBase64String(state.OriginalGameInfoBase64), ct);
            var changes = new List<FileTransaction.Change>();
            if (restoreInfo) changes.Add(new(GameInfoPath, temporary));
            changes.Add(new(VpkPath, null)); changes.Add(new(StatePath, null));
            await transaction.ApplyAsync(changes, ct);
        }
        finally { File.Delete(temporary); }
    }
    public async Task ReapplyAsync(string root, bool experimental, CancellationToken ct = default)
    {
        var state = ReadState(root) ?? throw new IOException("No installed Linux mods to reapply.");
        var vpk = LinuxPaths.SafeChild(root, VpkPath);
        if (FileTransaction.Hash(vpk) != state.VpkSha256) throw new IOException("Installed VPK changed.");
        await InstallAsync(root, vpk, experimental, ct);
    }
    public static string PatchGameInfo(string original)
    {
        if (original.Contains("_ArdysaMods", StringComparison.Ordinal))
            throw new InvalidDataException("An existing Ardysa mount was found. Restore it before using the Linux port.");
        var matches = SearchPaths.Matches(original);
        if (matches.Count != 1) throw new InvalidDataException("Expected exactly one SearchPaths block in gameinfo.gi.");
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return original.Insert(matches[0].Index + matches[0].Length,
            newline + "\t\t\t" + Marker + newline + "\t\t\tGame\t\t_ArdysaModsLinux");
    }
    private static InstalledMod? ReadState(string root)
    {
        var file = LinuxPaths.SafeChild(root, StatePath);
        return File.Exists(file) ? JsonSerializer.Deserialize<InstalledMod>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Invalid mod state.") : null;
    }
    private void RequireGame(string root)
    {
        if (!SteamDetector.Validate(root)) throw new DirectoryNotFoundException("Select a native Linux Dota 2 installation.");
        LinuxPaths.EnsureNoLinks(root);
        if (gameRunning()) throw new IOException("Close Dota 2 before changing game files.");
        if (SteamDetector.SteamUpdateInProgress(root)) throw new IOException("Wait for Steam to finish updating Dota 2.");
    }
}
