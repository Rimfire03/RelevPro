using System.Text;

namespace RelevPro.Core.Services;

/// <summary>
/// Verrou "un seul rédacteur" sur la base partagée (disque réseau) : le premier processus qui ouvre la
/// base garde ouvert en exclusivité d'écriture un fichier "&lt;base&gt;.lock" voisin ; tous les autres
/// ne peuvent que lire la base. Le verrou repose sur les modes de partage de fichier Windows (appliqués
/// par le serveur SMB), donc il est libéré automatiquement par le système si l'application plante ou si
/// le poste est coupé : aucun fichier de verrou "orphelin" à supprimer à la main.
/// </summary>
public sealed class DbWriteLock : IDisposable
{
    private readonly FileStream _stream;

    private DbWriteLock(FileStream stream) => _stream = stream;

    public static string GetLockPath(string dbPath) => dbPath + ".lock";

    /// <summary>Retourne le verrou si ce processus devient le rédacteur, null si un autre poste le détient
    /// déjà.</summary>
    public static DbWriteLock? TryAcquire(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        FileStream stream;
        try
        {
            // FileShare.Read : les autres postes peuvent lire le nom du détenteur, mais pas ouvrir en écriture.
            stream = new FileStream(GetLockPath(dbPath), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // 1re ligne : nom de la session Windows (lu par ReadOwner) ; les suivantes sont informatives.
        var owner = Encoding.UTF8.GetBytes($"{Environment.UserName}\nPoste : {Environment.MachineName}\nDepuis le {DateTime.Now:dd/MM/yyyy HH:mm}");
        stream.SetLength(0);
        stream.Write(owner);
        stream.Flush(flushToDisk: true);
        return new DbWriteLock(stream);
    }

    /// <summary>Nom de la session Windows qui détient actuellement l'accès en écriture, ou null si illisible.</summary>
    public static string? ReadOwner(string dbPath)
    {
        try
        {
            using var stream = new FileStream(GetLockPath(dbPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var user = reader.ReadLine()?.Trim();
            return string.IsNullOrEmpty(user) ? null : user;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
