using System.IO;
using System.Text;
using System.Windows;
using RelevPro.Views.Dialogs;

namespace RelevPro;

/// <summary>Contrat de licence utilisateur final (CLUF) : le texte est intégré à l'exécutable
/// (Legal\CLUF.txt, ressource incorporée) ; sa copie dans RelevData\CLUF.txt vaut acceptation de CETTE
/// version du texte. Le contrat est présenté au démarrage, et doit être accepté pour continuer, tant que ce
/// fichier est absent (première installation, fichier supprimé) ou que son contenu diffère du texte intégré
/// (contrat modifié par une nouvelle release). Relisible à tout moment depuis Paramètres
/// (<see cref="ShowEula"/>).</summary>
public partial class App
{
    private const string EulaResourceName = "RelevPro.CLUF.txt";

    private static string EulaFilePath => Path.Combine(AppContext.BaseDirectory, "RelevData", "CLUF.txt");

    /// <summary>Texte du contrat de cette version du logiciel (ressource incorporée).</summary>
    private static string LoadEulaText()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream(EulaResourceName)
                           ?? throw new InvalidOperationException($"Ressource {EulaResourceName} introuvable.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Texte déjà accepté sur ce poste (copie dans RelevData), ou null s'il n'y en a pas ou
    /// qu'elle est illisible.</summary>
    private static string? ReadAcceptedEulaText()
    {
        try
        {
            return File.Exists(EulaFilePath) ? File.ReadAllText(EulaFilePath, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Comparaison du contenu seul : fins de ligne (Git peut les convertir), BOM et blancs de
    /// début / fin ne comptent pas comme une modification du contrat.</summary>
    private static string NormalizeEula(string text) => text.Replace("\r\n", "\n").Trim('﻿', ' ', '\n', '\r', '\t');

    /// <summary>False si l'utilisateur refuse le contrat (l'application doit alors s'arrêter). Un refus
    /// après mise à jour laisse en place l'ancienne acceptation : le nouveau contrat sera redemandé au
    /// prochain lancement.</summary>
    private static bool EnsureEulaAccepted()
    {
        var text = LoadEulaText();
        var accepted = ReadAcceptedEulaText();
        if (accepted is not null && NormalizeEula(accepted) == NormalizeEula(text)) return true;

        if (new EulaWindow(text, isUpdate: accepted is not null).ShowDialog() != true) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EulaFilePath)!);
            File.WriteAllText(EulaFilePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            // Acceptation valable pour cette session ; elle sera redemandée au prochain lancement.
            MessageBox.Show($"Le contrat a été accepté, mais il n'a pas pu être enregistré dans :\n{EulaFilePath}\n\n{ex.Message}\n\n" +
                            "Il vous sera de nouveau présenté au prochain lancement.",
                "Contrat de licence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }

    /// <summary>Relecture du contrat (bouton de l'écran Paramètres), sans demande d'acceptation.</summary>
    public static void ShowEula() =>
        new EulaWindow(LoadEulaText(), readOnly: true) { Owner = Current.MainWindow }.ShowDialog();
}
