using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Icone vetorial de traco (grade de 24×24) na cor do texto em volta, para a barra lateral e o Inicio. Substitui os
/// emojis, que no Android tem alturas e larguras diferentes e desalinhavam a lista. <see cref="Kind"/> aceita o
/// proprio emoji da pagina (<c>PageViewModel.Icon</c>), entao os ViewModels nao mudam; sem desenho, mostra o texto.
/// </summary>
public sealed class LineIcon : Avalonia.Controls.Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<LineIcon, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LineIcon>();
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeWidth), 2);

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    /// <summary>Espessura do traco na grade de 24 (escala junto com o icone).</summary>
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    static LineIcon() => AffectsRender<LineIcon>(KindProperty, ForegroundProperty, StrokeWidthProperty);

    /// <summary>Desenhos proprios (traco simples, cantos arredondados), pelo emoji que cada pagina ja usa.</summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["⌂"] = "M4 11L12 4L20 11 M6 9.5V20H18V9.5 M10 20V15H14V20", // Inicio
        ["▦"] = "M4 4H10V10H4Z M14 4H20V10H14Z M4 14H10V20H4Z M14 14H20V20H14Z", // Caixas
        ["◉"] = "M3 12A9 9 0 1 0 21 12A9 9 0 1 0 3 12Z M3 12H9 M15 12H21 M9 12A3 3 0 1 0 15 12A3 3 0 1 0 9 12Z", // Equipe (Pokebola)
        ["🏦"] = "M3 9L12 4L21 9 M5 11V18 M10 11V18 M14 11V18 M19 11V18 M3 20.5H21", // Bank
        ["📖"] = "M5 5A2 2 0 0 1 7 3H18V17H7A2 2 0 0 0 5 19Z M5 19A2 2 0 0 0 7 21H18V17 M9 7.5H14", // Pokedex
        ["♟"] = "M8 8A4 4 0 1 0 16 8A4 4 0 1 0 8 8Z M4 21A8 8 0 0 1 20 21", // Treinador
        ["🎒"] = "M6 8H18L19 20.5H5Z M9 8V6.5A3 3 0 0 1 15 6.5V8 M9 13H15", // Mochila
        ["🌿"] = "M5 19C5 10 10 5 20 4C19 14 14 19 5 19Z M5 19L12 12", // Encontros
        ["🎁"] = "M4 8.5H20V12.5H4Z M5.5 12.5H18.5V20.5H5.5Z M12 8.5V20.5 M12 8.5C10.5 4.5 7 4.5 7 6.8C7 8.2 9 8.5 12 8.5Z M12 8.5C13.5 4.5 17 4.5 17 6.8C17 8.2 15 8.5 12 8.5Z", // Presentes
        ["🗂"] = "M3 6.5A1.5 1.5 0 0 1 4.5 5H9L11 7H19.5A1.5 1.5 0 0 1 21 8.5V17.5A1.5 1.5 0 0 1 19.5 19H4.5A1.5 1.5 0 0 1 3 17.5Z M3 10H21", // Save Manager
        ["🎮"] = "M7 8H17A4 4 0 0 1 21 12V13.5A3.5 3.5 0 0 1 14.8 15.8L14 15H10L9.2 15.8A3.5 3.5 0 0 1 3 13.5V12A4 4 0 0 1 7 8Z M7.5 10.5V13.5 M6 12H9 M15.5 11V11.01 M17.5 13V13.01", // Jogo
        ["🔎"] = "M4 10.5A6.5 6.5 0 1 0 17 10.5A6.5 6.5 0 1 0 4 10.5Z M15.3 15.3L20.5 20.5", // Pesquisa
        ["⚙"] = "M4 7H11 M15 7H20 M13 5V9 M4 17H7 M11 17H20 M9 15V19 M4 12H15 M19 12H20 M17 10V14", // Edicao em lote
        ["❔"] = "M3 12A9 9 0 1 0 21 12A9 9 0 1 0 3 12Z M9.5 9.5A2.5 2.5 0 1 1 13 11.8C12.4 12.1 12 12.6 12 13.3V14 M12 17V17.01", // Ajuda
        ["💾"] = "M5 4H16L20 8V19A1 1 0 0 1 19 20H5A1 1 0 0 1 4 19V5A1 1 0 0 1 5 4Z M8 4V9H15V4 M8 20V14H16V20", // Salvar
    };
    private static readonly Dictionary<string, Geometry> Cache = [];

    /// <summary>Tem desenho para este emoji (os testes conferem que toda pagina tem um).</summary>
    public static bool HasPath(string? kind) => kind is not null && Paths.ContainsKey(kind);

    private static Geometry? GetGeometry(string? kind)
    {
        if (kind is null || !Paths.TryGetValue(kind, out var data))
            return null;
        if (!Cache.TryGetValue(kind, out var geometry))
            Cache[kind] = geometry = Geometry.Parse(data);
        return geometry;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Width is > 0 ? Width : 20, Height is > 0 ? Height : 20);

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? Brushes.Gray;
        double size = System.Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;
        if (GetGeometry(Kind) is not { } geometry)
        {
            if (string.IsNullOrEmpty(Kind))
                return;
            var text = new FormattedText(Kind, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size * 0.75, brush);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
            return;
        }
        double scale = size / 24;
        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
