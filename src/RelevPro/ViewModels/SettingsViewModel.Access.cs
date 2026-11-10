using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Services;

namespace RelevPro.ViewModels;

/// <summary>Onglet « Utilisateurs » des Paramètres : liste des sessions Windows autorisées en lecture/écriture
/// (voir <see cref="AccessControl"/>). À gauche les utilisateurs connus, à droite les utilisateurs autorisés.</summary>
public partial class SettingsViewModel
{
    public ObservableCollection<string> KnownUsers { get; } = [];
    public ObservableCollection<string> WriterUsers { get; } = [];

    [ObservableProperty] private string? _selectedKnownUser;
    [ObservableProperty] private string? _selectedWriterUser;
    [ObservableProperty] private string _newUserName = string.Empty;
    [ObservableProperty] private string _accessStatusMessage = string.Empty;

    // Une seule sélection à la fois entre les deux listes : « Supprimer l'utilisateur » agit sans ambiguïté.
    partial void OnSelectedKnownUserChanged(string? value)
    {
        if (value is not null) SelectedWriterUser = null;
    }

    partial void OnSelectedWriterUserChanged(string? value)
    {
        if (value is not null) SelectedKnownUser = null;
    }

    /// <summary>Gestion des utilisateurs activée (case à cocher, commune à tous les postes). Désactivée par défaut :
    /// tout le monde peut alors modifier les données.</summary>
    [ObservableProperty] private bool _accessEnabled;

    private bool _loadingAccess;

