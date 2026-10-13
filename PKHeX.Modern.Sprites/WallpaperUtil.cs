using System;
using PKHeX.Core;
using static PKHeX.Core.GameVersion;

namespace PKHeX.Modern.Sprites;

/// <summary>Porte do <c>PKHeX.Drawing.Misc.WallpaperUtil</c>: papel de parede de cada caixa, pelo jogo e pelo indice.</summary>
public static class WallpaperUtil
{
    private const string DefaultWallpaper = "box_wp16xy";

    public static SpriteImage GetWallpaper(SaveFile sav, int box)
    {
        if (sav is SAV3Colosseum or SAV3XD) // um fixo para cada caixa
            return GetWallpaperImage(sav.Version, box);
        if (sav is SAV4BR) // um fixo para cada caixa
            return GetWallpaperImage(DP, box % 16);
        if (sav is not IBoxDetailWallpaper wp)
            return SpriteResources.Required(DefaultWallpaper);
        if (sav is SAV9ZA) // cidade (Lumiose)
            return SpriteResources.Required("box_wp02bdsp");
        if (sav is SAV8LA) // pasto
            return SpriteResources.Required("box_wp01bdsp");

        int wallpaper = wp.GetBoxWallpaper(box);
        var image = GetWallpaperImage(sav.Version, wallpaper);
        if (sav is SAV3RSBox rsBox && wallpaper == 20) // My Wallpaper (desenho do jogador no Pokemon Box)
            image = DrawMyWallpaper(rsBox, box, image);
        return image;
    }

    private static SpriteImage GetWallpaperImage(GameVersion version, int index)
        => SpriteResources.Get(GetWallpaperResourceName(version, index)) ?? SpriteResources.Required(DefaultWallpaper);

    public static string GetWallpaperResourceName(GameVersion version, int index)
    {
        index++; // os arquivos comecam em 1
        var suffix = GetResourceSuffix(version, index);
        var variant = version switch
        {
            SL when index is 20 => "_n", // Naranja
            VL when index is 20 => "_u", // Uva
            _ => string.Empty,
        };
        return $"box_wp{index:00}{suffix}{variant}";
    }

    private static string GetResourceSuffix(GameVersion version, int index) => version.Context switch
    {
        EntityContext.Gen3 when version == E => "e",
        EntityContext.Gen3 when FRLG.Contains(version) && index > 12 => "frlg",
        EntityContext.Gen3 => "rs",
        _ when version == COLO => "colo",
        _ when version == XD => "xd",

        EntityContext.Gen4 when DP.Contains(version) || index <= 16 => "dp",
        EntityContext.Gen4 when version == Pt => "pt",
        EntityContext.Gen4 when HGSS.Contains(version) => "hgss",

        EntityContext.Gen5 => B2W2.Contains(version) && index > 16 ? "b2w2" : "bw",
        EntityContext.Gen6 => ORAS.Contains(version) && index > 16 ? "ao" : "xy",
        EntityContext.Gen7 => "xy", // parecido; o PKHeX usa o do X/Y
        EntityContext.Gen8b => "bdsp",
        EntityContext.Gen8 => "swsh",
        EntityContext.Gen9 => "sv",
        _ => string.Empty,
    };

    /// <summary>
    /// "My Wallpaper" do Pokemon Box: a imagem do jogador (568x216, CMPR) tem uma metade para cada caixa, reduzida para
    /// o quadro do papel de parede (5,5 146x106). O PKHeX desenha com o GDI+; aqui a reducao e pela media da area.
    /// </summary>
    private static SpriteImage DrawMyWallpaper(SAV3RSBox sav, int box, SpriteImage wallpaper)
    {
        const int width = SAV3RSBox.WP_WIDTH, height = SAV3RSBox.WP_HEIGHT;
        const int dx = 5, dy = 5, dw = 146, dh = 106;
        var data = CMPR.Decompress(sav.MyWallpaper, width, height);
        var result = wallpaper.Clone();
        int sx0 = width / 2 * (box % 2), sw = width / 2;
        for (int y = 0; y < dh; y++)
        {
            int ys = y * height / dh, ye = Math.Max(ys + 1, (y + 1) * height / dh);
            for (int x = 0; x < dw; x++)
            {
                int xs = sx0 + (x * sw / dw), xe = Math.Max(xs + 1, sx0 + ((x + 1) * sw / dw));
                long b = 0, g = 0, r = 0, a = 0; int n = 0;
                for (int yy = ys; yy < ye; yy++)
                    for (int xx = xs; xx < xe; xx++)
                    {
                        int i = ((yy * width) + xx) * 4;
                        b += data[i]; g += data[i + 1]; r += data[i + 2]; a += data[i + 3]; n++;
                    }
                var over = new SpriteImage(1, 1, [(byte)(b / n), (byte)(g / n), (byte)(r / n), (byte)(a / n)]);
                result.DrawOver(over, dx + x, dy + y);
            }
        }
        return result;
    }
}
