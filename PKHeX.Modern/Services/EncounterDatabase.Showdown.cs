using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public static partial class EncounterDatabase
{
    /// <summary>
    /// Set do Showdown → Pokemon legal (parecido com o Auto Legality Mod, mas so com o Core): procura um encontro real
    /// deste jogo gerando com os criterios do set (IVs, habilidade, natureza, genero, shiny) e reaplica o resto
    /// (nivel, item, apelido, golpes, EVs, amizade, Tera Type, Hyper Training, menta e capsula quando o jogo permite).
    /// Entre os resultados legais fica o que mais se parece com o set; <paramref name="notKept"/> diz o que mudou.
    /// </summary>
    /// <param name="fromSet">O set aplicado num Pokemon em branco do save (CoreAdapter.CreateBlank + ApplyShowdown).</param>
    public static PKM? LegalizeSet(SaveFile sav, PKM fromSet, IBattleTemplate set, out string message, out IReadOnlyList<string> notKept, CancellationToken token = default)
    {
        notKept = [];
        var encounters = SearchEncounters(sav, set.Species, onlyThisGame: true, token)
            .Where(e => e.Form == set.Form || e is MysteryGift || e.Species != set.Species)
            .OrderBy(e => GetPreference(e, fromSet))
            .Take(30)
            .ToList();
        if (encounters.Count == 0)
        {
            message = "nenhum encontro possível para esta espécie neste jogo";
            return null;
        }

        var pi = sav.Personal.GetFormEntry(set.Species, set.Form);
        var mutations = EncounterMutationUtil.GetSuggested(sav.Context, set.Level);
        var full = EncounterCriteria.GetCriteria(set, pi, mutations);
        var attempts = new List<EncounterCriteria> { full };
        if (mutations.HasFlag(EncounterMutation.CanMaxIndividualStat))
            attempts.Add(full.ReviseIVsHyperTrainAvailable());
        attempts.Add(full.WithoutIVs());
        attempts.Add(new() { Nature = full.Nature, Gender = full.Gender, Shiny = full.Shiny });
        attempts.Add(new() { Shiny = full.Shiny });
        attempts.Add(EncounterCriteria.Unrestricted);

        PKM? best = null;
        IEncounterInfo? bestEnc = null;
        List<string>? bestMissing = null;
        string? lastProblem = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        foreach (var criteria in attempts)
        {
            foreach (var enc in encounters)
            {
                token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline && best is not null)
                    break;
                if (ToEntity(sav, enc, criteria, out _) is not { } pk)
                    continue;
                if (criteria.Shiny == Shiny.Always && enc is IEncounterSlot3 slot3 && pk is PK3 pk3
                    && !ShinyMethodH.TrySetShiny(pk3, slot3, criteria.Nature, criteria.Gender, TimeSpan.FromSeconds(2)))
                    continue;
                CarryOver(fromSet, pk, enc);
                ApplySetExtras(fromSet, set, pk, mutations);
                var la = new LegalityAnalysis(pk);
                if (!la.Valid && pk.Species != enc.Species && RaiseToLegalLevel(fromSet, pk) is { } raised)
                    la = raised;
                if (!la.Valid)
                {
                    lastProblem ??= CoreAdapter.GetLegalityIssues(pk, 1).FirstOrDefault();
                    continue;
                }
                var missing = Differences(set, fromSet, pk);
                if (best is null || missing.Count < bestMissing!.Count)
                    (best, bestEnc, bestMissing) = (pk, enc, missing);
                if (missing.Count == 0)
                    break;
            }
            if (bestMissing is { Count: 0 })
                break;
        }
        if (best is null)
        {
            message = "nenhum encontro gerou um Pokémon legal" + (lastProblem is null ? "" : $" ({lastProblem})");
            return null;
        }
        notKept = bestMissing!;
        var kind = bestEnc is IEncounterable e ? e.LongName : "encontro";
        var where = GetLocationName(bestEnc!);
        message = kind + (where.Length > 0 ? $" em {where}" : "");
        return best;
    }

    /// <summary>O que o Legalizar comum nao leva: EVs, amizade, Tera Type, Hyper Training, menta e capsula/patch.</summary>
    private static void ApplySetExtras(PKM from, IBattleTemplate set, PKM pk, EncounterMutation mutations)
    {
        Span<int> evs = stackalloc int[6];
        from.GetEVs(evs);
        pk.SetEVs(evs);
        pk.CurrentFriendship = from.CurrentFriendship;
        if (pk is ITeraType tera && (byte)set.TeraType < 18)
            tera.SetTeraType(set.TeraType);
        // Menta (Gen 8+): a natureza de batalha muda sem trocar a do PID.
        if (mutations.HasFlag(EncounterMutation.CanMintNature) && set.Nature.IsFixed && pk.StatAlignment != set.Nature)
            pk.StatAlignment = set.Nature;
        // Capsula/patch: tenta a habilidade do set e volta se ficar ilegal.
        if (set.Ability > 0 && pk.Ability != set.Ability && mutations.HasFlag(EncounterMutation.CanAbilityCapsule))
        {
            var number = pk.AbilityNumber;
            pk.SetAbility(set.Ability);
            if (!new LegalityAnalysis(pk).Valid)
                pk.RefreshAbility(number switch { 2 => 1, 4 => 2, _ => 0 });
        }
        // Hyper Training: so onde o IV gerado ficou abaixo do pedido (e o nivel permite).
        if (mutations.HasFlag(EncounterMutation.CanMaxIndividualStat))
        {
            Span<int> ivs = stackalloc int[6];
            for (int i = 0; i < 6; i++)
                ivs[i] = set.IVs[i];
            pk.SetSuggestedHyperTrainingData(ivs);
        }
        pk.ResetPartyStats();
        pk.RefreshChecksum();
    }

    /// <summary>Partes do set que o resultado nao manteve (para avisar o usuario).</summary>
    private static List<string> Differences(IBattleTemplate set, PKM from, PKM pk)
    {
        var list = new List<string>();
        if (set.Nature.IsFixed && pk.StatAlignment != set.Nature) list.Add("natureza");
        if (set.Ability > 0 && pk.Ability != set.Ability) list.Add("habilidade");
        if (set.Shiny != pk.IsShiny) list.Add("shiny");
        if (set.Gender is <= 1 && pk.PersonalInfo.IsDualGender && pk.Gender != set.Gender) list.Add("gênero");
        if (pk.CurrentLevel != from.CurrentLevel) list.Add("nível");
        Span<int> got = stackalloc int[6];
        pk.GetIVs(got);
        bool ivs = true;
        for (int i = 0; i < 6; i++)
        {
            var want = set.IVs[i];
            var effective = pk is IHyperTrain ht && ht.IsHyperTrained(i) ? 31 : got[i];
            if (want is >= 0 and <= 31 && effective != want)
                ivs = false;
        }
        if (!ivs) list.Add("IVs");
        Span<ushort> a = stackalloc ushort[4];
        Span<ushort> b = stackalloc ushort[4];
        from.GetMoves(a);
        pk.GetMoves(b);
        if (a.ContainsAnyExcept((ushort)0) && !a.SequenceEqual(b)) list.Add("golpes");
        if (from.HeldItem != pk.HeldItem) list.Add("item");
        if (pk is ITeraType tera && (byte)set.TeraType < 18 && tera.TeraType != set.TeraType) list.Add("Tera Type");
        return list;
    }
}
