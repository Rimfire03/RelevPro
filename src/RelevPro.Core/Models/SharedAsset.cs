namespace RelevPro.Core.Models;

/// <summary>Fichier partagé par tous les postes, stocké dans la base (ex. logo de l'entreprise).</summary>
public class SharedAsset
{
    public string Key { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
