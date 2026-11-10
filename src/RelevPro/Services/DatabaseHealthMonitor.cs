using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RelevPro.Core.Data;
using RelevPro.Core.Services;

namespace RelevPro.Services;

/// <summary>Contrôle régulier de l'accès à la base (serveur ou fichier) : alimente la puce verte / rouge du
/// pied de page. Une vérification légère (<c>SELECT 1</c> sur une connexion neuve, délai court) tourne en
/// arrière-plan à intervalle régulier ; une seule à la fois.</summary>
public partial class DatabaseHealthMonitor : ObservableObject
{
    private const int IntervalSeconds = 15;
    private const int ProbeTimeoutSeconds = 5;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(IntervalSeconds) };
    private int _running;
    private DateTime? _downSince;

    /// <summary>Une vérification a déjà abouti (sinon la puce reste grise).</summary>
    [ObservableProperty] private bool _isKnown;

    /// <summary>Dernier contrôle : base accessible.</summary>
    [ObservableProperty] private bool _isOk;

    /// <summary>Dernier contrôle : base inaccessible (perte de connexion).</summary>
    [ObservableProperty] private bool _isDown;

    /// <summary>Poste en lecture seule (un autre poste écrit dans le fichier) : puce orange quand la base répond.</summary>
    [ObservableProperty] private bool _isReadOnlyMode;

    [ObservableProperty] private string _tooltip = "Vérification de la base en cours...";

    public DatabaseHealthMonitor()
    {
        _timer.Tick += async (_, _) => await CheckNowAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = CheckNowAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task CheckNowAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return; // contrôle précédent encore en cours
        try
        {
            var target = App.DbFactory.Target;
            var error = await Task.Run(() => Probe(target));
            Apply(error, target);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void Apply(string? error, DbTarget target)
    {
        var now = DateTime.Now;
        IsKnown = true;
        if (error is null)
        {
            _downSince = null;
            IsOk = true;
            IsDown = false;
            IsReadOnlyMode = App.IsReadOnly;
            Tooltip = $"{(target.IsServer ? "Serveur" : "Base")} accessible - {target.Describe()}" +
                      (App.IsReadOnly ? "\nPoste en LECTURE SEULE : aucune modification n'est enregistrée." : "") +
                      $"\nDernier contrôle : {now:HH:mm:ss}";
        }
        else
        {
            _downSince ??= now;
            IsOk = false;
            IsDown = true;
            Tooltip = $"Connexion perdue depuis {_downSince:HH:mm:ss} - {target.Describe()}\n{error}\nNouvel essai automatique toutes les {IntervalSeconds} s.";
        }
    }

    /// <summary>Retourne null si la base répond, sinon le motif de l'échec.</summary>
    private static string? Probe(DbTarget target)
    {
        try
        {
            if (!target.IsServer)
            {
                if (!File.Exists(target.FilePath)) return "Fichier de base introuvable.";
                using var ro = new RelevDbContext(target, readOnly: true);
                RunSelectOne(ro);
                return null;
            }

            var server = target.Server!.Clone();
            server.ConnectTimeoutSeconds = ProbeTimeoutSeconds;
            using var ctx = new RelevDbContext(DbTarget.ForServer(server));
            RunSelectOne(ctx);
            return null;
        }
        catch (Exception ex)
        {
            while (ex.InnerException is not null) ex = ex.InnerException;
            return ex.Message;
        }
    }

    private static void RunSelectOne(RelevDbContext ctx)
    {
        var conn = ctx.Database.GetDbConnection();
        conn.Open();
        try
        {
            using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = ProbeTimeoutSeconds;
            cmd.ExecuteScalar();
        }
        finally
        {
            conn.Close();
        }
    }
}
