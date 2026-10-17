using System;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Curva da chance de já ter achado o shiny (1 − (1 − p)^n) até 99%, com as marcas de 50/90/99% e um ponto nos
/// encontros atuais. Cores do tema: a linha usa a cor de destaque e o texto a cor herdada.
/// </summary>
public sealed class OddsCurve : Avalonia.Controls.Control
{
    public static readonly StyledProperty<double> ProbabilityProperty = AvaloniaProperty.Register<OddsCurve, double>(nameof(Probability));
    public static readonly StyledProperty<long> EncountersProperty = AvaloniaProperty.Register<OddsCurve, long>(nameof(Encounters));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<OddsCurve, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<OddsCurve>();

    public double Probability { get => GetValue(ProbabilityProperty); set => SetValue(ProbabilityProperty, value); }
    public long Encounters { get => GetValue(EncountersProperty); set => SetValue(EncountersProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    static OddsCurve() => AffectsRender<OddsCurve>(ProbabilityProperty, EncountersProperty, StrokeProperty, ForegroundProperty);

    public override void Render(DrawingContext context)
    {
        var p = Probability;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (p <= 0 || w < 40 || h < 30)
            return;
        var text = Foreground ?? Brushes.Gray;
        var stroke = Stroke ?? Brushes.DodgerBlue;
        double maxN = Math.Max(ShinyOdds.EncountersFor(p, 0.99), Encounters * 1.1);
        const double padBottom = 16;
        double plotH = h - padBottom;
        Point At(double n) => new(n / maxN * w, plotH - ShinyOdds.ChanceBy(p, (long)n) * (plotH - 4));

        var grid = new Pen(text, 0.5) { DashStyle = DashStyle.Dash };
        var faint = new SolidColorBrush(Colors.Gray, 0.6);
        foreach (var target in new[] { 0.5, 0.9, 0.99 })
        {
            double n = ShinyOdds.EncountersFor(p, target);
            double x = n / maxN * w;
            context.DrawLine(grid, new Point(x, 0), new Point(x, plotH));
            var label = new FormattedText($"{target:P0}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, faint);
            context.DrawText(label, new Point(Math.Min(x + 2, w - label.Width), plotH + 2));
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(0), false);
            for (int i = 1; i <= 80; i++)
                g.LineTo(At(maxN * i / 80));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(stroke, 2), geometry);
        context.DrawLine(new Pen(text, 1), new Point(0, plotH), new Point(w, plotH));

        if (Encounters > 0)
        {
            var dot = At(Math.Min(Encounters, maxN));
            context.DrawEllipse(stroke, new Pen(text, 1.5), dot, 5, 5);
        }
    }
}
