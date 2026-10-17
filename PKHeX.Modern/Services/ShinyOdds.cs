using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um número que o método pede (tamanho da corrente, combo, surtos derrotados...).</summary>
public sealed record OddsParam(string Label, int Max, string Tip = "");

/// <summary>
/// Um método de caçada shiny num jogo. <see cref="Probability"/> recebe os dois parâmetros e o Shiny Charm e devolve a
/// chance de um encontro sair shiny. <see cref="Verified"/> = false aparece como "conferir" (valor com dúvida).
/// </summary>
public sealed record ShinyMethod(string Key, string Name, string Note, string Source, bool Verified, bool UsesCharm,
    Func<int, int, bool, double> Probability, OddsParam? Param1 = null, OddsParam? Param2 = null);

/// <summary>
/// Chances de shiny por jogo e método, como o Biolume (github.com/HydrosPlays/Biolume), mas a partir do save aberto.
/// A chance com várias tentativas ("rolls") é 1 − (1 − 1/base)^tentativas, a mesma conta do jogo.
/// Fontes: páginas "Shiny Pokémon", "Masuda method", "Poké Radar", "Chain fishing", "SOS Battle", "Catch combo",
/// "Mass outbreak", "Sandwich" e "Dynamax Adventure" da Bulbapedia e as tabelas equivalentes da Serebii.
/// </summary>
public static class ShinyOdds
{
    public const string BulbaShiny = "Bulbapedia: Shiny Pokémon";

    /// <summary>Chance de um encontro com <paramref name="rolls"/> tentativas na base 1/<paramref name="baseOdds"/>.</summary>
    public static double Rolls(int baseOdds, int rolls) => rolls <= 0 ? 0 : 1 - Math.Pow(1 - 1.0 / baseOdds, rolls);

    /// <summary>Encontros para chegar a <paramref name="target"/> (0,5 = 50%) de chance de ter visto pelo menos um shiny.</summary>
    public static int EncountersFor(double p, double target) =>
        p <= 0 ? int.MaxValue : p >= 1 ? 1 : (int)Math.Ceiling(Math.Log(1 - target) / Math.Log(1 - p));

    /// <summary>Chance de já ter achado pelo menos um shiny depois de <paramref name="encounters"/> encontros.</summary>
    public static double ChanceBy(double p, long encounters) => p <= 0 ? 0 : 1 - Math.Pow(1 - p, encounters);

    /// <summary>"1/4096" a partir da chance.</summary>
    public static string OneIn(double p) => p <= 0 ? "—" : $"1/{1 / p:0.#}";

    /// <summary>O jogo do save tem Shiny Charm? (B2W2 em diante, menos Let's Go sem ele e jogos sem o item.)</summary>
    public static bool HasCharmItem(SaveFile sav) => sav is SAV5B2W2 or SAV6XY or SAV6AO or SAV7SM or SAV7USUM or SAV7b
        or SAV8SWSH or SAV8BS or SAV8LA or SAV9SV or SAV9ZA;

    /// <summary>O Shiny Charm está na mochila do save?</summary>
    public static bool SaveHasCharm(SaveFile sav)
    {
        if (!HasCharmItem(sav))
            return false;
        try
        {
            var names = CoreAdapter.GetItemNames(sav);
            int id = -1;
            for (int i = 0; i < names.Count; i++)
                if (names[i] == "Shiny Charm") { id = i; break; }
            if (id < 0)
                return false;
            return CoreAdapter.GetBag(sav).Pouches.Any(p => p.Items.Any(it => it.Index == id && it.Count > 0));
        }
        catch
        {
            return false;
        }
    }

    // Métodos por jogo. Os mesmos registros servem a vários jogos.
    private static ShinyMethod Full(int baseOdds, bool charm) => new("full", "Encontro comum", "Selvagem, estático ou ovo sem bônus.",
        BulbaShiny, true, charm, (_, _, c) => Rolls(baseOdds, 1 + (c ? 2 : 0)));

