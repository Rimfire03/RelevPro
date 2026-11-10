using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RelevPro.Services;

public static class ImageLoader
{
    /// <summary>Image WPF à partir d'un fichier en mémoire, ou null si le contenu n'est pas une image lisible.</summary>
    public static ImageSource? TryCreate(byte[] data)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(data);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
