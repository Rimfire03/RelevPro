using System.Globalization;

namespace RelevPro.Core.Services;

/// <summary>Tri « naturel » des noms (centrales, lieux, familles) : les suites de chiffres se comparent comme
/// des nombres, le reste comme du texte sans tenir compte de la casse ni des espaces avant un nombre (« CTA 3 » et « CTA3 » se rangent pareil). CTA2 vient avant CTA10 (l'ordre
/// alphabétique simple donnait CTA10 avant CTA2).</summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var numX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numY = y.AsSpan(startY, j - startY).TrimStart('0');
                if (numX.Length != numY.Length) return numX.Length < numY.Length ? -1 : 1;
                var cmp = numX.CompareTo(numY, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && !char.IsDigit(x[i])) i++;
                while (j < y.Length && !char.IsDigit(y[j])) j++;
                var cmp = CultureInfo.CurrentCulture.CompareInfo.Compare(
                    x.AsSpan(startX, i - startX).TrimEnd(), y.AsSpan(startY, j - startY).TrimEnd(), CompareOptions.IgnoreCase);
                if (cmp != 0) return cmp;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
