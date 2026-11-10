using ClosedXML.Excel;
using RelevPro.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

/// <summary>Export Excel d'une année : une feuille par vue de relevé (consommations par mois, puis par an) et une feuille
/// « Relevés » listant toutes les saisies de l'année du site courant. Déposé dans <see cref="AppSettings.ExportPath"/>.</summary>
public class ExcelExportService
{
    private static readonly string[] MonthShortNames =
        { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E5E7EB");
    private static readonly XLColor ForcedFill = XLColor.FromHtml("#FFC7CE");

    public string ExportYear(RelevDbContext ctx, string exportFolder, int year, string? siteName = null)
    {
        Directory.CreateDirectory(exportFolder);
        using var workbook = new XLWorkbook();

        foreach (var view in ctx.ReadingViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
        {
            var ws = workbook.Worksheets.Add(SheetName(view.Nom, workbook));
            var row = 1;
            ws.Cell(row++, 1).Value = $"{view.Nom} - consommations {year} par mois";
            ws.Cell(row - 1, 1).Style.Font.Bold = true;
            var headers = new List<string> { "Compteur", "Unité" };
            headers.AddRange(MonthShortNames);
            headers.Add("Total");
            WriteHeader(ws, row++, headers);
            foreach (var r in ConsumptionService.Monthly(ctx, view.Id, year))
            {
                ws.Cell(row, 1).Value = r.Compteur;
                ws.Cell(row, 2).Value = r.Unite;
                for (var m = 0; m < 12; m++) SetNumber(ws.Cell(row, 3 + m), r.Values[m]);
                SetNumber(ws.Cell(row, 15), r.Total);
                row++;
            }

            row += 2;
            var years = ConsumptionService.YearsWithData(ctx, view.Id);
            ws.Cell(row++, 1).Value = $"{view.Nom} - consommations par an";
            ws.Cell(row - 1, 1).Style.Font.Bold = true;
            var yh = new List<string> { "Compteur", "Unité" };
            yh.AddRange(years.Select(y => y.ToString()));
            WriteHeader(ws, row++, yh);
            foreach (var r in ConsumptionService.Yearly(ctx, view.Id, years))
            {
                ws.Cell(row, 1).Value = r.Compteur;
                ws.Cell(row, 2).Value = r.Unite;
                for (var i = 0; i < years.Count; i++) SetNumber(ws.Cell(row, 3 + i), r.Values[i]);
                row++;
            }
            ws.Columns().AdjustToContents();
        }

        var readings = workbook.Worksheets.Add("Relevés");
        WriteHeader(readings, 1, new List<string> { "Date", "Compteur", "Valeur", "Unité", "Forcé", "Anomalie", "Saisi par" });
        var line = 2;
        var rows = ctx.MeterReadings.AsNoTracking().Include(r => r.Meter)
            .Where(r => r.Date.Year == year).OrderBy(r => r.Date).ThenBy(r => r.MeterId).ToList();
        foreach (var r in rows)
        {
            readings.Cell(line, 1).Value = r.Date;
            readings.Cell(line, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            readings.Cell(line, 2).Value = r.Meter?.Nom ?? "";
            readings.Cell(line, 3).Value = r.Value;
            readings.Cell(line, 4).Value = r.Meter?.Unite ?? "";
            readings.Cell(line, 5).Value = r.Forced ? "Oui" : "";
            readings.Cell(line, 6).Value = r.Anomaly;
            readings.Cell(line, 7).Value = r.EnteredBy;
            if (r.Forced) readings.Range(line, 1, line, 7).Style.Fill.BackgroundColor = ForcedFill;
            line++;
        }
        readings.Columns().AdjustToContents();

        var now = DateTime.Now;
        var sitePart = string.IsNullOrWhiteSpace(siteName) ? "" : " - " + string.Concat(siteName.Split(Path.GetInvalidFileNameChars())).Trim();
        var fullPath = Path.Combine(exportFolder, $"Releves {year}{sitePart} du {now:dd.MM.yyyy} a {now.Hour}.{now.Minute}.xlsx");
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    private static string SheetName(string name, XLWorkbook wb)
    {
        var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        if (clean.Length == 0) clean = "Vue";
        if (clean.Length > 28) clean = clean[..28];
        var result = clean;
        for (var i = 2; wb.Worksheets.Any(w => w.Name.Equals(result, StringComparison.OrdinalIgnoreCase)); i++) result = $"{clean} {i}";
        return result;
    }

    private static void WriteHeader(IXLWorksheet ws, int row, List<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) ws.Cell(row, i + 1).Value = headers[i];
        var range = ws.Range(row, 1, row, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void SetNumber(IXLCell cell, decimal? v)
    {
        if (v is null) return;
        cell.Value = v.Value;
        cell.Style.NumberFormat.Format = "#,##0.##";
    }
}
