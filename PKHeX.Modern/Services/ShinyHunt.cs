using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Uma fase: um shiny que não era o alvo, registrado sem zerar a caçada.</summary>
public sealed class HuntPhase
{
    /// <summary>Contador da caçada quando o shiny apareceu.</summary>
    public long At { get; set; }
    public DateTime When { get; set; } = DateTime.Now;
    /// <summary>Espécie do shiny (0 = ainda não identificada).</summary>
    public ushort Species { get; set; }
    /// <summary>Onde está no save, quando veio do "Achei!".</summary>
    public string Where { get; set; } = "";
}

/// <summary>Uma caçada shiny: alvo, jogo, método, contador, tempo e fases. Guardada em JSON (<see cref="ShinyHuntStore"/>).</summary>
public sealed class ShinyHunt
{
    public ushort Species { get; set; }
    public byte Form { get; set; }
    /// <summary>Jogo da caçada (<see cref="GameVersion"/> do save em que ela começou).</summary>
    public int Version { get; set; }
    public string Method { get; set; } = "full";
    /// <summary>Nome do método quando a caçada terminou (para os troféus e o CSV).</summary>
    public string MethodName { get; set; } = "";
    public int Param1 { get; set; }
    public int Param2 { get; set; }
    public bool Charm { get; set; }
    public long Count { get; set; }
    public DateTime Started { get; set; } = DateTime.Now;
    /// <summary>Tempo caçando: soma dos intervalos entre contagens (pausas de mais de 10 minutos não contam).</summary>
    public double Seconds { get; set; }
    public DateTime? LastCount { get; set; }
    /// <summary>Shiny que já estavam no save (o "Achei!" só avisa dos novos).</summary>
    public List<string> KnownShinies { get; set; } = [];
    /// <summary>A lista acima tem os shiny de todas as espécies (as caçadas da etapa 1 guardavam só os da família do alvo).</summary>
    public bool KnowsAllShinies { get; set; }
    public List<HuntPhase> Phases { get; set; } = [];
    public DateTime? FoundAt { get; set; }
    public string FoundWhere { get; set; } = "";
    /// <summary>Identidade do Pokémon achado no save (<see cref="ShinyHuntInfo.KeyOf"/>), para o "Ir" dos troféus.</summary>
    public string FoundKey { get; set; } = "";

    public bool IsFinished => FoundAt is not null;
    /// <summary>Encontros desde o último shiny (fase atual).</summary>
    public long PhaseCount => Count - (Phases.Count > 0 ? Phases[^1].At : 0);

    /// <summary>Pausa máxima entre duas contagens que ainda conta como tempo caçando.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(10);

    public void Add(long step, DateTime now)
    {
        if (LastCount is { } last && now - last is { } gap && gap > TimeSpan.Zero && gap <= MaxGap)
            Seconds += gap.TotalSeconds;
        LastCount = now;
        Count = Math.Max(0, Count + step);
    }

    /// <summary>Cópia independente (para guardar nos troféus).</summary>
    public ShinyHunt Copy() => JsonSerializer.Deserialize<ShinyHunt>(JsonSerializer.Serialize(this))!;
}

/// <summary>Tudo o que a página guarda: a caçada atual e as concluídas (troféus).</summary>
public sealed class ShinyHuntData
{
    public ShinyHunt? Current { get; set; }
    public List<ShinyHunt> Trophies { get; set; } = [];
}

/// <summary>Grava as caçadas em shiny-hunt.json, na pasta dos backups (com cópia .bak e troca atômica).</summary>
public static class ShinyHuntStore
{
    private static string? _folder;
    /// <summary>Pasta do arquivo; sem definir, é a pasta dos backups (%APPDATA%\PKHeX.Modern), que os testes já mandam para uma pasta temporária.</summary>
    public static string Folder
    {
        get => _folder ?? Path.GetDirectoryName(SaveBackup.Folder) ?? SaveBackup.Folder;
        set => _folder = value;
    }
    private static string FilePath => Path.Combine(Folder, "shiny-hunt.json");

