namespace RelevPro.Services;

/// <summary>Lecture d'un nombre entier saisi directement dans une cellule de grille.</summary>
public static class IntInput
{
    /// <summary>Texte vide = valeur effacée (null). Retourne false, après un message, si le texte n'est
    /// pas un nombre entier.</summary>
    public static bool TryParse(string text, string fieldLabel, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var parsed))
        {
            value = parsed;
            return true;
        }

        App.Dialogs.ShowMessage(fieldLabel, $"« {text.Trim()} » n'est pas un nombre entier : la valeur n'a pas été enregistrée.");
        return false;
    }
}
