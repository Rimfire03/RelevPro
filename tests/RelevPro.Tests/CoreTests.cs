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
    public void Consumption_uses_previous_period_index()
    {
        var rs = new[] { R("2026-01-31", 100), R("2026-02-28", 130), R("2026-03-31", 170) };
        Assert.Equal(30, ConsumptionService.Consumption(rs, new DateTime(2026, 2, 1), new DateTime(2026, 3, 1)));
        Assert.Equal(40, ConsumptionService.Consumption(rs, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1)));
        Assert.Null(ConsumptionService.Consumption(rs, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
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
            Assert.Contains(n, x => x.Level == NotificationLevel.Pending && x.Title.Contains("Eau"));
            Assert.Contains(n, x => x.Level == NotificationLevel.Late && x.Title.Contains("Purge"));
            Assert.DoesNotContain(n, x => x.Level == NotificationLevel.Late && x.Title.Contains("Eau"));
            Assert.Equal(1, db.Sites.Count());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
