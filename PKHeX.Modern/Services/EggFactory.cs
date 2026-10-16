using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um ovo pedido à fábrica: a espécie que vai nascer e para qual espécie que falta ele serve.</summary>
public sealed record EggRequest(ushort Species, ushort For);

/// <summary>Resultado de um ovo: o Pokémon (ovo) gerado ou o motivo de não ter saído.</summary>
public sealed record EggResult(EggRequest Request, PKM? Egg, string Note)
{
    public string Name => CoreAdapter.SpeciesNames[Request.Species]
        + (Request.For != Request.Species ? $" (→ {CoreAdapter.SpeciesNames[Request.For]})" : "");
}

/// <summary>
/// Fábrica de ovos: gera ovos legais, a partir dos encontros de ovo do próprio jogo (os mesmos do Banco de encontros),
/// para cada espécie que falta no save (um ovo da forma básica para cada espécie da família que falta) ou um de cada
/// família que nasce de ovo. Só lê o save; quem grava é o MainViewModel, com um único desfazer.
/// </summary>
public static class EggFactory
{
    /// <summary>A Gen 1 não tem ovos.</summary>
    public static bool IsSupported(SaveFile? sav) => sav is { Generation: >= 2, HasBox: true };

    /// <summary>Ovos a gerar. <paramref name="onlyMissing"/>: um por espécie ausente do save (caixas e equipe); senão, um por família.</summary>
    public static IReadOnlyList<EggRequest> Plan(SaveFile sav, bool onlyMissing)
    {
        if (!IsSupported(sav))
            return [];
        EvolutionTree? tree = null;
        try { tree = EvolutionTree.GetEvolutionTree(sav.Context); } catch { /* sem arvore: cada especie e a propria base */ }
        ushort Base(ushort s)
        {
            if (tree is null)
                return s;
            try { return tree.GetBaseSpeciesForm(s, 0).Species; } catch { return s; }
        }

        var owned = new HashSet<ushort>();
        if (sav.HasParty)
            for (int i = 0; i < sav.PartyCount; i++)
                if (sav.GetPartySlotAtIndex(i) is { } pk && !CoreAdapter.IsEmpty(pk))
                    owned.Add(pk.Species);
        for (int box = 0; box < sav.BoxCount; box++)
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                if (sav.GetBoxSlotAtIndex(box, slot) is { } pk && !CoreAdapter.IsEmpty(pk))
                    owned.Add(pk.Species);

        var list = new List<EggRequest>();
        for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
        {
            if (!sav.Personal.IsPresentInGame(s, 0))
                continue;
            var b = Base(s);
            if (!sav.Personal.IsPresentInGame(b, 0) || !CanBreedFamily(sav, tree, b))
                continue;
            if (onlyMissing ? !owned.Contains(s) : b == s)
                list.Add(new EggRequest(b, s));
        }
        return list;
    }

    /// <summary>Alguém da família (a base ou uma evolução direta) tem grupo de ovo que cruza (bebês como Pichu e Togepi não cruzam, mas nascem).</summary>
    private static bool CanBreedFamily(SaveFile sav, EvolutionTree? tree, ushort baseSpecies)
    {
        bool Breeds(ushort s)
        {
            var p = sav.Personal[s];
            return ((EggGroup)p.EggGroup1).CanBreed || ((EggGroup)p.EggGroup2).CanBreed;
        }
        if (Breeds(baseSpecies))
            return true;
        if (tree is null)
            return false;
        try
        {
            foreach (var evo in tree.Forward.GetForward(baseSpecies, 0).Span)
                if (evo.Species <= sav.MaxSpeciesID && Breeds(evo.Species))
                    return true;
        }
        catch
        {
            // sem evolucoes conhecidas
        }
        return false;
    }