    public static ShinyHuntData Load()
    {
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                // A etapa 1 gravava só a caçada (com "Species" na raiz); agora vêm a caçada atual e os troféus
                if (doc.RootElement.TryGetProperty("Species", out _))
                    return new ShinyHuntData { Current = doc.RootElement.Deserialize<ShinyHunt>() is { Species: > 0 } legacy ? legacy : null };
                if (doc.RootElement.Deserialize<ShinyHuntData>() is { } data)
                    return data;
            }
            catch
            {
                // arquivo corrompido: tenta a cópia
            }
        }
        return new ShinyHuntData();
    }

    public static void Save(ShinyHuntData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (data.Current is null && data.Trophies.Count == 0)
            {
                File.Delete(FilePath);
                File.Delete(FilePath + ".bak"); // senão o Load traria a caçada apagada de volta
                return;
            }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(tmp, FilePath, true);
        }
        catch
        {
            // as caçadas continuam na memória; grava na próxima mudança
        }
    }
}

/// <summary>CSV das caçadas (troféus, a atual e as fases), para planilha.</summary>
public static class ShinyHuntCsv
{
    private static readonly string[] Headers = ["Tipo", "Espécie", "Caçada", "Jogo", "Método", "Encontros", "Fases", "Começou", "Fim", "Onde", "Horas"];

