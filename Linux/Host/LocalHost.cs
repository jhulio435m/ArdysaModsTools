// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace ArdysaModsTools.Linux;

internal static class LocalHost
{
    public sealed record GalleryRequest(Dictionary<string, GallerySelection> Selections, string[]? Favorites = null);
    public sealed record CatalogRequest(string HeroId, string AssetPath, string Output);
    public sealed record SessionRequest(string Token);
    public sealed record FileRequest(string Input, string Output);
    public sealed record BuildRequest(string Source, string Output);
    public sealed record RecipeRequest(string Recipe, string Output);
    public sealed record DownloadRequest(string[] Urls, string Sha256, string Output);
    public sealed record InstallRequest(string Input);
    public sealed record ListRequest(string Input, int Offset = 0, int Limit = 200);
    public sealed record PerformanceRequest(int? FpsMax, int? MenuFps);

    public static async Task RunAsync(PreferenceStore preferences, CancellationToken ct,
        string? sessionFile = null, bool noBrowser = false)
    {
        if (noBrowser && sessionFile is null) throw new ArgumentException("--no-browser requires --session-file.");
        if (sessionFile is not null && !Path.IsPathFullyQualified(sessionFile))
            throw new ArgumentException("Session file must use an absolute path.");
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
            throw new IOException("Linux frontend has not been built. See Linux/README.md for build instructions.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { Args = [], ContentRootPath = AppContext.BaseDirectory, WebRootPath = webRoot });
        builder.Logging.ClearProviders(); // Never log session bootstrap tokens or local operation paths.
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, 0);
            server.Limits.MaxRequestBodySize = 1024 * 1024;
        });
        var app = builder.Build();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sessionCookie = "amt-linux-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var origin = "";
        using var operationGate = new SemaphoreSlim(1, 1);
        var manager = new ModManager();
        CancellationTokenSource? galleryCancellation = null;
        string Root() => preferences.Load().DotaRoot ?? new SteamDetector().Detect().Select(i => i.Root).SingleOrDefault()
            ?? throw new IOException("Select a Dota installation first.");
        bool TokenMatches(string? value) => value is not null && value.Length == token.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(token));
        app.Use(async (context, next) =>
        {
            var expectedOrigin = origin.Length == 0
                ? app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
                : origin;
            if (context.Request.Host.Value != new Uri(expectedOrigin).Authority)
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: https://cdn.ardysamods.my.id https://cdn2.ardysamods.my.id https://cdn.cloudflare.steamstatic.com https://cdn.akamai.steamstatic.com; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (context.Request.Method != "GET" && context.Request.Headers.Origin != expectedOrigin)
                { context.Response.StatusCode = 403; return; }
                if (context.Request.Path != "/api/session" && !TokenMatches(context.Request.Cookies[sessionCookie]))
                { context.Response.StatusCode = 401; return; }
            }
            var locked = false;
            try
            {
                if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method != "GET" && context.Request.Path != "/api/gallery/cancel")
                {
                    locked = await operationGate.WaitAsync(0, context.RequestAborted);
                    if (!locked) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "An operation is already running." }); return; }
                }
                await next(context);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message }, context.RequestAborted);
            }
            finally { if (locked) operationGate.Release(); }
        });
        app.MapPost("/api/session", (SessionRequest request, HttpContext context) =>
        {
            if (!TokenMatches(request.Token)) return Results.Unauthorized();
            context.Response.Cookies.Append(sessionCookie, token, new CookieOptions
            { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/api" });
            return Results.Ok();
        });
        var catalog = new HeroCatalog();
        app.MapGet("/api/catalog", async (CancellationToken cancellation) => await catalog.LoadAsync(cancellation));
        app.MapPost("/api/catalog/generate", async (CatalogRequest value, CancellationToken cancellation) =>
        { await catalog.GenerateAsync(Root(), value.HeroId, value.AssetPath, value.Output, cancellation); return Results.Ok(); });
        var gallery = new GalleryStore();
        var galleryLog = new List<string>();
        var galleryBusy = false;
        app.MapGet("/api/gallery", async (CancellationToken cancellation) => new { heroes = await catalog.LoadAsync(cancellation), state = gallery.Load() });
        app.MapGet("/api/gallery/progress", () => { lock (galleryLog) return new { busy = galleryBusy, lines = galleryLog.ToArray() }; });
        app.MapPost("/api/gallery/save", async (GalleryRequest value, CancellationToken cancellation) =>
        {
            GalleryStore.Validate(value.Selections, await catalog.LoadAsync(cancellation));
            var previous = gallery.Load();
            gallery.Save(previous with { Draft = value.Selections, Favorites = value.Favorites ?? previous.Favorites });
            return Results.Ok();
        });
        app.MapPost("/api/gallery/cancel", () => { galleryCancellation?.Cancel(); return Results.Ok(); });
        app.MapPost("/api/gallery/apply", async (GalleryRequest value, CancellationToken cancellation) =>
        {
            var root = Root();
            var preference = preferences.Load();
            if (!preference.ExperimentalMount) throw new InvalidOperationException("Habilita la instalación experimental en Herramientas → Mods.");
            var status = manager.Status(root);
            if (status.GameRunning) throw new InvalidOperationException("Cierra Dota antes de aplicar la selección.");
            if (status.RecoveryRequired || status.BuildChanged || status.PatchChanged)
                throw new InvalidOperationException("Revisa la instalación en Herramientas antes de aplicar cambios.");
            var heroes = await catalog.LoadAsync(cancellation);
            GalleryStore.Validate(value.Selections, heroes);
            var state = gallery.Load();
            Directory.CreateDirectory(gallery.DirectoryPath);
            var installed = Path.Combine(root, "game/_ArdysaModsLinux/pak01_dir.vpk");
            if (state.InstalledHash is not null && File.Exists(installed) && !GalleryStore.Hash(installed).Equals(state.InstalledHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("El paquete activo cambió fuera del editor. Conserva tu selección y revisa la instalación.");
            if (state.BuildId is not null && state.BuildId != SteamDetector.BuildId(root))
                throw new IOException("Dota cambió de versión. Hay que regenerar la base del editor antes de aplicar.");
            LinuxPaths.EnsureNoLinks(gallery.BaselineVpk);
            if (!File.Exists(gallery.BaselineVpk) && state.Baseline.Count > 0)
                throw new IOException("Falta el respaldo del editor. Conserva tu selección antes de recuperar la base.");
            if (!File.Exists(gallery.BaselineVpk) && File.Exists(installed)) File.Copy(installed, gallery.BaselineVpk);
            if (File.Exists(gallery.BaselineVpk))
            {
                var baselineHash = GalleryStore.Hash(gallery.BaselineVpk);
                if (state.BaselineHash is not null && !baselineHash.Equals(state.BaselineHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("El respaldo del editor cambió. No se modificó Dota.");
                state = state with { BaselineHash = baselineHash };
            }
            state = state with { BuildId = SteamDetector.BuildId(root) };
            gallery.Save(state with { Draft = value.Selections, Favorites = value.Favorites ?? state.Favorites });
            var output = Path.Combine(gallery.DirectoryPath, "selection-" + Guid.NewGuid().ToString("N") + "_dir.vpk");
            lock (galleryLog) { galleryLog.Clear(); galleryBusy = true; }
            using var generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            galleryCancellation = generationCancellation;
            try
            {
                await new GalleryComposer().GenerateAsync(root, gallery, state, value.Selections, heroes, output,
                    line => { lock (galleryLog) { galleryLog.Add(line); if (galleryLog.Count > 300) galleryLog.RemoveAt(0); } }, generationCancellation.Token);
                await manager.InstallAsync(root, output, preference.ExperimentalMount, generationCancellation.Token);
                gallery.Save(state with { Draft = value.Selections, Applied = value.Selections,
                    Favorites = value.Favorites ?? state.Favorites, InstalledHash = GalleryStore.Hash(installed) });
                return Results.Ok(new { message = "Selección instalada. Puedes abrir Dota.", heroes = value.Selections.Count });
            }
            catch (OperationCanceledException) { throw new InvalidOperationException("Operación cancelada. Tu selección sigue guardada."); }
            finally { galleryCancellation = null; lock (galleryLog) galleryBusy = false; if (File.Exists(output)) File.Delete(output); }
        });
        app.MapGet("/api/detect", () => new SteamDetector().Detect());
        app.MapGet("/api/preferences", preferences.Load);
        app.MapPost("/api/preferences", (Preferences value) => { preferences.Save(value); return Results.Ok(); });
        app.MapGet("/api/status", () => manager.Status(Root()));
        app.MapPost("/api/install", async (InstallRequest value, CancellationToken cancellation) =>
        { await manager.InstallAsync(Root(), value.Input, preferences.Load().ExperimentalMount, cancellation); return Results.Ok(); });
        app.MapPost("/api/disable", async (CancellationToken cancellation) =>
        { await manager.DisableAsync(Root(), cancellation); return Results.Ok(); });
        app.MapPost("/api/reapply", async (CancellationToken cancellation) =>
        { await manager.ReapplyAsync(Root(), preferences.Load().ExperimentalMount, cancellation); return Results.Ok(); });
        app.MapPost("/api/recover", () => { FileTransaction.Recover(Root()); return Results.Ok(); });
        app.MapPost("/api/vpk/verify", async (InstallRequest value, CancellationToken cancellation) =>
        { var archive = new VpkArchive(value.Input); await archive.VerifyAsync(cancellation); return Results.Ok(new { entries = archive.Entries.Count }); });
        app.MapPost("/api/vpk/list", (ListRequest value) =>
        {
            if (value.Offset < 0 || value.Limit is < 1 or > 500) throw new ArgumentException("Invalid VPK page.");
            var entries = new VpkArchive(value.Input).Entries;
            return new { total = entries.Count, offset = value.Offset,
                entries = entries.Skip(value.Offset).Take(value.Limit).Select(e => new { e.Path, e.Length, PreloadBytes = e.Preload.Length }) };
        });
        app.MapPost("/api/vpk/extract", async (FileRequest value, CancellationToken cancellation) =>
        { await new VpkArchive(value.Input).ExtractAsync(value.Output, cancellation); return Results.Ok(); });
        app.MapPost("/api/vpk/build", async (BuildRequest value, CancellationToken cancellation) =>
        { await VpkArchive.BuildAsync(value.Source, value.Output, cancellation); return Results.Ok(); });
        app.MapPost("/api/generate", async (RecipeRequest value, CancellationToken cancellation) =>
        { await new GenerationService().GenerateAsync(Root(), value.Recipe, value.Output, cancellation); return Results.Ok(); });
        app.MapPost("/api/download", async (DownloadRequest value, CancellationToken cancellation) =>
        {
            using var downloader = new AssetDownloader();
            await downloader.DownloadAsync(value.Urls, value.Sha256, value.Output, cancellation);
            return Results.Ok();
        });
        app.MapPost("/api/performance", async (PerformanceRequest value, CancellationToken cancellation) =>
        { await new PerformanceConfig().SaveAsync(Root(), value.FpsMax, value.MenuFps, cancellation); return Results.Ok(); });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        await app.StartAsync(ct);
        origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine($"ArdysaModsTools Linux listening locally at {origin}. Close with Ctrl+C.");
        try
        {
            var launchUrl = origin + "/gallery.html#session=" + token;
            if (sessionFile is not null)
            {
                LinuxPaths.EnsureNoLinks(sessionFile);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using var stream = new FileStream(sessionFile, options);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(launchUrl), ct);
            }
            if (!noBrowser)
            {
                var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(launchUrl);
                using var process = Process.Start(start) ?? throw new IOException("Could not open the browser.");
            }
            await app.WaitForShutdownAsync(ct);
        }
        finally
        {
            if (sessionFile is not null) File.Delete(sessionFile);
            await app.StopAsync(CancellationToken.None); await app.DisposeAsync();
        }
    }
}
