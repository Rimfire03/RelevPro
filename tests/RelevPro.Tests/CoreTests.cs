using Xunit;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Tests;

public class CoreTests
{
    private static MeterReading R(string d, decimal v) => new() { Date = DateTime.Parse(d), Value = v };
    private static readonly Meter M = new() { Nom = "Eau", Unite = "m3", SeuilEcartPct = 100 };

    [Fact]
    public void Decreasing_value_is_flagged()
    {
        var a = ReadingValidator.Check(M, new DateTime(2026, 3, 1), 90, new[] { R("2026-02-01", 100) });
        Assert.Single(a);
    }

    [Fact]
    public void Normal_increase_is_accepted_and_big_gap_flagged()
    {
        var hist = new[] { R("2026-01-01", 0), R("2026-02-01", 100), R("2026-03-01", 200), R("2026-04-01", 300) };
        Assert.Empty(ReadingValidator.Check(M, new DateTime(2026, 5, 1), 410, hist));
        Assert.Single(ReadingValidator.Check(M, new DateTime(2026, 5, 1), 900, hist));
    }

    [Fact]
    public void Value_above_next_reading_is_flagged()
    {
        var a = ReadingValidator.Check(M, new DateTime(2026, 2, 15), 500, new[] { R("2026-02-01", 100), R("2026-03-01", 200) });
        Assert.Single(a);
    }

    [Fact]
    public void Periods_boundaries()
    {
        Assert.Equal(new DateTime(2026, 11, 9), Periods.Start(Recurrence.Hebdo, new DateTime(2026, 11, 15)));
        Assert.Equal(new DateTime(2026, 7, 1), Periods.Start(Recurrence.Semestriel, new DateTime(2026, 11, 10)));
        Assert.Equal(new DateTime(2026, 10, 1), Periods.Start(Recurrence.Trimestriel, new DateTime(2026, 11, 10)));
    }

