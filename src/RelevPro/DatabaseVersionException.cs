namespace RelevPro;

/// <summary>Logiciel et base de données à des versions incompatibles : le démarrage est bloqué.
/// <paramref name="appTooOld"/> : la base est plus récente que le logiciel (mettre le logiciel à jour).</summary>
public class DatabaseVersionException(string message, bool appTooOld = false) : Exception(message)
{
    public bool AppTooOld { get; } = appTooOld;
}
