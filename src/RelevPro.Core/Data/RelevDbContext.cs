using RelevPro.Core.Models;
using RelevPro.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Data;

public class RelevDbContext : DbContext
{
    private readonly DbTarget _target;
    private readonly bool _readOnly;

    // Version du serveur MariaDB/MySQL détectée une fois par serveur (la détection ouvre une connexion).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServerVersion> MySqlVersions = new();

    public RelevDbContext(string dbPath, bool readOnly = false) : this(DbTarget.ForFile(dbPath), readOnly) { }

    public RelevDbContext(DbTarget target, bool readOnly = false)
    {
        _target = target;
        _readOnly = readOnly;
    }

    public DatabaseProvider Provider => _target.Provider;

    /// <summary>Site affiché (module MultiSite) : toutes les lectures, modifications et suppressions des tables propres à un site sont limitées à ce site (filtre de requête global), et les lignes ajoutées lui sont rattachées. Modifiable à tout moment ; 1 = site principal.</summary>
    public int CurrentSiteId { get; set; } = 1;

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SharedAsset> SharedAssets => Set<SharedAsset>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<ReadingView> ReadingViews => Set<ReadingView>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<ViewLine> ViewLines => Set<ViewLine>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<TaskCompletion> TaskCompletions => Set<TaskCompletion>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Journal SQLite par défaut (DELETE) volontairement conservé : le mode WAL ne fonctionne pas sur
        // un disque réseau. Le délai d'attente couvre les lectures qui tombent pendant une écriture.
        switch (_target.Provider)
        {
            case DatabaseProvider.Sqlite:
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _target.FilePath,
                    Mode = _readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                    DefaultTimeout = 30
                }.ToString();
                optionsBuilder.UseSqlite(connectionString);
                break;

            case DatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(_target.Server!.BuildConnectionString());
                break;

            case DatabaseProvider.MariaDb:
                var mysql = _target.Server!.BuildConnectionString();
                optionsBuilder.UseMySql(mysql, MySqlVersions.GetOrAdd(mysql, ServerVersion.AutoDetect));
                break;

            case DatabaseProvider.SqlServer:
                optionsBuilder.UseSqlServer(_target.Server!.BuildConnectionString());
                break;
        }

        // Contexte partagé pour toute la session : sans suivi, chaque requête relit la base et voit donc
        // les modifications enregistrées entre-temps par le poste rédacteur.
        if (_readOnly) optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        // Serveur : pas de mode lecture seule natif comme SQLite, donc toute écriture est refusée par un intercepteur.
        if (_readOnly && _target.IsServer) optionsBuilder.AddInterceptors(ReadOnlyGuard.Instance);
    }

    // Suivi de la dernière saisie sur serveur (pas de déclencheurs SQLite) : voir DatabaseWriteTracking.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (_readOnly && _target.IsServer) throw ReadOnlyGuard.Error();

        foreach (var entry in ChangeTracker.Entries<ISiteScoped>())
            if (entry.State == EntityState.Added && entry.Entity.SiteId == 0) entry.Entity.SiteId = CurrentSiteId;

        var changed = Provider != DatabaseProvider.Sqlite && ChangeTracker.HasChanges();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (changed && result > 0) DatabaseWriteTracking.StampServer(this);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SharedAsset>().HasKey(a => a.Key);

        modelBuilder.Entity<ReadingView>().Property(v => v.Famille).HasMaxLength(200);
        modelBuilder.Entity<ViewLine>().Property(l => l.Groupe).HasMaxLength(200);
        modelBuilder.Entity<Meter>().Property(m => m.Famille).HasMaxLength(200);
        modelBuilder.Entity<CatalogItem>().Property(c => c.Nom).HasMaxLength(100);
        modelBuilder.Entity<Meter>().Property(m => m.SeuilHaut).HasPrecision(18, 4);
        modelBuilder.Entity<Meter>().Property(m => m.Capacite).HasPrecision(18, 4);
        modelBuilder.Entity<ViewLine>().HasOne(l => l.ReadingView).WithMany(v => v.Lines).HasForeignKey(l => l.ReadingViewId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ViewLine>().HasOne(l => l.Meter).WithMany().HasForeignKey(l => l.MeterId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MeterReading>().HasOne(r => r.Meter).WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskCompletion>().HasOne(t => t.ViewLine).WithMany().HasForeignKey(t => t.ViewLineId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ViewLine>().Ignore(l => l.Titre);
        modelBuilder.Entity<MeterReading>().Property(r => r.Value).HasPrecision(18, 4);
        modelBuilder.Entity<MeterReading>().HasIndex(r => new { r.MeterId, r.Date });

        // MultiSite : chaque table propre à un site est limitée au site courant du contexte.
        foreach (var type in modelBuilder.Model.GetEntityTypes().Select(e => e.ClrType).Where(t => typeof(ISiteScoped).IsAssignableFrom(t)).ToList())
        {
            var e = System.Linq.Expressions.Expression.Parameter(type, "e");
            var filter = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Equal(
                    System.Linq.Expressions.Expression.Property(e, nameof(ISiteScoped.SiteId)),
                    System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(this), nameof(CurrentSiteId))),
                e);
            modelBuilder.Entity(type).HasQueryFilter(filter);
        }
    }
}

/// <summary>Refuse toute commande d'écriture sur un poste en lecture seule connecté à un serveur de base de données
/// (voir <see cref="ServerWriteLock"/>).</summary>
internal sealed class ReadOnlyGuard : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public static readonly ReadOnlyGuard Instance = new();

    public static InvalidOperationException Error() =>
        new("Ce poste est en lecture seule : un autre poste a l'accès en écriture à la base. Aucune modification n'a été enregistrée.");

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> NonQueryExecuting(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result) => throw Error();

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw Error();
}