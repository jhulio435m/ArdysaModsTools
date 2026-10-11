// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

namespace ArdysaModsTools.Linux;

/// <summary>Recoverable file replacement. The journal is durable before the first rename.</summary>
public sealed class FileTransaction : IDisposable
{
    public sealed record Change(string Relative, string? Source);
    public sealed record Entry(string Relative, bool Existed, string? BeforeHash, string? AfterHash, int Index);
    public sealed record Journal(string Phase, List<Entry> Entries);
    private readonly string root;
    private readonly string directory;
    private readonly FileStream gate;
    private readonly Func<bool> gameRunning;
    public FileTransaction(string root, Func<bool>? gameRunning = null)
    {
        this.root = Path.GetFullPath(root);
        this.gameRunning = gameRunning ?? SteamDetector.GameRunning;
        LinuxPaths.EnsureNoLinks(this.root);
        directory = LinuxPaths.SafeChild(this.root, ".ardysa-linux-transaction");
        Directory.CreateDirectory(directory);
        var lockFile = LinuxPaths.SafeChild(directory, "lock");
        gate = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(Path.Combine(directory, "journal.json")))
        {
            gate.Dispose();
            throw new IOException("An interrupted operation needs recovery. Run the recover command.");
        }
    }
    public async Task ApplyAsync(IReadOnlyList<Change> changes, CancellationToken ct = default)
    {
        var entries = new List<Entry>();
        if (changes.Select(c => c.Relative).Distinct(StringComparer.Ordinal).Count() != changes.Count)
            throw new InvalidDataException("Duplicate transaction destination.");
        try
        {
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                var target = LinuxPaths.SafeChild(root, change.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var existed = File.Exists(target);
                if (existed) await CopyAsync(target, Path.Combine(directory, $"{i}.backup"), ct);
                var staged = Path.Combine(directory, $"{i}.stage");
                if (change.Source is not null) await CopyAsync(change.Source, staged, ct);
                entries.Add(new(change.Relative, existed, existed ? Hash(target) : null,
                    change.Source is null ? null : Hash(staged), i));
            }
            WriteJournal(directory, new("pending", entries));
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (gameRunning()) throw new IOException("Close Dota 2 before changing game files.");
                if (SteamDetector.SteamUpdateInProgress(root)) throw new IOException("Wait for Steam to finish updating Dota 2.");
                var target = LinuxPaths.SafeChild(root, entry.Relative);
                if ((File.Exists(target) ? Hash(target) : null) != entry.BeforeHash)
                    throw new IOException($"File changed during transaction: {entry.Relative}");
                if (entry.AfterHash is null) File.Delete(target);
                else File.Move(Path.Combine(directory, $"{entry.Index}.stage"), target, true);
            }
            WriteJournal(directory, new("committed", entries));
            Cleanup(directory, entries);
        }
        catch
        {
            if (File.Exists(Path.Combine(directory, "journal.json"))) RecoverUnlocked(root, directory);
            else Cleanup(directory, entries);
            throw;
        }
    }
    public static void Recover(string root, Func<bool>? gameRunning = null)
    {
        LinuxPaths.EnsureNoLinks(root);
        var directory = LinuxPaths.SafeChild(root, ".ardysa-linux-transaction");
        if (!Directory.Exists(directory)) return;
        using var gate = new FileStream(LinuxPaths.SafeChild(directory, "lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if ((gameRunning ?? SteamDetector.GameRunning)()) throw new IOException("Close Dota 2 before recovery.");
        RecoverUnlocked(root, directory);
    }
    private static void RecoverUnlocked(string root, string directory)
    {
        var path = LinuxPaths.SafeChild(directory, "journal.json");
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid transaction journal.");
        if (journal.Phase is not ("pending" or "committed")) throw new InvalidDataException("Unknown journal phase.");
        if (journal.Phase == "pending")
        {
            // Validate every target before restoring any; never overwrite a Steam update.
            foreach (var entry in journal.Entries)
            {
                var target = LinuxPaths.SafeChild(root, entry.Relative);
                var hash = File.Exists(target) ? Hash(target) : null;
                if (hash != entry.BeforeHash && hash != entry.AfterHash)
                    throw new IOException($"Recovery conflict: {entry.Relative}. Keep backups and inspect manually.");
                var backup = LinuxPaths.SafeChild(directory, $"{entry.Index}.backup");
                if (entry.Existed && (!File.Exists(backup) || Hash(backup) != entry.BeforeHash))
                    throw new IOException($"Recovery backup missing or damaged: {entry.Relative}");
            }
            foreach (var entry in journal.Entries.AsEnumerable().Reverse())
            {
                var target = LinuxPaths.SafeChild(root, entry.Relative);
                if (entry.Existed)
                {
                    var temporary = LinuxPaths.SafeChild(directory, $"{entry.Index}.restore");
                    File.Copy(Path.Combine(directory, $"{entry.Index}.backup"), temporary, true);
                    File.Move(temporary, target, true);
                }
                else File.Delete(target);
            }
            // Rollback is now complete. Cleanup may be retried after a crash without needing backups.
            WriteJournal(directory, new("committed", journal.Entries));
        }
        Cleanup(directory, journal.Entries);
    }
    internal static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static async Task CopyAsync(string source, string target, CancellationToken ct)
    {
        LinuxPaths.EnsureNoLinks(target);
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.Create);
        await input.CopyToAsync(output, ct);
        output.Flush(true);
    }
    private static void WriteJournal(string directory, Journal journal)
    {
        var temporary = LinuxPaths.SafeChild(directory, "journal.tmp");
        using (var stream = new FileStream(temporary, FileMode.Create))
        {
            JsonSerializer.Serialize(stream, journal);
            stream.Flush(true);
        }
        File.Move(temporary, Path.Combine(directory, "journal.json"), true);
    }
    private static void Cleanup(string directory, IEnumerable<Entry> entries)
    {
        // Remove the journal last so an interrupted cleanup remains recoverable.
        // A committed journal only needs cleanup; a pending journal must retain backups.
        foreach (var entry in entries)
            foreach (var suffix in new[] { "stage", "backup", "restore" })
                File.Delete(LinuxPaths.SafeChild(directory, $"{entry.Index}.{suffix}"));
        File.Delete(LinuxPaths.SafeChild(directory, "journal.tmp"));
        File.Delete(LinuxPaths.SafeChild(directory, "journal.json"));
    }
    public void Dispose() => gate.Dispose();
}
