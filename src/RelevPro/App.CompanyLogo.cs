using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro;

/// <summary>Logo de l'entreprise, stocké dans la base et commun à tous les postes.</summary>
public partial class App
{
    /// <summary>Logo de l'entreprise stocké dans la base (null si aucun), et son image prête à afficher.</summary>
    public static byte[]? CompanyLogo { get; private set; }
    public static System.Windows.Media.ImageSource? CompanyLogoImage { get; private set; }
    public static event Action? CompanyLogoChanged;

    /// <summary>Enregistre (ou retire, si null) le logo dans la base et met à jour l'affichage.</summary>
    public static void SetCompanyLogo(byte[]? data)
    {
        if (data is null) CompanyLogoService.Remove(Db);
        else CompanyLogoService.Set(Db, data);
        LoadCompanyLogo();
    }

    private static void LoadCompanyLogo()
    {
        CompanyLogo = CompanyLogoService.Get(Db);
        CompanyLogoImage = CompanyLogo is null ? null : ImageLoader.TryCreate(CompanyLogo);
        CompanyLogoChanged?.Invoke();
    }
}
