using System.Text.Json;
using RelevPro.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

/// <summary>Résultat du contrôle d'accès au démarrage : <see cref="Allowed"/> = la session Windows peut écrire.</summary>
public sealed record AccessState(bool Allowed, bool FirstUser, bool Enabled);

/// <summary>Contenu des listes d'accès : utilisateurs autorisés en lecture/écriture et utilisateurs connus.</summary>
public sealed record AccessLists(List<string> Writers, List<string> Known, bool Enabled);

/// <summary>
/// Utilisateurs autorisés à modifier les données (lecture/écriture). Les autres sont en lecture seule.
/// Les listes sont stockées dans la table <c>DbInfo</c> (clés <c>AccessWriters</c> et <c>AccessKnown</c>, JSON) : elles sont
/// donc communes à tous les postes, en mode fichier SQLite comme sur serveur, et ne changent pas le schéma de la base.
/// <list type="bullet">
/// <item>« jean » : la session Windows « jean », quel que soit le domaine ;</item>
/// <item>« DOMAINE\jean » ou « jean@domaine » : cette session précise ;</item>
/// <item>« @domaine » : toutes les sessions de ce domaine.</item>
/// </list>
/// La fonction est désactivée par défaut (clé <c>AccessEnabled</c>) : tout le monde peut alors écrire. À l'activation, si la
/// liste des autorisés est vide, la session qui active y est ajoutée et obtient les droits d'écriture.
/// En cas d'erreur de lecture, l'accès est accordé (on ne verrouille jamais l'application sur un incident technique).
/// </summary>
public static class AccessControl
{
    private const string WritersKey = "AccessWriters";
    private const string KnownKey = "AccessKnown";
    private const string EnabledKey = "AccessEnabled";

    public static string CurrentUser => Environment.UserName;
    public static string CurrentDomain => Environment.UserDomainName;

    /// <summary>Identité complète de la session, forme « DOMAINE\utilisateur ».</summary>
    public static string CurrentIdentity => $"{CurrentDomain}\\{CurrentUser}";

