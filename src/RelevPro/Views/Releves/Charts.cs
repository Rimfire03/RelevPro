using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RelevPro.Views.Releves;

/// <summary>Base des graphiques dessinés directement (pas de bibliothèque) : couleurs du thème, textes, redessin au besoin.</summary>
public abstract class ChartBase : FrameworkElement
{
    protected static Brush Res(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        return (Brush)new BrushConverter().ConvertFromString(fallback)!;
    }

    protected Brush Accent => Res("BrushPrimary", "#2563EB");
    protected static readonly Brush Grid = Freeze("#E2E8F0");
    protected static readonly Brush Muted = Freeze("#94A3B8");
    protected static readonly Brush Soft = Freeze("#CBD5E1");
    protected static readonly Brush Warm = Freeze("#F59E0B");
    protected static readonly Brush Text = Freeze("#475569");

    protected static Brush Freeze(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    protected FormattedText Txt(string s, double size, Brush brush, FontWeight? w = null) =>
        new(s, CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, w ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static string Compact(double v) =>
        Math.Abs(v) >= 1_000_000 ? (v / 1_000_000).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " M"
        : Math.Abs(v) >= 10_000 ? (v / 1000).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " k"
        : v.ToString("#,0.##", CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>Graduation « ronde » : plus petit multiple de 1, 2, 2,5, 5 × 10^n couvrant la valeur maximale.</summary>
    protected static (double Max, double Step) Nice(double max, int ticks = 4)
    {
        if (max <= 0) return (1, 0.25);
        var raw = max / ticks; var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(f => f * mag).First(x => x >= raw);
        return (Math.Ceiling(max / step) * step, step);
    }
}

/// <summary>Barres groupées : consommation de chaque mois, année choisie (couleur) et année précédente (gris), moyenne en pointillés.</summary>
public sealed class BarChart : ChartBase
{
    public static readonly DependencyProperty MonthsProperty = DependencyProperty.Register(nameof(Months), typeof(object), typeof(BarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PrevMonthsProperty = DependencyProperty.Register(nameof(PrevMonths), typeof(object), typeof(BarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty YearProperty = DependencyProperty.Register(nameof(Year), typeof(int), typeof(BarChart), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public object? Months { get => GetValue(MonthsProperty); set => SetValue(MonthsProperty, value); } // object : le compilateur XAML refuse une propriété tableau liée dans un modèle
    public object? PrevMonths { get => GetValue(PrevMonthsProperty); set => SetValue(PrevMonthsProperty, value); }
    public int Year { get => (int)GetValue(YearProperty); set => SetValue(YearProperty, value); }

    private static readonly string[] Letters = { "J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D" };

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w < 60 || h < 60) return;
        var cur = Months as decimal?[] ?? new decimal?[12]; var prev = PrevMonths as decimal?[] ?? new decimal?[12];
        var vals = cur.Concat(prev).Where(v => v is not null).Select(v => (double)v!.Value).ToList();
        var (max, step) = Nice(vals.Count == 0 ? 0 : vals.Max());
        double left = 40, right = 6, top = 20, bottom = 20;
        double pw = w - left - right, ph = h - top - bottom;
        double Y(double v) => top + ph - v / max * ph;

        var pen = new Pen(Grid, 1); pen.Freeze();
        for (double g = 0; g <= max + step / 2; g += step)
        {
            dc.DrawLine(pen, new Point(left, Y(g)), new Point(w - right, Y(g)));
            var t = Txt(Compact(g), 10, Muted); dc.DrawText(t, new Point(left - 6 - t.Width, Y(g) - t.Height / 2));
        }
        var hasPrev = prev.Any(v => v is > 0);
        var slot = pw / 12; var bw = Math.Max(3, slot * (hasPrev ? 0.34 : 0.56));
        for (var i = 0; i < 12; i++)
        {
            var cx = left + slot * (i + 0.5);
            void Bar(decimal? v, Brush b, double x)
            {
                if (v is not { } d || d <= 0) return;
                var y = Y((double)d); var r = new Rect(x, y, bw, top + ph - y);
                dc.DrawRoundedRectangle(b, null, r, 2, 2);
                dc.DrawRectangle(b, null, new Rect(x, Math.Max(y + 2, top + ph - 2), bw, 2)); // pied droit
            }
            if (hasPrev) { Bar(prev[i], Soft, cx - bw - 1); Bar(cur[i], Accent, cx + 1); }
            else Bar(cur[i], Accent, cx - bw / 2);
            var l = Txt(Letters[i], 10, Muted); dc.DrawText(l, new Point(cx - l.Width / 2, h - bottom + 4));
        }
        var known = cur.Where(v => v is > 0).Select(v => (double)v!.Value).ToList();
        if (known.Count > 1)
        {
            var avg = known.Average();
            var dash = new Pen(Warm, 1.4) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) }; dash.Freeze();
            dc.DrawLine(dash, new Point(left, Y(avg)), new Point(w - right, Y(avg)));
        }
        // légende
        double lx = left;
        void Key(Brush b, string s) { dc.DrawRoundedRectangle(b, null, new Rect(lx, 6, 9, 9), 2, 2); var t = Txt(s, 10.5, Text); dc.DrawText(t, new Point(lx + 13, 3.5)); lx += 13 + t.Width + 12; }
        Key(Accent, Year.ToString()); if (hasPrev) Key(Soft, (Year - 1).ToString());
        dc.DrawLine(new Pen(Warm, 2), new Point(lx, 10.5), new Point(lx + 12, 10.5)); var m = Txt("moyenne", 10.5, Text); dc.DrawText(m, new Point(lx + 16, 3.5));
    }
}

/// <summary>Courbe des relevés d'une mesure sur l'année, avec zone sous la courbe, minimum et maximum repérés.</summary>
public sealed class LineChart : ChartBase
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(object), typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty YearProperty = DependencyProperty.Register(nameof(Year), typeof(int), typeof(LineChart), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public object? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public int Year { get => (int)GetValue(YearProperty); set => SetValue(YearProperty, value); }

    private static readonly string[] Letters = { "J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D" };

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w < 60 || h < 60) return;
        double left = 40, right = 10, top = 14, bottom = 20;
        double pw = w - left - right, ph = h - top - bottom;
        var pts = (Points as List<(DateTime Date, decimal Value)> ?? new()).OrderBy(p => p.Date).ToList();
        var pen = new Pen(Grid, 1); pen.Freeze();
        if (pts.Count == 0)
        {
            var t0 = Txt("Aucun relevé cette année", 12, Muted); dc.DrawText(t0, new Point((w - t0.Width) / 2, (h - t0.Height) / 2));
            return;
        }
        var min = (double)pts.Min(p => p.Value); var maxv = (double)pts.Max(p => p.Value);
        if (maxv - min < 1e-9) { min -= 1; maxv += 1; }
        var pad = (maxv - min) * 0.12; var lo = min - pad; var hi = maxv + pad;
        var (_, step) = Nice(hi - lo, 4);
        double Y(double v) => top + ph - (v - lo) / (hi - lo) * ph;
        var start = new DateTime(Year, 1, 1); var span = (new DateTime(Year + 1, 1, 1) - start).TotalDays;
        double X(DateTime d) => left + (d - start).TotalDays / span * pw;
        for (var g = Math.Ceiling(lo / step) * step; g <= hi; g += step)
        {
            dc.DrawLine(pen, new Point(left, Y(g)), new Point(w - right, Y(g)));
            var t = Txt(Compact(g), 10, Muted); dc.DrawText(t, new Point(left - 6 - t.Width, Y(g) - t.Height / 2));
        }
        for (var i = 0; i < 12; i++) { var x = X(new DateTime(Year, i + 1, 1)) + pw / 24; var l = Txt(Letters[i], 10, Muted); dc.DrawText(l, new Point(x - l.Width / 2, h - bottom + 4)); }

        var accent = Accent;
        var area = new StreamGeometry();
        using (var c = area.Open())
        {
            c.BeginFigure(new Point(X(pts[0].Date), top + ph), true, true);
            foreach (var p in pts) c.LineTo(new Point(X(p.Date), Y((double)p.Value)), false, false);
            c.LineTo(new Point(X(pts[^1].Date), top + ph), false, false);
        }
        area.Freeze();
        var fill = accent.Clone(); fill.Opacity = 0.12; fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(X(pts[0].Date), Y((double)pts[0].Value)), false, false);
            foreach (var p in pts.Skip(1)) c.LineTo(new Point(X(p.Date), Y((double)p.Value)), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, line);
        if (pts.Count <= 60) foreach (var p in pts) dc.DrawEllipse(Brushes.White, new Pen(accent, 1.6), new Point(X(p.Date), Y((double)p.Value)), 2.8, 2.8);

        void Tag(DateTime d, decimal v, Brush b, bool above)
        {
            var pt = new Point(X(d), Y((double)v));
            dc.DrawEllipse(b, new Pen(Brushes.White, 1.5), pt, 4.5, 4.5);
            var t = Txt(Compact((double)v), 10.5, b, FontWeights.SemiBold);
            var ty = above ? pt.Y - t.Height - 5 : pt.Y + 5;
            dc.DrawText(t, new Point(Math.Clamp(pt.X - t.Width / 2, left, w - right - t.Width), ty));
        }
        var mx = pts.MaxBy(p => p.Value); var mn = pts.MinBy(p => p.Value);
        Tag(mx.Date, mx.Value, Warm, true);
        if (mn.Value != mx.Value) Tag(mn.Date, mn.Value, Muted, false);
    }
}

/// <summary>Anneau de progression : part des périodes réalisées.</summary>
public sealed class RingChart : ChartBase
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(nameof(Ratio), typeof(double), typeof(RingChart), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EmptyProperty = DependencyProperty.Register(nameof(Empty), typeof(bool), typeof(RingChart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Ratio { get => (double)GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public bool Empty { get => (bool)GetValue(EmptyProperty); set => SetValue(EmptyProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight; var d = Math.Min(w, h);
        if (d < 40) return;
        var c = new Point(w / 2, h / 2); var th = Math.Max(8, d * 0.11); var r = d / 2 - th / 2 - 2;
        var track = new Pen(Grid, th); track.Freeze();
        dc.DrawEllipse(null, track, c, r, r);
        var ratio = Math.Clamp(Ratio, 0, 1);
        if (!Empty && ratio > 0)
        {
            var color = ratio >= 0.9 ? Res("BrushSuccess", "#16A34A") : ratio >= 0.6 ? Accent : Warm;
            var pen = new Pen(color, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (ratio >= 0.9999) dc.DrawEllipse(null, pen, c, r, r);
            else
            {
                var a = -Math.PI / 2; var e = a + ratio * 2 * Math.PI;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), false, false);
                    ctx.ArcTo(new Point(c.X + r * Math.Cos(e), c.Y + r * Math.Sin(e)), new Size(r, r), 0, ratio > 0.5, SweepDirection.Clockwise, true, false);
                }
                g.Freeze(); dc.DrawGeometry(null, pen, g);
            }
        }
        var txt = Txt(Empty ? "—" : $"{ratio * 100:0} %", d * 0.2, Text, FontWeights.SemiBold);
        dc.DrawText(txt, new Point(c.X - txt.Width / 2, c.Y - txt.Height / 2));
    }
}
