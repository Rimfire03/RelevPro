using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RelevPro.Core.Services;

/// <summary>Informations sur une nouvelle version disponible, extraites de la release GitHub.</summary>
/// <param name="DbSchema">Version du schéma de base lue dans les notes de la release (marqueur
/// <c>&lt;!-- db-schema: N --&gt;</c> ajouté par tools\Release.ps1), null si absente.</param>
/// <param name="IsChannelSwitch">Retour sur le canal Main depuis une build Dev : la version proposée peut être
/// plus ancienne que celle installée.</param>
public record UpdateInfo(string Version, string ReleaseUrl, string ReleaseNotes, string AssetUrl, string AssetName,
    int? DbSchema = null, bool IsChannelSwitch = false);

/// <summary>Vérifie et applique les mises à jour en s'appuyant sur les releases GitHub du dépôt
/// (exécutable portable publié en asset .zip sur chaque release) : télécharge l'archive, la décompresse,
/// puis remplace le contenu du dossier d'installation par celui de l'archive avant de relancer
/// l'application. Le remplacement se fait via un processus PowerShell entièrement détaché (voir
/// <see cref="DownloadAndApplyAsync"/>), pas un script .bat sur disque (une première version basée sur un
/// script batch s'était révélée peu fiable : le script pouvait supprimer son propre dossier pendant qu'il
/// s'exécutait encore, s'interrompant avant de relancer l'application).</summary>
public static class UpdateChannels
{
    public const string Main = "Main";
    public const string Dev = "Dev";
    public static readonly string[] All = [Main, Dev];
}

public class UpdateService
{
    private const string RepoOwner = "Rimfire03";
    private const string RepoName = "RelevPro";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RelevPro-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Interroge la dernière release GitHub et retourne ses infos si sa version est plus
    /// récente que <paramref name="currentVersion"/> (format "MAJOR.MINOR.PATCH"). Retourne null
    /// UNIQUEMENT quand la vérification a réussi et qu'il n'y a pas de mise à jour disponible.
    /// Lève une exception en cas d'échec réel (réseau, API indisponible, réponse inattendue) : avaler
    /// ces erreurs en silence (comme avant) rendait impossible de distinguer "à jour" de "la
    /// vérification a échoué", ce qui empêchait de diagnostiquer un vrai problème (ex. limite de débit
    /// de l'API GitHub, pare-feu). L'appelant du démarrage silencieux (voir App.Updates.cs) est
    /// responsable de ne pas déranger l'utilisateur pour ces erreurs ; le bouton "Vérifier maintenant"
    /// (Paramètres) les affiche telles quelles.</summary>
    /// <summary>Explication quand la dernière vérification n'a rien proposé alors qu'un retour sur Main était
    /// demandé (ex. base incompatible), sinon null.</summary>
    public string? LastCheckNote { get; private set; }