    partial void OnAccessEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditAccess));
        OnPropertyChanged(nameof(AccessIdentityText));
        OnPropertyChanged(nameof(ShowSelfEnroll));
        if (_loadingAccess) return;

        if (!CanToggleAccess || !App.GuardWritable())
        {
            SetAccessEnabledSilently(!value);
            return;
        }

        try
        {
            if (value)
            {
                var current = AccessControl.Load(App.DbFactory.Target);
                if (current.Writers.Count > 0 && !AccessControl.IsCurrentUserAllowed(current.Writers) &&
                    !App.Dialogs.ShowConfirm("Utilisateurs autorisés",
                        $"Votre session « {AccessControl.CurrentIdentity} » n'est pas dans la liste des utilisateurs autorisés : vous passerez en lecture seule à la prochaine ouverture.\n\nActiver quand même ?"))
                {
                    SetAccessEnabledSilently(false);
                    return;
                }
            }

            var wasEmpty = AccessControl.Load(App.DbFactory.Target).Writers.Count == 0;
            AccessControl.SetEnabled(App.DbFactory.Target, value);
            ReloadAccess();
            AccessStatusMessage = value
                ? (wasEmpty ? $"Activée : « {AccessControl.CurrentIdentity} » est le premier utilisateur autorisé en lecture/écriture. " : "Activée. ")
                  + "S'applique à la prochaine ouverture de l'application sur chaque poste."
                : "Désactivée : tous les utilisateurs peuvent modifier les données (à la prochaine ouverture de chaque poste).";
        }
        catch (Exception ex)
        {
            SetAccessEnabledSilently(!value);
            AccessStatusMessage = "Changement impossible : " + ex.Message;
        }
    }

    private void SetAccessEnabledSilently(bool value)
    {
        _loadingAccess = true;
        try { AccessEnabled = value; }
        finally { _loadingAccess = false; }
    }

    public string AccessIdentityText => !AccessEnabled
        ? "Gestion des utilisateurs désactivée : toutes les sessions Windows peuvent modifier les données."
        : $"Session Windows : {AccessControl.CurrentIdentity}" + (App.AccessAllowed ? "  (accès lecture/écriture approuvé)" : "  (accès non approuvé : lecture seule)");

    /// <summary>Seul un utilisateur autorisé et non en lecture seule peut activer/désactiver la fonction.</summary>
    public bool CanToggleAccess => App.IsWritable && App.AccessAllowed;

    /// <summary>La liste n'est modifiable que si la fonction est activée.</summary>
    public bool CanEditAccess => CanToggleAccess && AccessEnabled;

    [RelayCommand]
    private void ReloadAccess()
    {
        try
        {
            var lists = AccessControl.Load(App.DbFactory.Target);
            var known = lists.Known.Concat(lists.Writers).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => !lists.Writers.Contains(k, StringComparer.OrdinalIgnoreCase));
            Fill(KnownUsers, known);
            Fill(WriterUsers, lists.Writers);
            SetAccessEnabledSilently(lists.Enabled);
            OnPropertyChanged(nameof(CanEditAccess));
            OnPropertyChanged(nameof(AccessIdentityText));
            AccessStatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            AccessStatusMessage = "Liste des utilisateurs illisible : " + ex.Message;
        }
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var v in values.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)) target.Add(v);
    }

    private static void Insert(ObservableCollection<string> target, string value)
    {
        if (target.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        var index = 0;
        while (index < target.Count && string.Compare(target[index], value, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
        target.Insert(index, value);
    }

    /// <summary>Le bouton « S'ajouter par mot de passe » n'apparaît que si la fonction est active et que cette session n'est
    /// pas (encore) autorisée.</summary>
    public bool ShowSelfEnroll => AccessEnabled && !App.AccessAllowed;

    /// <summary>Permet à la session courante de se déclarer utilisateur lecture/écriture en saisissant le mot de passe.</summary>
    [RelayCommand]
    private void SelfEnrollWithPassword()
    {
        var window = new RelevPro.Views.Dialogs.PasswordPromptWindow(
            "Accès en lecture/écriture",
            $"Saisissez le mot de passe pour autoriser la session « {AccessControl.CurrentIdentity} » à modifier les données.",
            AccessControl.VerifySelfEnrollPassword)
        { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() != true) return;

        try
        {
            AccessControl.EnrollCurrentUser(App.DbFactory.Target);
            ReloadAccess();
            AccessStatusMessage = $"« {AccessControl.CurrentIdentity} » est maintenant autorisé en lecture/écriture.";
            if (App.Dialogs.ShowConfirm("Accès en lecture/écriture",
                    "Votre session est autorisée. Redémarrer l'application maintenant pour pouvoir modifier les données ?"))
                App.Restart();
        }
        catch (Exception ex)
        {
            AccessStatusMessage = "Ajout impossible : " + ex.Message;
        }
    }

    /// <summary>Flèche « Ajouter » : déplace l'utilisateur connu sélectionné vers les utilisateurs autorisés.</summary>
    [RelayCommand]
    private void AddAccessUser()
    {
        if (!CanChangeAccess() || SelectedKnownUser is not { } user) return;
        KnownUsers.Remove(user);
        Insert(WriterUsers, user);
        Persist();
    }

    /// <summary>Flèche « Supprimer » : retire l'utilisateur autorisé sélectionné (il reste dans les utilisateurs connus).</summary>
    [RelayCommand]
    private void RemoveAccessUser()
    {
        if (!CanChangeAccess() || SelectedWriterUser is not { } user) return;
        if (!ConfirmLeavingWriters(user)) return;
        WriterUsers.Remove(user);
        Insert(KnownUsers, user);
        Persist();
    }

    /// <summary>Supprime définitivement l'utilisateur sélectionné (dans l'une ou l'autre liste). Un utilisateur connu qui
    /// se reconnecte réapparaît dans les utilisateurs connus.</summary>
    [RelayCommand]
    private void DeleteAccessUser()
    {
        if (!CanChangeAccess()) return;
        if (SelectedWriterUser is { } writer)
        {
            if (!ConfirmLeavingWriters(writer)) return;
            WriterUsers.Remove(writer);
        }
        else if (SelectedKnownUser is { } known)
            KnownUsers.Remove(known);
        else
            return;
        Persist();
    }

    /// <summary>Crée un utilisateur pas encore connecté (ou un domaine « @domaine ») : reconnu à sa première connexion.
    /// Validé par la touche Entrée ; il est ajouté aux utilisateurs connus, la flèche lui donne l'accès en écriture.</summary>
    [RelayCommand]
    private void CreateAccessUser()
    {
        if (!CanChangeAccess()) return;
        var name = NewUserName.Trim();
        if (name.Length == 0) return;
        if (name == "@")
        {
            AccessStatusMessage = "Saisissez un domaine après le « @ » (exemple : @entreprise).";
            return;
        }
        if (WriterUsers.Contains(name, StringComparer.OrdinalIgnoreCase) || KnownUsers.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            AccessStatusMessage = $"« {name} » est déjà dans la liste.";
            return;
        }
        Insert(KnownUsers, name);
        SelectedKnownUser = name;
        NewUserName = string.Empty;
        Persist($"« {name} » créé : utilisez la flèche « Ajouter » pour lui donner l'accès en écriture.");
    }

    private bool CanChangeAccess() => App.GuardWritable() && App.AccessAllowed;

    /// <summary>Retirer sa propre session (ou le dernier utilisateur) de la liste des autorisés est confirmé / refusé.</summary>
    private bool ConfirmLeavingWriters(string user)
    {
        var remaining = WriterUsers.Where(w => !string.Equals(w, user, StringComparison.OrdinalIgnoreCase)).ToList();
        if (remaining.Count == 0)
        {
            AccessStatusMessage = "Il doit rester au moins un utilisateur autorisé en lecture/écriture.";
            return false;
        }
        if (AccessControl.IsCurrentUserAllowed(remaining)) return true;
        return App.Dialogs.ShowConfirm("Utilisateurs autorisés",
            $"Votre session « {AccessControl.CurrentIdentity} » ne sera plus autorisée : vous passerez en lecture seule à la prochaine ouverture.\n\nContinuer ?");
    }

    /// <summary>Enregistre immédiatement les deux listes dans la base (plus de bouton « Enregistrer »).</summary>
    private void Persist(string? message = null)
    {
        try
        {
            AccessControl.Save(App.DbFactory.Target, WriterUsers, KnownUsers.Concat(WriterUsers));
            AccessStatusMessage = message ?? "Enregistré. Les changements s'appliquent à la prochaine ouverture de l'application sur chaque poste.";
        }
        catch (Exception ex)
        {
            AccessStatusMessage = "Enregistrement impossible : " + ex.Message;
        }
    }
}