    [Fact]
    public void Consumption_is_next_period_first_reading_minus_first_reading()
    {
        var rs = new[] { R("2026-01-05", 100), R("2026-01-26", 120), R("2026-02-02", 130), R("2026-02-23", 150), R("2026-03-02", 170) };
        Assert.Equal(30, ConsumptionService.Consumption(rs, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
        Assert.Equal(40, ConsumptionService.Consumption(rs, new DateTime(2026, 2, 1), new DateTime(2026, 3, 1)));
        Assert.Null(ConsumptionService.Consumption(rs, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1))); // un seul relevé
        Assert.Null(ConsumptionService.Consumption(rs, new DateTime(2025, 12, 1), new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void Weekly_view_columns_and_slower_lines()
    {
        var cols = Periods.Columns(Recurrence.Hebdo, new DateTime(2026, 1, 20));
        Assert.Equal(new[] { 5, 12, 19, 26 }, cols.Select(c => c.Start.Day));
        Assert.Single(Periods.Columns(Recurrence.Mensuel, new DateTime(2026, 11, 10)));
        // mensuelle dans une vue hebdo : première semaine du mois seulement
        Assert.True(Periods.IsActive(Recurrence.Hebdo, Recurrence.Mensuel, new DateTime(2026, 1, 5)));
        Assert.False(Periods.IsActive(Recurrence.Hebdo, Recurrence.Mensuel, new DateTime(2026, 1, 12)));
        // trimestrielle : première semaine de janvier/avril/juillet/octobre
        Assert.True(Periods.IsActive(Recurrence.Hebdo, Recurrence.Trimestriel, new DateTime(2026, 4, 6)));
        Assert.False(Periods.IsActive(Recurrence.Hebdo, Recurrence.Trimestriel, new DateTime(2026, 5, 4)));
        Assert.True(Periods.IsActive(Recurrence.Hebdo, Recurrence.Hebdo, new DateTime(2026, 5, 4)));
        // vue mensuelle, ligne annuelle : seulement en janvier
        Assert.True(Periods.IsActive(Recurrence.Mensuel, Recurrence.Annuel, new DateTime(2026, 1, 1)));
        Assert.False(Periods.IsActive(Recurrence.Mensuel, Recurrence.Annuel, new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void Stats_per_view_cover_meters_and_tasks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using var db = f.Create();
            var view = new ReadingView { Nom = "V", Recurrence = Recurrence.Mensuel };
            var cum = new Meter { Nom = "Eau", Unite = "m³" };
            var mes = new Meter { Nom = "Temp", Cumulatif = false };
            db.ReadingViews.Add(view); db.Meters.AddRange(cum, mes); db.SaveChanges();
            db.ViewLines.AddRange(
                new ViewLine { ReadingViewId = view.Id, MeterId = cum.Id, Recurrence = Recurrence.Mensuel, CreatedOn = new DateTime(2026, 1, 1) },
                new ViewLine { ReadingViewId = view.Id, MeterId = mes.Id, Recurrence = Recurrence.Mensuel, CreatedOn = new DateTime(2026, 1, 1) },
                new ViewLine { ReadingViewId = view.Id, Kind = LineKind.Tache, Libelle = "Purge", Recurrence = Recurrence.Mensuel, CreatedOn = new DateTime(2026, 1, 1) });
            foreach (var (d, v) in new[] { ("2026-01-05", 100m), ("2026-02-02", 150m), ("2026-03-02", 170m), ("2026-04-01", 200m) })
                db.MeterReadings.Add(new MeterReading { MeterId = cum.Id, Date = DateTime.Parse(d), Value = v });
            db.MeterReadings.Add(new MeterReading { MeterId = mes.Id, Date = new DateTime(2026, 2, 2), Value = 10 });
            db.MeterReadings.Add(new MeterReading { MeterId = mes.Id, Date = new DateTime(2026, 3, 2), Value = 20 });
            db.SaveChanges();
            var line = db.ViewLines.First(l => l.Kind == LineKind.Tache);
            db.TaskCompletions.Add(new TaskCompletion { ViewLineId = line.Id, PeriodStart = new DateTime(2026, 1, 1), CompletedAt = DateTime.Now, CompletedBy = "t" });
            db.SaveChanges();

            var rows = ConsumptionService.Stats(db, view.Id, 2026, new DateTime(2026, 4, 15));
            Assert.Equal(3, rows.Count);
            Assert.Equal(100m, rows[0][6]);          // 200 - 100 de janvier à avril
            Assert.Equal(4, rows[0][3]);
            Assert.Equal(10m, rows[1][11]); Assert.Equal(20m, rows[1][12]); Assert.Equal(15m, rows[1][13]);
            Assert.Equal("1/3 (33 %)", rows[2][15]); // janv., févr., mars échus
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Period_start_month_shifts_quarter_semester_year()
    {
        // trimestre commençant en février : fév-avr, mai-juil, août-oct, nov-janv
        Assert.Equal(new DateTime(2026, 11, 1), Periods.Start(Recurrence.Trimestriel, new DateTime(2027, 1, 15), 2));
        Assert.Equal(new DateTime(2026, 8, 1), Periods.Start(Recurrence.Trimestriel, new DateTime(2026, 10, 31), 2));
        Assert.Equal(new DateTime(2026, 4, 1), Periods.Start(Recurrence.Annuel, new DateTime(2026, 11, 23), 4));
        Assert.Equal(new DateTime(2026, 4, 1), Periods.Start(Recurrence.Annuel, new DateTime(2027, 3, 31), 4));
        Assert.Equal(new DateTime(2026, 9, 1), Periods.Start(Recurrence.Semestriel, new DateTime(2026, 11, 23), 3));
        Assert.Equal(new DateTime(2026, 10, 1), Periods.Start(Recurrence.Trimestriel, new DateTime(2026, 11, 23))); // défaut : calendaire
    }

    [Fact]
    public void Weekly_reading_is_late_from_date_plus_one_day()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using var db = f.Create();
            var meter = new Meter { Nom = "Eau" };
            var view = new ReadingView { Nom = "V" };
            db.Meters.Add(meter); db.ReadingViews.Add(view); db.SaveChanges();
            db.ViewLines.Add(new ViewLine { ReadingViewId = view.Id, MeterId = meter.Id, Recurrence = Recurrence.Hebdo, CreatedOn = new DateTime(2026, 1, 1) });
            db.MeterReadings.Add(new MeterReading { MeterId = meter.Id, Date = new DateTime(2026, 11, 2), Value = 10 }); // semaine précédente faite
            db.SaveChanges();
            var monday = NotificationService.Build(db, new DateTime(2026, 11, 9));
            Assert.Contains(monday, x => x.Level == NotificationLevel.Pending);
            Assert.DoesNotContain(monday, x => x.Level == NotificationLevel.Late);
            Assert.Contains(NotificationService.Build(db, new DateTime(2026, 11, 10)), x => x.Level == NotificationLevel.Late);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Database_is_created_and_notifications_work()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using var db = f.Create();
            var meter = new Meter { Nom = "Eau" };
            var view = new ReadingView { Nom = "Bâtiment A" };
            db.Meters.Add(meter); db.ReadingViews.Add(view); db.SaveChanges();
            db.ViewLines.Add(new ViewLine { ReadingViewId = view.Id, MeterId = meter.Id, Recurrence = Recurrence.Mensuel, CreatedOn = new DateTime(2026, 1, 1) });
            db.ViewLines.Add(new ViewLine { ReadingViewId = view.Id, Kind = LineKind.Tache, Libelle = "Purge", Recurrence = Recurrence.Annuel, CreatedOn = new DateTime(2025, 1, 1) });
            db.MeterReadings.Add(new MeterReading { MeterId = meter.Id, Date = new DateTime(2026, 10, 15), Value = 10 });
            db.SaveChanges();

            var n = NotificationService.Build(db, new DateTime(2026, 11, 10));
            Assert.Contains(n, x => x.Level == NotificationLevel.Pending && (x.Title + x.Detail).Contains("Eau"));
            Assert.Contains(n, x => x.Level == NotificationLevel.Late && (x.Title + x.Detail).Contains("Purge"));
            Assert.DoesNotContain(n, x => x.Level == NotificationLevel.Late && (x.Title + x.Detail).Contains("Eau"));
            // une seule notification par vue, récurrence et période
            Assert.Equal(n.Count(x => x.Level == NotificationLevel.Late).ToString(), n.Where(x => x.Level == NotificationLevel.Late).Select(x => (x.ViewId, x.Detail)).Distinct().Count().ToString());
            Assert.Equal(1, db.Sites.Count());
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Catalog_defaults_rename_and_delete_update_meters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using var db = f.Create();
            db.Meters.Add(new Meter { Nom = "Gaz", Unite = "Nm3", Famille = "Gaz" });
            db.SaveChanges();
            CatalogService.EnsureDefaults(db);
            Assert.Contains("kWh", CatalogService.Names(db, CatalogKind.Unite));
            Assert.Contains("Nm3", CatalogService.Names(db, CatalogKind.Unite)); // unité déjà saisie sur un compteur
            Assert.Contains("Chauffage", CatalogService.Names(db, CatalogKind.Famille));
            Assert.False(CatalogService.Add(db, CatalogKind.Unite, "KWH")); // doublon, casse ignorée

            Assert.True(CatalogService.Rename(db, CatalogKind.Unite, "Nm3", "m³ normaux"));
            Assert.Equal("m³ normaux", db.Meters.Single().Unite);
            CatalogService.Delete(db, CatalogKind.Famille, "Gaz");
            Assert.Equal("", db.Meters.Single().Famille);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Old_v2_database_is_migrated_to_catalogs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using (var db = f.Create())
            {
                db.Database.ExecuteSqlRaw("DROP TABLE CatalogItems");
                db.Database.ExecuteSqlRaw("ALTER TABLE Meters DROP COLUMN Famille");
                db.Database.ExecuteSqlRaw("PRAGMA user_version = 2");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            new DbContextFactory(path).EnsureDatabaseUpToDate("test");
            using var db2 = new DbContextFactory(path).Create();
            db2.Meters.Add(new Meter { Nom = "X", Famille = "Eau" }); db2.SaveChanges();
            CatalogService.EnsureDefaults(db2);
            Assert.Contains("Eau", CatalogService.Names(db2, CatalogKind.Famille));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Stopped_meter_raises_no_notification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relevpro-test-{Guid.NewGuid():N}.db");
        try
        {
            var f = new DbContextFactory(path);
            f.EnsureDatabaseUpToDate("test");
            using var db = f.Create();
            var meter = new Meter { Nom = "Ancien", Arrete = true };
            var view = new ReadingView { Nom = "V" };
            db.Meters.Add(meter); db.ReadingViews.Add(view); db.SaveChanges();
            db.ViewLines.Add(new ViewLine { ReadingViewId = view.Id, MeterId = meter.Id, Recurrence = Recurrence.Hebdo, CreatedOn = new DateTime(2026, 1, 1) });
            db.SaveChanges();
            Assert.Empty(NotificationService.Build(db, new DateTime(2026, 11, 10)));
            meter.Arrete = false; db.SaveChanges();
            Assert.Contains(NotificationService.Build(db, new DateTime(2026, 11, 10)), x => x.Level == NotificationLevel.Late);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Rollover_is_summed_with_capacity_and_other_drops_count_zero()
    {
        var rs = new[] { R("2026-01-05", 99000), R("2026-01-26", 99900), R("2026-02-02", 100), R("2026-03-02", 300) };
        rs[2].Tour = true;
        var cap = ConsumptionService.Capacity(null, rs); // 99 900 -> 100 000
        Assert.Equal(100000, cap);
        Assert.Equal(1100, ConsumptionService.Consumption(rs, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), cap)); // 900 + (100 000 - 99 900 + 100)
        Assert.Equal(200, ConsumptionService.Consumption(rs, new DateTime(2026, 2, 1), new DateTime(2026, 3, 1), cap));
        rs[2].Tour = false; // changement de compteur : pas de consommation négative
        Assert.Equal(0, ConsumptionService.Consumption(rs, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), cap)); // baisse non qualifiée : jamais négative
        rs[2].Remplacement = true; // compteur changé : le nouveau compteur part de 0
        Assert.Equal(1000, ConsumptionService.Consumption(rs, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), cap)); // 900 + 100
        var spike = new[] { R("2026-01-05", 1000), R("2026-01-12", 9), R("2026-01-19", 1100), R("2026-02-02", 1200) };
        Assert.Equal(200, ConsumptionService.Consumption(spike, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1))); // valeur isolée aberrante ignorée
    }

    [Fact]
    public void Confirmed_rollover_is_not_flagged_by_validator()
    {
        var hist = new[] { R("2026-01-05", 99000), R("2026-01-12", 99400), R("2026-01-19", 99800) };
        Assert.NotEmpty(ReadingValidator.Check(M, new DateTime(2026, 1, 26), 150, hist));
        Assert.Empty(ReadingValidator.Check(new Meter { Nom = "X", SeuilEcartPct = 200 }, new DateTime(2026, 1, 26), 150, hist, 100000));
    }
}
