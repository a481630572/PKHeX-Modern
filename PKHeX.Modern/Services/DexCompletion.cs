using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>O que fazer na Pokedex inteira do save.</summary>
public enum DexAction
{
    /// <summary>Todas vistas e capturadas (formas, generos e idiomas onde o jogo guarda).</summary>
    Complete,
    /// <summary>Todas vistas, sem mudar as capturadas.</summary>
    SeenAll,
    /// <summary>Tira as capturadas (as vistas ficam).</summary>
    CaughtNone,
    /// <summary>Limpa tudo: nenhuma vista nem capturada.</summary>
    Clear,
}

/// <summary>
/// Completar ou limpar a Pokedex do save, como os botoes "Seen All / Caught All / Complete" dos editores de Pokedex do
/// PKHeX. Da Gen 4 em diante usa o <see cref="ZukanBase{T}"/> de cada jogo (formas, generos, idiomas e, se pedido,
/// shiny vistos); na Gen 1 a 3 marca especie por especie. Legends: Arceus guarda pesquisa por tarefas e fica de fora
/// (o PKHeX tambem nao tem "completar tudo" para ele).
/// </summary>
public static class DexCompletion
{
    public static bool IsSupported(SaveFile? sav) => sav is { HasPokeDex: true } and not SAV8LA
        && (sav.Generation <= 3 || GetKind(sav) is not null);

    /// <summary>Shiny vistos so existe na Pokedex da Gen 5 em diante.</summary>
    public static bool HasShinyOption(SaveFile? sav) => sav is not null && sav.Generation >= 5 && IsSupported(sav);

    public static (int Seen, int Caught) Count(SaveFile sav)
    {
        int seen = 0, caught = 0;
        for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
        {
            if (sav.GetSeen(s)) seen++;
            if (sav.GetCaught(s)) caught++;
        }
        return (seen, caught);
    }

    public static void Apply(SaveFile sav, DexAction action, bool shiny)
    {
        switch (sav)
        {
            case SAV4 s: Run(s.Dex, action, shiny); break;
            case SAV5 s: Run5(s.Zukan, action, shiny, (LanguageID)sav.Language); break;
            case SAV6XY s: Run6(s.Zukan, action, shiny, (LanguageID)sav.Language); break;
            case SAV6AO s: Run6(s.Zukan, action, shiny, (LanguageID)sav.Language); break;
            case SAV7 s: Run(s.Zukan, action, shiny); break;
            case SAV7b s: Run(s.Zukan, action, shiny); break;
            case SAV8SWSH s: Run(s.Zukan, action, shiny); break;
            case SAV8BS s: Run(s.Zukan, action, shiny); break;
            case SAV9SV s: Run(s.Zukan, action, shiny); break;
            case SAV9ZA s: Run(s.Zukan, action, shiny); break;
            default: RunSimple(sav, action); break;
        }
    }

    private static string? GetKind(SaveFile sav) => sav switch
    {
        SAV4 or SAV5 or SAV6XY or SAV6AO or SAV7 or SAV7b or SAV8SWSH or SAV8BS or SAV9SV or SAV9ZA => "zukan",
        _ => null,
    };

    private static void Run<T>(ZukanBase<T> dex, DexAction action, bool shiny) where T : SaveFile
    {
        switch (action)
        {
            case DexAction.Complete: dex.CompleteDex(shiny); break;
            case DexAction.SeenAll: dex.SeenAll(shiny); break;
            case DexAction.CaughtNone: dex.CaughtNone(); break;
            case DexAction.Clear: dex.CaughtNone(); dex.SeenNone(); break;
        }
    }

    // Gen 5 e 6: Pokedex com formas, generos, shiny e idiomas, mas fora do ZukanBase (mesmos nomes de metodo).
    private static void Run5(Zukan5 dex, DexAction action, bool shiny, LanguageID language)
    {
        switch (action)
        {
            case DexAction.Complete: dex.SeenAll(shiny); dex.CaughtAll(language, allLanguages: true); break;
            case DexAction.SeenAll: dex.SeenAll(shiny); break;
            case DexAction.CaughtNone: dex.CaughtNone(); break;
            case DexAction.Clear: dex.CaughtNone(); dex.SeenNone(); break;
        }
    }

    private static void Run6(Zukan6 dex, DexAction action, bool shiny, LanguageID language)
    {
        switch (action)
        {
            case DexAction.Complete: dex.SeenAll(shiny); dex.CaughtAll(language, allLanguages: true); break;
            case DexAction.SeenAll: dex.SeenAll(shiny); break;
            case DexAction.CaughtNone: dex.CaughtNone(); break;
            case DexAction.Clear: dex.CaughtNone(); dex.SeenNone(); break;
        }
    }

    // Gen 1 a 3: so visto/capturado por especie.
    private static void RunSimple(SaveFile sav, DexAction action)
    {
        for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
        {
            switch (action)
            {
                case DexAction.Complete: sav.SetSeen(s, true); sav.SetCaught(s, true); break;
                case DexAction.SeenAll: sav.SetSeen(s, true); break;
                case DexAction.CaughtNone: sav.SetCaught(s, false); break;
                case DexAction.Clear: sav.SetCaught(s, false); sav.SetSeen(s, false); break;
            }
        }
    }
}