    public static byte[] Encode(ShinyHuntData data)
    {
        var text = new System.Text.StringBuilder();
        Line(Headers.Select(Loc.T));
        var hunts = data.Trophies.Select(t => (Hunt: t, Kind: "Troféu")).ToList();
        if (data.Current is { IsFinished: false } current)
            hunts.Add((current, "Em andamento"));
        foreach (var (h, kind) in hunts)
        {
            var target = Name(h.Species);
            Line([Loc.T(kind), target, target, Game(h.Version), h.MethodName.Length > 0 ? Loc.T(h.MethodName) : h.Method, N(h.Count), N(h.Phases.Count),
                Date(h.Started), h.FoundAt is { } f ? Date(f) : "", h.FoundWhere, (h.Seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture)]);
            foreach (var p in h.Phases)
                Line([Loc.T("Fase"), p.Species > 0 ? Name(p.Species) : Loc.T("a identificar"), target, Game(h.Version), "", N(p.At), "", "", Date(p.When), p.Where, ""]);
        }
        var encoding = new System.Text.UTF8Encoding(true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
        void Line(IEnumerable<string> cells) => text.AppendJoin(',', cells.Select(BoxReport.Escape)).Append("\r\n");
    }

    private static string Name(ushort species) => species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[species] : $"#{species}";
    private static string Game(int version) => EncounterDatabase.GetVersionName((GameVersion)version);
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Date(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Um local onde o alvo aparece no jogo, com níveis, shiny lock e golpes que podem estragar a caçada.</summary>
public sealed record HuntLocation(string Location, string Kind, string Levels, bool ShinyLocked, IReadOnlyList<RiskMove> Risks);

/// <summary>Golpe do Pokémon selvagem que pode acabar com o encontro, com o que fazer.</summary>
public sealed record RiskMove(string Move, string Danger, string Counter);

/// <summary>Ficha do alvo (locais, shiny lock, golpes perigosos) e o "Achei!" lido do save.</summary>
public static class ShinyHuntInfo
{
    // Golpes que encerram ou estragam o encontro, com o motivo e a resposta (IDs do PKHeX.Core)
    private static readonly Dictionary<ushort, (string Danger, string Counter)> Risky = new()
    {
        [(ushort)Move.Explosion] = ("o Pokémon desmaia", "use um Pokémon com Damp ou do tipo Ghost, ou capture antes"),
        [(ushort)Move.SelfDestruct] = ("o Pokémon desmaia", "use um Pokémon com Damp ou do tipo Ghost, ou capture antes"),
        [(ushort)Move.MistyExplosion] = ("o Pokémon desmaia", "use um Pokémon com Damp, ou capture antes"),
        [(ushort)Move.Memento] = ("o Pokémon desmaia", "use Taunt ou capture antes"),
        [(ushort)Move.HealingWish] = ("o Pokémon desmaia", "use Taunt ou capture antes"),
        [(ushort)Move.LunarDance] = ("o Pokémon desmaia", "use Taunt ou capture antes"),
        [(ushort)Move.FinalGambit] = ("o Pokémon desmaia", "use um Pokémon do tipo Ghost ou capture antes"),
        [(ushort)Move.DestinyBond] = ("derrubar o Pokémon derruba o seu junto", "não ataque no turno em que ele usar; capture com status"),
        [(ushort)Move.PerishSong] = ("todos desmaiam em 3 turnos", "capture rápido ou troque de Pokémon para zerar a contagem"),
        [(ushort)Move.Roar] = ("o encontro acaba", "use um Pokémon com Soundproof ou Suction Cups, ou Taunt"),
        [(ushort)Move.Whirlwind] = ("o encontro acaba", "use um Pokémon com Suction Cups, ou Taunt"),
        [(ushort)Move.Teleport] = ("o Pokémon foge", "use Mean Look, Block ou Shadow Tag, ou Taunt"),
        [(ushort)Move.DragonTail] = ("o encontro acaba", "use um Pokémon com Suction Cups ou do tipo Fairy"),
        [(ushort)Move.CircleThrow] = ("o encontro acaba", "use um Pokémon com Suction Cups ou do tipo Ghost"),
        [(ushort)Move.DoubleEdge] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.TakeDown] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.Submission] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.VoltTackle] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.FlareBlitz] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.BraveBird] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.WoodHammer] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.HeadSmash] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.WildCharge] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.HeadCharge] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.WaveCrash] = ("recuo: pode desmaiar sozinho", "deixe o HP dele alto antes de lançar a bola"),
        [(ushort)Move.Struggle] = ("recuo: pode desmaiar sozinho", "não deixe o PP dele acabar"),
    };

    public static IReadOnlyList<RiskMove> RisksOf(PKM pk)
    {
        var list = new List<RiskMove>();
        foreach (var m in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 })
            if (m != 0 && Risky.TryGetValue(m, out var r) && m < CoreAdapter.MoveNames.Count)
                list.Add(new RiskMove(CoreAdapter.MoveNames[m], Loc.T(r.Danger), Loc.T(r.Counter)));
        return list;
    }

    /// <summary>Onde o alvo aparece neste jogo (sem eventos, trocas e ovos), um por local e tipo, com a faixa de níveis.</summary>
    public static IReadOnlyList<HuntLocation> Locations(SaveFile sav, ushort species, int max = 40)
    {
        var groups = new Dictionary<string, (string Location, string Kind, int Min, int Max, bool Locked, List<RiskMove> Risks)>();
        var order = new List<string>();
        foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, onlyThisGame: true))
        {
            // Só os encontros deste jogo: o banco junta jogos do mesmo contexto (Black 2 num save de Black) e os ovos não têm local
            if (enc is MysteryGift || enc is IFixedTrainer { IsFixedTrainer: true } || enc.Species != species || enc.IsEgg
                || !(enc.Version == sav.Version || enc.Version.Contains(sav.Version)))
                continue;
            var location = EncounterDatabase.GetLocationName(enc);
            if (string.IsNullOrWhiteSpace(location))
                location = "—";
            var kind = enc is IEncounterable e ? e.LongName : EncounterDatabase.GetCategory(enc);
            var key = location + "|" + kind;
            if (!groups.TryGetValue(key, out var g))
            {
                if (groups.Count >= max)
                    continue;
                g = (location, kind, enc.LevelMin, enc.LevelMax, enc.Shiny == Shiny.Never, []);
                order.Add(key);
            }
            g.Min = Math.Min(g.Min, enc.LevelMin);
            g.Max = Math.Max(g.Max, enc.LevelMax);
            g.Locked &= enc.Shiny == Shiny.Never;
            if (EncounterDatabase.ToEntity(sav, enc, out _) is { } pk)
                foreach (var r in RisksOf(pk))
                    if (!g.Risks.Any(x => x.Move == r.Move))
                        g.Risks.Add(r);
            groups[key] = g;
        }
        return [.. order.Select(k => groups[k]).Select(g => new HuntLocation(g.Location, g.Kind,
            g.Min == g.Max ? $"Nv. {g.Min}" : $"Nv. {g.Min}–{g.Max}", g.Locked, g.Risks))];
    }

    /// <summary>Espécie base da família (Charizard → Charmander), para o "Achei!" aceitar o alvo já evoluído.</summary>
    public static ushort Family(SaveFile sav, ushort species)
    {
        try { return EvolutionTree.GetEvolutionTree(sav.Context).GetBaseSpeciesForm(species, 0).Species; }
        catch { return species; }
    }

    public static string KeyOf(PKM pk) => $"{pk.Species}:{pk.EncryptionConstant:X8}:{pk.PID:X8}";

    /// <summary>Shiny da família do alvo guardados no save (caixas e equipe), com onde estão.</summary>
    public static IEnumerable<(PKM Pk, string Where)> ShiniesOf(SaveFile sav, ushort species)
    {
        var family = Family(sav, species);
        bool Match(PKM pk) => !CoreAdapter.IsEmpty(pk) && !pk.IsEgg && pk.IsShiny && Family(sav, pk.Species) == family;
        if (sav.HasParty)
            for (int i = 0; i < sav.PartyCount; i++)
                if (sav.GetPartySlotAtIndex(i) is { } pk && Match(pk))
                    yield return (pk, $"Equipe · {i + 1}");
        for (int b = 0; b < sav.BoxCount; b++)
            for (int s = 0; s < sav.BoxSlotCount; s++)
                if (sav.GetBoxSlotAtIndex(b, s) is { } pk && Match(pk))
                    yield return (pk, $"{CoreAdapter.GetBoxName(sav, b)} · {s + 1}");
    }

    /// <summary>Todos os shiny guardados no save (caixas e equipe), com onde estão e a caixa/posição (caixa -1 = equipe).</summary>
    public static IEnumerable<(PKM Pk, string Where, int Box, int Slot)> AllShinies(SaveFile sav)
    {
        static bool IsShiny(PKM pk) => !CoreAdapter.IsEmpty(pk) && !pk.IsEgg && pk.IsShiny;
        if (sav.HasParty)
            for (int i = 0; i < sav.PartyCount; i++)
                if (sav.GetPartySlotAtIndex(i) is { } pk && IsShiny(pk))
                    yield return (pk, $"Equipe · {i + 1}", -1, i);
        for (int b = 0; b < sav.BoxCount; b++)
            for (int s = 0; s < sav.BoxSlotCount; s++)
                if (sav.GetBoxSlotAtIndex(b, s) is { } pk && IsShiny(pk))
                    yield return (pk, $"{CoreAdapter.GetBoxName(sav, b)} · {s + 1}", b, s);
    }

    /// <summary>Local e data de captura gravados no Pokémon (para o "Achei!").</summary>
    public static string CaughtText(PKM pk)
    {
        string where;
        try { where = GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, pk.Generation, pk.Version); }
        catch { where = ""; }
        var date = pk.MetDate is { } d ? d.ToString("dd/MM/yyyy") : "";
        return string.Join(" · ", new[] { where, date }.Where(x => x.Length > 0));
    }
}
