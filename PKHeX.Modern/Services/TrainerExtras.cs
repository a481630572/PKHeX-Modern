using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um grupo de insignias (uma regiao): nomes e como ler/gravar cada bit.</summary>
public sealed record BadgeGroup(string Region, IReadOnlyList<string> Names, Func<int, bool> Get, Action<int, bool> Set);

/// <summary>
/// Campos do treinador que mudam de jogo para jogo (os do SAV_SimpleTrainer/SAV_Trainer do PKHeX): insignias, moedas
/// do Game Corner, BP e as datas de inicio da aventura e de entrada no Hall da Fama.
/// </summary>
public static class TrainerExtras
{
    private static readonly string[] Kanto = ["Boulder", "Cascade", "Thunder", "Rainbow", "Soul", "Marsh", "Volcano", "Earth"];
    private static readonly string[] Johto = ["Zephyr", "Hive", "Plain", "Fog", "Storm", "Mineral", "Glacier", "Rising"];
    private static readonly string[] Hoenn = ["Stone", "Knuckle", "Dynamo", "Heat", "Balance", "Feather", "Mind", "Rain"];
    private static readonly string[] Sinnoh = ["Coal", "Forest", "Cobble", "Fen", "Relic", "Mine", "Icicle", "Beacon"];
    private static readonly string[] UnovaBW = ["Trio", "Basic", "Insect", "Bolt", "Quake", "Jet", "Freeze", "Legend"];
    private static readonly string[] UnovaB2W2 = ["Basic", "Toxic", "Insect", "Bolt", "Quake", "Jet", "Legend", "Wave"];
    private static readonly string[] Kalos = ["Bug", "Cliff", "Rumble", "Plant", "Voltage", "Fairy", "Psychic", "Iceberg"];

    /// <summary>Insignias do save (vazio: o jogo nao tem, ou o PKHeX nao sabe onde ficam).</summary>
    public static IReadOnlyList<BadgeGroup> GetBadges(SaveFile sav) => sav switch
    {
        SAV1 s => [Bits("Kanto", Kanto, () => s.Badges, v => s.Badges = v, 0)],
        SAV2 s => [Bits("Johto", Johto, () => s.Badges, v => s.Badges = v, 0), Bits("Kanto", Kanto, () => s.Badges, v => s.Badges = v, 8)],
        SAV3FRLG s => [Bits("Kanto", Kanto, () => s.Badges, v => s.Badges = v, 0)],
        SAV3 s when s is SAV3RS or SAV3E => [Bits("Hoenn", Hoenn, () => s.Badges, v => s.Badges = v, 0)],
        SAV4HGSS s => [Bits("Johto", Johto, () => s.Badges, v => s.Badges = (byte)v, 0), Bits("Kanto", Kanto, () => s.Badges16, v => s.Badges16 = v, 0)],
        SAV4 s => [Bits("Sinnoh", Sinnoh, () => s.Badges, v => s.Badges = (byte)v, 0)],
        SAV5B2W2 s => [Bits("Unova", UnovaB2W2, () => s.Misc.Badges, v => s.Misc.Badges = v, 0)],
        SAV5 s => [Bits("Unova", UnovaBW, () => s.Misc.Badges, v => s.Misc.Badges = v, 0)],
        SAV6XY s => [Bits("Kalos", Kalos, () => s.Badges, v => s.Badges = v, 0)],
        SAV6AO s => [Bits("Hoenn", Hoenn, () => s.Badges, v => s.Badges = v, 0)],
        SAV8SWSH s => [Bits("Galar", Galar(s.Version), () => s.Badges, v => s.Badges = v, 0)],
        SAV8BS s => [new("Sinnoh", Sinnoh, i => s.FlagWork.GetSystemFlag(124 + i), (i, v) => s.FlagWork.SetSystemFlag(124 + i, v))],
        _ => [],
    };

    // Sword e Shield trocam dois ginasios (Fighting/Ghost e Rock/Ice).
    private static string[] Galar(GameVersion version) => version == GameVersion.SH
        ? ["Grass", "Water", "Fire", "Ghost", "Fairy", "Ice", "Dark", "Dragon"]
        : ["Grass", "Water", "Fire", "Fighting", "Fairy", "Rock", "Dark", "Dragon"];

    private static BadgeGroup Bits(string region, string[] names, Func<int> get, Action<int> set, int shift) => new(region, names,
        i => (get() >> (shift + i) & 1) != 0,
        (i, v) => { int bit = 1 << (shift + i); set(v ? get() | bit : get() & ~bit); });

    /// <summary>Moedas do Game Corner (Gen 1 a 4).</summary>
    public static bool HasCoins(SaveFile sav) => sav is SAV1 or SAV2 or SAV3 or SAV4;
    public static uint GetCoins(SaveFile sav) => sav switch { SAV1 s => s.Coin, SAV2 s => s.Coin, SAV3 s => s.Coin, SAV4 s => s.Coin, _ => 0 };
    public static void SetCoins(SaveFile sav, uint value)
    {
        value = Math.Min(value, (uint)sav.MaxCoins);
        switch (sav) { case SAV1 s: s.Coin = value; break; case SAV2 s: s.Coin = value; break; case SAV3 s: s.Coin = value; break; case SAV4 s: s.Coin = value; break; }
    }

    /// <summary>Battle Points (Emerald, Gen 4 a 8, menos Let's Go e BD/SP).</summary>
    public static bool HasBP(SaveFile sav) => sav is SAV3E or SAV4 or SAV5 or SAV6XY or SAV6AO or SAV7SM or SAV7USUM or SAV8SWSH;
    public static int GetBP(SaveFile sav) => sav switch
    {
        SAV3E s => s.SmallBlock.BP, SAV4 s => s.BP, SAV5 s => s.BattleSubway.BP, SAV6 s => s.BP,
        SAV7 s when s is SAV7SM or SAV7USUM => (int)((ISaveBlock7Main)s).Misc.BP, SAV8SWSH s => s.Misc.BP, _ => 0,
    };
    public static void SetBP(SaveFile sav, int value)
    {
        value = Math.Clamp(value, 0, 9999);
        switch (sav)
        {
            case SAV3E s: s.SmallBlock.BP = (ushort)value; break;
            case SAV4 s: s.BP = value; break;
            case SAV5 s: s.BattleSubway.BP = value; break;
            case SAV6 s: s.BP = value; break;
            case SAV7 s when s is SAV7SM or SAV7USUM: ((ISaveBlock7Main)s).Misc.BP = (uint)value; break;
            case SAV8SWSH s: s.Misc.BP = value; break;
        }
    }

    /// <summary>Genero do treinador: Gen 2 so no Crystal; Colosseum, XD e Battle Revolution nao tem escolha.</summary>
    public static bool HasGender(SaveFile sav) => sav switch
    {
        SAV1 or SAV3Colosseum or SAV3XD or SAV4BR => false,
        SAV2 s => s.Version == GameVersion.C,
        _ => true,
    };

    /// <summary>Datas de inicio e do Hall da Fama (segundos desde 2000), como no PKHeX: Gen 4 e 5.</summary>
    public static bool HasDates(SaveFile sav) => sav is SAV4 or SAV5;
    public static DateTime GetDate(uint seconds) { DateUtil.GetDateTime2000(seconds, out var date, out _); return date; }
    public static uint ToSeconds(DateTime value) => (uint)DateUtil.GetSecondsFrom2000(value.Date, new DateTime(2000, 1, 1).Add(value.TimeOfDay));
}