    private static ShinyMethod Masuda(int baseOdds, int extra, bool charm) => new("masuda", "Método Masuda (ovo)",
        "Pais de idiomas diferentes (jogo de outra região).", "Bulbapedia: Masuda method", true, charm,
        (_, _, c) => Rolls(baseOdds, 1 + extra + (c ? 2 : 0)));

    private static readonly ShinyMethod Gen2Breeding = new("g2breed", "Ovo com pai/mãe shiny (Gen 2)",
        "No Gold, Silver e Crystal, o filhote herda os DVs do pai do sexo oposto: com um shiny na creche, 1 em 64.",
        "Bulbapedia: Shiny Pokémon (Generation II breeding)", true, false, (_, _, _) => 1.0 / 64);

    private static readonly ShinyMethod RadarDPPt = new("radar4", "Poké Radar (corrente)",
        "Chance de cada moita brilhar; a corrente para em 40.", "Bulbapedia: Poké Radar (Generation IV)", true, false,
        (chain, _, _) => Math.Ceiling(65535.0 / (8200 - 200 * Math.Clamp(chain, 0, 40))) / 65536,
        new OddsParam("Corrente", 40, "Pokémon iguais seguidos no Poké Radar"));

    private static ShinyMethod ChainFishing(bool charm) => new("fishing", "Pesca em corrente",
        "Cada pescada seguida no mesmo lugar soma 2 tentativas, até 20.", "Bulbapedia: Chain fishing", true, charm,
        (chain, _, c) => Rolls(4096, 1 + 2 * Math.Clamp(chain, 0, 20) + (c ? 2 : 0)),
        new OddsParam("Corrente", 99, "Pescadas seguidas sem sair do lugar"));

    private static readonly ShinyMethod FriendSafari = new("safari", "Friend Safari", "Todo encontro no Friend Safari tem 4 tentativas a mais.",
        "Bulbapedia: Friend Safari", true, true, (_, _, c) => Rolls(4096, 5 + (c ? 2 : 0)));

    private static readonly ShinyMethod Sos = new("sos", "SOS (corrente de chamados)",
        "A partir do 11º chamado: +4 tentativas; do 21º: +8; do 31º: +12.", "Bulbapedia: SOS Battle", true, true,
        (chain, _, c) => Rolls(4096, 1 + (chain >= 31 ? 12 : chain >= 21 ? 8 : chain >= 11 ? 4 : 0) + (c ? 2 : 0)),
        new OddsParam("Corrente", 255, "Aliados chamados seguidos"));

    private static readonly ShinyMethod CatchCombo = new("combo", "Catch Combo",
        "Capturas seguidas da mesma espécie: 11+ soma 3 tentativas, 21+ soma 7, 31+ soma 11; o Lure soma 1.", "Bulbapedia: Catch combo", true, true,
        (combo, lure, c) => Rolls(4096, 1 + (combo >= 31 ? 11 : combo >= 21 ? 7 : combo >= 11 ? 3 : 0) + (lure > 0 ? 1 : 0) + (c ? 2 : 0)),
        new OddsParam("Combo", 999, "Capturas seguidas da mesma espécie"), new OddsParam("Lure ativo (0/1)", 1, "1 com Lure ou Super/Max Lure ativo"));

    private static readonly ShinyMethod DynamaxAdventure = new("dynamax", "Dynamax Adventure",
        "Chance fixa por aventura completada para o lendário: 1/300, ou 1/100 com o Shiny Charm.", "Bulbapedia: Dynamax Adventure", true, true,
        (_, _, c) => c ? 1.0 / 100 : 1.0 / 300);

    private static readonly ShinyMethod OutbreakSv = new("outbreak9", "Surto em massa (com sanduíche)",
        "30–59 derrotados no surto: +1 tentativa; 60+: +2. Sparkling Power soma 1, 2 ou 3 tentativas (nível 1, 2 ou 3).",
        "Bulbapedia: Mass outbreak (Scarlet/Violet), Sandwich", true, true,
        (cleared, sparkle, c) => Rolls(4096, 1 + (cleared >= 60 ? 2 : cleared >= 30 ? 1 : 0) + Math.Clamp(sparkle, 0, 3) + (c ? 2 : 0)),
        new OddsParam("Derrotados no surto", 999), new OddsParam("Sparkling Power (0–3)", 3, "Nível do Sparkling Power do sanduíche"));

