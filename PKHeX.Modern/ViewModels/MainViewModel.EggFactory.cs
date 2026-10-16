using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Fábrica de ovos: um ovo para cada espécie que falta no save.</summary>
    public RelayCommand MissingEggsCommand => _missingEggs ??= new RelayCommand(() => _ = EggFactoryAsync(true));
    private RelayCommand? _missingEggs;
    /// <summary>Fábrica de ovos: um ovo de cada família que nasce de ovo no jogo.</summary>
    public RelayCommand FamilyEggsCommand => _familyEggs ??= new RelayCommand(() => _ = EggFactoryAsync(false));
    private RelayCommand? _familyEggs;
    private bool _makingEggs;

    /// <summary>Vagas livres das caixas, a partir da caixa aberta (depois as seguintes e, por fim, as anteriores).</summary>
    private List<(int Box, int Slot)> FreeEggSlots(SaveFile sav)
    {
        var list = new List<(int, int)>();
        for (int n = 0; n < sav.BoxCount; n++)
        {
            int box = (Boxes.CurrentBox + n) % sav.BoxCount;
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                if (CoreAdapter.IsEmpty(sav.GetBoxSlotAtIndex(box, slot)))
                    list.Add((box, slot));
        }
        return list;
    }

    /// <summary>
    /// Gera os ovos em segundo plano, mostra a lista para confirmar e grava nas vagas livres das caixas com um só desfazer.
    /// No modo legal, só ovos legais.
    /// </summary>
    public async Task EggFactoryAsync(bool onlyMissing)
    {
        if (_sav is not { } sav || _history is null || _makingEggs || BlockedByHardcore("a fábrica de ovos"))
            return;
        if (!EggFactory.IsSupported(sav))
        {
            Status = "A Gen 1 não tem ovos.";
            return;
        }
        var requests = EggFactory.Plan(sav, onlyMissing);
        if (requests.Count == 0)
        {
            Status = onlyMissing ? "Nenhuma espécie que nasce de ovo falta neste save." : "Nenhuma família nasce de ovo neste jogo.";
            return;
        }
        var free = FreeEggSlots(sav);
        if (free.Count == 0)
        {
            Status = "As caixas estão cheias. Libere espaço para os ovos.";
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;

        bool legal = LegalMode;
        _makingEggs = true;
        Status = string.Format(Loc.T("Fábrica de ovos: procurando os ovos de {0} espécie(s)..."), requests.Count);
        IReadOnlyList<EggResult> results;
        try
        {
            results = await Task.Run(() => EggFactory.Make(sav, requests, legal,
                (i, n) => Dispatcher.UIThread.Post(() => Status = string.Format(Loc.T("Fábrica de ovos: {0} de {1}..."), i, n)), free.Count));
        }
        catch (Exception ex)
        {
            Status = "Erro na fábrica de ovos: " + ex.Message;
            return;
        }
        finally
        {
            _makingEggs = false;
        }
        if (!ReferenceEquals(_sav, sav) || !free.SequenceEqual(FreeEggSlots(sav)))
        {
            Status = "O save mudou durante a fábrica de ovos. Tente de novo.";
            return;
        }

        var ready = results.Where(r => r.Egg is not null).ToList();
        var details = results.Select(r => (r.Egg is null ? "✗ " : "🥚 ") + r.Name + (r.Note.Length > 0 ? " — " + Loc.T(r.Note) : "")).ToList();
        if (ready.Count == 0)
        {
            await ConfirmAsync("Fábrica de ovos", "Nenhum ovo pôde ser gerado.", "OK", cancelText: "", details: details, icon: "🥚", detailsExpanded: true);
            return;
        }
        int left = requests.Count - results.Count;
        var message = string.Format(Loc.T("{0} ovo(s) vão para as vagas livres das caixas, a partir de {1}. Dá para desfazer com Ctrl+Z."),
            ready.Count, CoreAdapter.GetBoxName(sav, free[0].Box))
            + (left > 0 ? " " + string.Format(Loc.T("{0} ficaram de fora porque as caixas encheram."), left) : "");
        if (!await ConfirmAsync("Fábrica de ovos", message, "Gerar ovos", details: details, icon: "🥚", detailsExpanded: true))
            return;
        if (!ReferenceEquals(_sav, sav) || !free.SequenceEqual(FreeEggSlots(sav)))
        {
            Status = "O save mudou antes de gravar os ovos. Tente de novo.";
            return;
        }

        var targets = free.Take(ready.Count).ToList();
        _history.Record(string.Format(Loc.T("fábrica de ovos ({0})"), ready.Count), [.. targets.Select(t => new SlotHistory.Key(t.Box, t.Slot))]);
        try
        {
            for (int n = 0; n < ready.Count; n++)
                CoreAdapter.SetBoxSlot(sav, ready[n].Egg!, targets[n].Box, targets[n].Slot);
        }
        catch (Exception ex)
        {
            _history.Rollback(); // nada pela metade
            Status = "Erro ao gravar os ovos: " + ex.Message;
            OnHistoryChanged();
            RefreshSlots();
            return;
        }
        IsDirty = true;
        OnHistoryChanged();
        CurrentPage = Boxes;
        Boxes.CurrentBox = targets[0].Box;
        RefreshSlots();
        Status = string.Format(Loc.T("{0} ovo(s) nas caixas. Ctrl+Z desfaz; Salvar grava no arquivo."), ready.Count);
    }
}