    /// <summary>L'entrée de la liste désigne-t-elle cette session ?</summary>
    public static bool Matches(string entry, string user, string domain)
    {
        entry = entry.Trim();
        if (entry.Length == 0) return false;

        if (entry[0] == '@')
            return string.Equals(entry[1..].Trim(), domain, StringComparison.OrdinalIgnoreCase);

        var slash = entry.IndexOf('\\');
        if (slash > 0)
            return string.Equals(entry[..slash], domain, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(entry[(slash + 1)..], user, StringComparison.OrdinalIgnoreCase);

        var at = entry.IndexOf('@');
        if (at > 0)
            return string.Equals(entry[..at], user, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(entry[(at + 1)..], domain, StringComparison.OrdinalIgnoreCase);

        return string.Equals(entry, user, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowed(IEnumerable<string> writers, string user, string domain) =>
        writers.Any(w => Matches(w, user, domain));

    public static bool IsCurrentUserAllowed(IEnumerable<string> writers) =>
        IsAllowed(writers, CurrentUser, CurrentDomain);

    /// <summary>Contrôle l'accès de cette session au démarrage et enregistre l'utilisateur dans la liste des connus.
    /// Si la liste des autorisés est vide, y ajoute cette session (premier utilisateur connecté).</summary>
    public static AccessState Evaluate(DbTarget target)
    {
        try
        {
            if (!target.IsServer && !File.Exists(target.FilePath)) return new AccessState(true, true, false);

            using var ctx = new RelevDbContext(target, false);
            ServerSql.EnsureDbInfo(ctx);
            var lists = Read(ctx);

            // Fonction désactivée : aucun contrôle, tout le monde peut modifier les données.
            if (!lists.Enabled) return new AccessState(true, false, false);

            var firstUser = lists.Writers.Count == 0;
            if (firstUser)
            {
                lists.Writers.Add(CurrentIdentity);
                Write(ctx, WritersKey, lists.Writers);
            }

            if (!lists.Known.Any(k => string.Equals(k, CurrentIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                lists.Known.Add(CurrentIdentity);
                Write(ctx, KnownKey, lists.Known);
            }

            return new AccessState(IsCurrentUserAllowed(lists.Writers), firstUser, true);
        }
        catch
        {
            return new AccessState(true, false, false);
        }
    }

    public static AccessLists Load(DbTarget target)
    {
        using var ctx = new RelevDbContext(target, false);
        ServerSql.EnsureDbInfo(ctx);
        return Read(ctx);
    }

    /// <summary>Active ou désactive la fonction pour tous les postes. À l'activation, si la liste des autorisés est vide,
    /// la session courante y est ajoutée (et aux utilisateurs connus). Retourne les listes à jour.</summary>
    public static AccessLists SetEnabled(DbTarget target, bool enabled)
    {
        using var ctx = new RelevDbContext(target, false);
        ServerSql.EnsureDbInfo(ctx);
        var lists = Read(ctx);
        if (enabled && lists.Writers.Count == 0)
        {
            lists.Writers.Add(CurrentIdentity);
            Write(ctx, WritersKey, lists.Writers);
            if (!lists.Known.Any(k => string.Equals(k, CurrentIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                lists.Known.Add(CurrentIdentity);
                Write(ctx, KnownKey, lists.Known);
            }
        }
        ServerSql.SetInfo(ctx, EnabledKey, enabled ? "1" : "0");
        return lists with { Enabled = enabled };
    }

    // Empreinte SHA-256 du mot de passe permettant à une session de se déclarer elle-même utilisateur lecture/écriture.
    // Barrière « douce » (le contrôle est côté application) : elle évite surtout les ajouts involontaires.
    private const string SelfEnrollPasswordHash = "BCE9BF9DF19B1F049E5BAE37FC9F1B6007DFFAD92B2F7826BC55A207426D814A";

    public static bool VerifySelfEnrollPassword(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password)));
        return string.Equals(hash, SelfEnrollPasswordHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ajoute la session courante aux utilisateurs lecture/écriture (et aux connus) : à n'appeler qu'après
    /// <see cref="VerifySelfEnrollPassword"/>. Retourne les listes à jour.</summary>
    public static AccessLists EnrollCurrentUser(DbTarget target)
    {
        using var ctx = new RelevDbContext(target, false);
        ServerSql.EnsureDbInfo(ctx);
        var lists = Read(ctx);
        if (!lists.Writers.Any(w => string.Equals(w, CurrentIdentity, StringComparison.OrdinalIgnoreCase)))
        {
            lists.Writers.Add(CurrentIdentity);
            Write(ctx, WritersKey, lists.Writers);
        }
        if (!lists.Known.Any(k => string.Equals(k, CurrentIdentity, StringComparison.OrdinalIgnoreCase)))
        {
            lists.Known.Add(CurrentIdentity);
            Write(ctx, KnownKey, lists.Known);
        }
        return lists;
    }

    public static void Save(DbTarget target, IEnumerable<string> writers, IEnumerable<string> known)
    {
        using var ctx = new RelevDbContext(target, false);
        ServerSql.EnsureDbInfo(ctx);
        Write(ctx, WritersKey, Normalize(writers));
        Write(ctx, KnownKey, Normalize(known));
    }

    private static AccessLists Read(RelevDbContext ctx) =>
        new(Parse(ServerSql.GetInfo(ctx, WritersKey)), Parse(ServerSql.GetInfo(ctx, KnownKey)),
            ServerSql.GetInfo(ctx, EnabledKey) == "1");

    private static void Write(RelevDbContext ctx, string key, List<string> values) =>
        ServerSql.SetInfo(ctx, key, JsonSerializer.Serialize(values));

    private static List<string> Normalize(IEnumerable<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