    /// <param name="installedIsDev">La build installée est une build du canal Dev.</param>
    /// <param name="currentDbSchema">Version de schéma de base gérée par la build installée : sert à ne proposer
    /// le retour sur Main que si la dernière version Main partage ce même schéma.</param>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, string channel = UpdateChannels.Main,
        bool installedIsDev = false, int? currentDbSchema = null)
    {
        LastCheckNote = null;
        // Main : dernière release stable (branche main). Dev : release la plus récente de la branche dev, dont le
        // tag se termine par "-dev" (publiée en pré-version, donc jamais vue par /releases/latest).
        var dev = string.Equals(channel, UpdateChannels.Dev, StringComparison.OrdinalIgnoreCase);
        var url = dev
            ? $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=30"
            : $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"L'API GitHub a répondu {(int)response.StatusCode} {response.ReasonPhrase} : {body}");
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        if (dev)
        {
            // Liste triée de la plus récente à la plus ancienne : première release non brouillon dont le tag finit par -dev.
            var first = root.EnumerateArray().FirstOrDefault(r =>
                !(r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True)
                && r.TryGetProperty("tag_name", out var tn)
                && (tn.GetString() ?? "").EndsWith("-dev", StringComparison.OrdinalIgnoreCase));
            if (first.ValueKind != JsonValueKind.Object) return null;
            root = first;
        }

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        var versionText = tag.TrimStart('v', 'V');   // ex. "1.12.0" ou "1.12.0-dev"
        var releaseNotes = root.TryGetProperty("body", out var notesEl) ? notesEl.GetString() ?? "" : "";
        var marker = System.Text.RegularExpressions.Regex.Match(releaseNotes, @"<!--\s*db-schema:\s*(\d+)\s*-->");
        int? dbSchema = marker.Success ? int.Parse(marker.Groups[1].Value) : null;

        var channelSwitch = false;
        if (!IsNewer(versionText, currentVersion))
        {
            // Retour sur Main depuis une build Dev : la version Main est proposée même si son numéro est plus ancien,
            // mais seulement si elle gère le même schéma de base (sinon l'ancienne version ne saurait pas ouvrir la base).
            if (dev || !installedIsDev) return null;
            if (dbSchema is null || currentDbSchema is null || dbSchema != currentDbSchema)
            {
                LastCheckNote = $"La dernière version Main ({versionText}) n'est pas proposée : " +
                    (dbSchema is null
                        ? "la version de sa base de données n'est pas connue."
                        : $"sa base de données (version {dbSchema}) est différente de la vôtre (version {currentDbSchema}).") +
                    " Une version Main plus récente sera proposée dès qu'elle sera compatible.";
                return null;
            }
            channelSwitch = true;
        }

        // Version installée (MSI) : on récupère le .msi ; version portable : le .zip.
        var ext = InstallMode.IsInstalled ? ".msi" : ".zip";
        string? assetUrl = null;
        string? assetName = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
                assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                assetName = name;
                break;
            }
        }
        if (assetUrl == null || assetName == null)
            throw new InvalidOperationException($"La release {tag} sur GitHub n'a pas de fichier {ext} en pièce jointe.");

        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";

        return new UpdateInfo(versionText, htmlUrl, releaseNotes, assetUrl, assetName, dbSchema, channelSwitch);
    }

    /// <summary>Télécharge l'archive .zip de la nouvelle version, la décompresse intégralement, puis lance
    /// un processus PowerShell détaché qui attend la fermeture du processus courant, copie tout le
    /// contenu décompressé (l'exécutable, mais aussi tout autre fichier requis à côté - ex. RelevData\
    /// LatoFont) par-dessus le dossier d'installation, relance l'application, puis nettoie le dossier
    /// temporaire. Seuls les fichiers présents dans l'archive sont écrasés/ajoutés : les données propres
    /// à l'utilisateur qui n'y figurent jamais (RelevData\relevpro.db, settings.json, exports...) ne sont
    /// jamais touchées - l'appelant est malgré tout invité à sauvegarder la base avant d'appeler cette
    /// méthode (voir DbContextFactory.CreateBackup), par précaution.
    /// <para>La commande PowerShell est transmise encodée en base64 (-EncodedCommand), jamais comme
    /// ligne de commande classique : les chemins concernés (dossier d'installation, profil utilisateur...)
    /// peuvent contenir des accents ou espaces, et cet encodage évite tout problème de parsing/échappement
    /// côté interpréteur de commandes - PowerShell décode directement les caractères Unicode.</para>
    /// <para>L'appelant doit fermer l'application juste après (le remplacement de l'exécutable ne peut se
    /// faire tant qu'il est verrouillé).</para></summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RelevPro-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, info.AssetName);

        using (var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(zipPath);
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;
                if (total > 0) progress?.Report(readTotal * 100.0 / total);
            }
        }

        var currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossible de déterminer l'exécutable en cours d'exécution.");

        // Version installée : le paquet MSI se charge du remplacement (élévation UAC demandée par msiexec) ;
        // les données (RelevData\relevpro.db, réglages) et la licence (registre) ne sont pas touchées.
        if (zipPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            RunDetachedMsi(zipPath);
            return;
        }

        var extractDir = Path.Combine(tempDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var exeName = Path.GetFileName(currentExePath);
        var installDir = Path.GetDirectoryName(currentExePath)
            ?? throw new InvalidOperationException("Impossible de déterminer le dossier d'installation.");
        var currentPid = Environment.ProcessId;

        // L'archive publiée place toujours l'exécutable directement à sa racine (voir "Publier
        // l'exécutable portable" dans le README) : vérifie que la structure est bien celle attendue
        // avant de lancer le remplacement, plutôt que d'échouer silencieusement plus tard. Le nom
        // attendu est déduit de l'exécutable actuellement lancé plutôt que codé en dur, pour ne pas
        // se désynchroniser si l'exécutable est renommé un jour.
        if (!File.Exists(Path.Combine(extractDir, exeName)))
            throw new InvalidOperationException($"{exeName} introuvable à la racine de l'archive téléchargée.");

        RunDetachedReplace(currentPid, extractDir, installDir, currentExePath);
    }

    /// <summary>Argument de ligne de commande qui fait agir l'application comme assistant de remplacement.</summary>
    public const string ApplyUpdateArg = "--apply-update";

    /// <summary>Version installée (MSI) : lance <c>msiexec</c> élevé (UAC) sur le paquet téléchargé ; l'appelant ferme
    /// l'application aussitôt. Aucun interpréteur de commandes : un PowerShell masqué est classé « stager » par les antivirus.</summary>
    private static void RunDetachedMsi(string msiPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{msiPath}\" /passive /norestart",
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    /// <summary>Version portable : lance la NOUVELLE version, déjà décompressée dans le dossier temporaire, en mode
    /// assistant (<see cref="ApplyUpdateArg"/>) : elle attend la fermeture de l'application, copie l'archive par-dessus
    /// le dossier d'installation et relance l'application (voir <see cref="RunApplyHelper"/>). Ni PowerShell ni script.</summary>
    private static void RunDetachedReplace(int pid, string extractDir, string installDir, string targetExePath)
    {
        var helper = Path.Combine(extractDir, Path.GetFileName(targetExePath));
        var psi = new ProcessStartInfo { FileName = helper, WorkingDirectory = extractDir, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { ApplyUpdateArg, pid.ToString(), extractDir, installDir, targetExePath }) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>Corps du mode assistant : args = [--apply-update, pid, dossier extrait, dossier d'installation, exécutable à relancer].
    /// Attend la fin du processus <c>pid</c>, copie tout (avec nouvelles tentatives tant que des fichiers sont verrouillés),
    /// relance l'application. Le dossier temporaire est nettoyé au lancement suivant (<see cref="CleanOldUpdateFolders"/>).</summary>
    public static void RunApplyHelper(string[] args)
    {
        if (args.Length < 5 || !int.TryParse(args[1], out var pid)) return;
        var (extractDir, installDir, exe) = (args[2], args[3], args[4]);
        try { Process.GetProcessById(pid).WaitForExit(60_000); } catch (ArgumentException) { /* déjà terminé */ } catch { }

        for (var attempt = 1; ; attempt++)
        {
            try { CopyAll(extractDir, installDir); break; }
            catch when (attempt < 15) { Thread.Sleep(1000); }
            catch { return; }
        }
        Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true });
    }

    private static void CopyAll(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from)) CopyAll(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    /// <summary>Supprime les dossiers de mise à jour restés dans %TEMP% (plus de 10 minutes) ; sans erreur.</summary>
    public static void CleanOldUpdateFolders()
    {
        try
        {
            foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "RelevPro-Update-*"))
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(d) > TimeSpan.FromMinutes(10))
                    try { Directory.Delete(d, recursive: true); } catch { }
        }
        catch { }
    }

    // Comparaison de versions commune (suffixe de canal « -dev » ignoré) : voir TomLine.AppInfo.
    private static bool IsNewer(string candidate, string current) => TomLine.AppInfo.AppVersion.IsNewer(candidate, current);
}