    private static readonly ShinyMethod SparklingSv = new("sparkling9", "Sanduíche (Sparkling Power)",
        "Fora de surto: o Sparkling Power soma 1, 2 ou 3 tentativas.", "Bulbapedia: Sandwich", true, true,
        (sparkle, _, c) => Rolls(4096, 1 + Math.Clamp(sparkle, 0, 3) + (c ? 2 : 0)),
        new OddsParam("Sparkling Power (0–3)", 3));

    private static readonly ShinyMethod OutbreakLa = new("outbreak8a", "Mass Outbreak (Legends: Arceus)",
        "Surto comum: +25 tentativas. Pesquisa da espécie: nível 10 soma 1, completa soma 3. O Shiny Charm soma 3.",
        "Bulbapedia: Mass outbreak (Legends: Arceus)", false, true,
        (research, _, c) => Rolls(4096, 1 + 25 + ResearchLa(research) + (c ? 3 : 0)),
        new OddsParam("Pesquisa (0 nada, 1 nível 10, 2 completa)", 2));

    private static readonly ShinyMethod FullLa = new("full8a", "Encontro comum (Legends: Arceus)",
        "Pesquisa da espécie: nível 10 soma 1 tentativa, completa soma 3. O Shiny Charm soma 3.", BulbaShiny, false, true,
        (research, _, c) => Rolls(4096, 1 + ResearchLa(research) + (c ? 3 : 0)),
        new OddsParam("Pesquisa (0 nada, 1 nível 10, 2 completa)", 2));

    private static int ResearchLa(int level) => level switch { >= 2 => 3, 1 => 1, _ => 0 };

    /// <summary>Métodos que valem para o jogo do save (o primeiro é o encontro comum).</summary>
    public static IReadOnlyList<ShinyMethod> For(SaveFile? sav) => sav switch
    {
        SAV2 => [Full(8192, false), Gen2Breeding],
        SAV3E or SAV3RS or SAV3FRLG => [Full(8192, false)],
        SAV4DP or SAV4Pt => [Full(8192, false), Masuda(8192, 4, false), RadarDPPt],
        SAV4HGSS => [Full(8192, false), Masuda(8192, 4, false)],
        SAV5BW => [Full(8192, false), Masuda(8192, 5, false)],
        SAV5B2W2 => [Full(8192, true), Masuda(8192, 5, true)],
        SAV6XY => [Full(4096, true), Masuda(4096, 5, true), ChainFishing(true), FriendSafari],
        SAV6AO => [Full(4096, true), Masuda(4096, 5, true), ChainFishing(true)],
        SAV7SM or SAV7USUM => [Full(4096, true), Masuda(4096, 5, true), Sos],
        SAV7b => [Full(4096, true), CatchCombo],
        SAV8SWSH => [Full(4096, true), Masuda(4096, 5, true), DynamaxAdventure],
        SAV8BS => [Full(4096, true), Masuda(4096, 5, true)],
        SAV8LA => [FullLa, OutbreakLa],
        SAV9SV => [Full(4096, true), Masuda(4096, 5, true), SparklingSv, OutbreakSv],
        SAV9ZA => [Full(4096, false)],
        _ => [],
    };

    /// <summary>Métodos que ainda não entraram por falta de valores seguros (mostrados como aviso na página).</summary>
    public static string Missing(SaveFile? sav) => sav switch
    {
        SAV6XY => "Poké Radar e DexNav ainda não entraram (valores a conferir).",
        SAV6AO => "DexNav ainda não entrou (valores a conferir).",
        SAV7USUM => "Ultra Wormholes ainda não entraram (valores a conferir).",
        SAV8SWSH => "O bônus de derrotados (Number Battled) e a Brilliant Aura ainda não entraram (valores a conferir).",
        SAV8BS => "O Poké Radar do BD/SP ainda não entrou (valor a conferir).",
        SAV9ZA => "O Shiny Charm e os bônus do Z-A ainda não entraram (valores a conferir).",
        _ => "",
    };
}
