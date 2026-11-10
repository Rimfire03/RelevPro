using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RelevPro.Services;

public static class VisualTreeExtensions
{
    /// <summary>Premier ancêtre de type <typeparamref name="T"/> (arbre visuel, ou logique pour les éléments
    /// de texte comme Run), en partant de l'élément lui-même.</summary>
    public static T? FindAncestor<T>(this DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as T;
    }
}
