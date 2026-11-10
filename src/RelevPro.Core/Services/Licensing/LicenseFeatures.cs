namespace RelevPro.Core.Services.Licensing;

/// <summary>Identifiants de fonctionnalités de licence (valeurs de <c>license.features</c> renvoyées par le serveur
/// de licences) : une seule définition par module, à utiliser avec <see cref="LicenseManager.HasFeature"/>.</summary>
public static class LicenseFeatures
{
    /// <summary>Fonctionnalité « multisite ».</summary>
    public const string MultiSite = "multisite";
}
