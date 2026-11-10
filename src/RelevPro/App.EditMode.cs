namespace RelevPro;

public partial class App
{
    /// <summary>Mode édition (structure : vues, lignes, compteurs, catalogues). Désactivé à chaque lancement ; la saisie
    /// des relevés et des tâches reste toujours possible.</summary>
    public static bool IsEditMode { get; set; }
}