    /// <summary>Gera os ovos (lento: procura os encontros de ovo de cada espécie). Ovos iguais saem do mesmo encontro.</summary>
    public static IReadOnlyList<EggResult> Make(SaveFile sav, IReadOnlyList<EggRequest> requests, bool requireLegal,
        Action<int, int>? progress = null, int max = int.MaxValue, CancellationToken token = default)
    {
        var made = new Dictionary<ushort, (PKM? Egg, string Note)>();
        var results = new List<EggResult>(requests.Count);
        int count = 0;
        for (int i = 0; i < requests.Count && count < max; i++) // para quando as vagas acabam
        {
            token.ThrowIfCancellationRequested();
            progress?.Invoke(i, requests.Count);
            var r = requests[i];
            if (!made.TryGetValue(r.Species, out var hit))
                made[r.Species] = hit = MakeOne(sav, r.Species, requireLegal, token);
            results.Add(new EggResult(r, hit.Egg?.Clone(), hit.Note));
            if (hit.Egg is not null)
                count++;
        }
        progress?.Invoke(requests.Count, requests.Count);
        return results;
    }

    /// <summary>
    /// O mesmo que marcar "É ovo" no editor do PKHeX: contador de eclosão cheio, nome "Egg", sem local de encontro
    /// (o ovo foi recebido na creche, que fica no local do ovo). Variações de data e local para o
    /// <see cref="LegalityAnalysis"/> escolher a que vale em cada geração.
    /// </summary>
    private static IEnumerable<PKM> AsUnhatched(SaveFile sav, PKM hatched)
    {
        // Cada geração guarda o ovo de um jeito (Gen 3: local de eclosão e nível 0; Gen 5: nível 1 e sem local...)
        var locations = new[] { LocationEdits.GetNoneLocation(hatched), hatched.MetLocation, (ushort)0 }.Distinct().ToArray();
        var levels = new[] { hatched.MetLevel, (byte)0 }.Distinct().ToArray();
        foreach (var metLevel in levels)
        foreach (var metLocation in locations)
        foreach (var sameDate in hatched.Format >= 4 ? new[] { false, true } : [false])
        {
            var pk = hatched.Clone();
            pk.IsEgg = true;
            pk.CurrentFriendship = pk.PersonalInfo.HatchCycles;
            pk.MetLevel = metLevel;
            pk.MetLocation = metLocation;
            if (pk.Format >= 4)
                pk.MetDate = sameDate ? pk.EggMetDate : null;
            if (pk.Format >= 6)
                pk.ClearMemories();
            if (pk is PK9)
                pk.Version = 0; // em Scarlet/Violet o ovo fica sem versão até chocar
            pk.Nickname = SpeciesName.GetEggName(pk.Language, pk.Format);
            pk.IsNicknamed = EggStateLegality.IsNicknameFlagSet(pk);
            pk.RefreshChecksum();
            yield return pk;
        }
    }

    private static (PKM? Egg, string Note) MakeOne(SaveFile sav, ushort species, bool requireLegal, CancellationToken token)
    {
        IEnumerable<IEncounterInfo> eggs;
        try
        {
            eggs = EncounterDatabase.SearchEncounters(sav, species, onlyThisGame: true, token)
                .Where(e => e is IEncounterEgg && e.Species == species) // só ovo de criação (eventos e presentes têm outro treinador)
                .OrderBy(e => e.Version == sav.Version || e.Version.Contains(sav.Version) ? 0 : 1).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, ex.Message); }
        PKM? illegal = null;
        foreach (var enc in eggs)
        {
            token.ThrowIfCancellationRequested();
            if (EncounterDatabase.ToEntity(sav, enc, out _) is not { } made)
                continue;
            // Os encontros de ovo do Core geram o Pokemon ja chocado ("Force Hatch"): volta ele a ovo
            foreach (var egg in made.IsEgg ? [made] : AsUnhatched(sav, made))
            {
                if (new LegalityAnalysis(egg, sav.Personal, StorageSlotType.Box).Valid)
                    return (egg, "");
                illegal ??= egg;
            }
        }
        if (illegal is not null && !requireLegal)
            return (illegal, "ovo ilegal (modo legal desligado)");
        return (null, illegal is null ? "não nasce de ovo neste jogo" : "nenhum ovo legal");
    }
}
