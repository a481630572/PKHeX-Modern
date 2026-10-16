using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Aba Nuzlocke da Pokédex: o relatório do save aberto (<see cref="NuzlockeReport"/>), só leitura.</summary>
public sealed class NuzlockeViewModel(Action<int, int> goTo, Action<string> status) : ViewModelBase
{
    private SaveFile? _sav;
    private bool _loading;

    /// <summary>Copia o relatório em texto (definido pelo MainViewModel; usa a área de transferência da janela).</summary>
    public Func<string, Task>? CopyText { get; set; }

    public void Load(SaveFile sav)
    {
        _sav = sav;
        _loading = true;
        GraveyardOptions = ["Nenhuma (sem mortos)", .. Enumerable.Range(0, sav.BoxCount).Select(b => BoxName(sav, b))];
        Raise(nameof(GraveyardOptions));
        GraveyardIndex = NuzlockeReport.GuessGraveyard(sav) + 1;
        Raise(nameof(GraveyardIndex));
        _loading = false;
        Refresh();
    }

    private static string BoxName(SaveFile sav, int b)
    {
        try { return sav is IBoxDetailNameRead n && n.GetBoxName(b) is { Length: > 0 } name ? name : $"Caixa {b + 1}"; }
        catch { return $"Caixa {b + 1}"; }
    }

    public IReadOnlyList<string> GraveyardOptions { get; private set; } = [];
    private int _graveyard;
    /// <summary>Caixa onde ficam os mortos (0 = nenhuma); começa pela caixa com nome de cemitério, se houver.</summary>
    public int GraveyardIndex
    {
        get => _graveyard;
        set { if (Set(ref _graveyard, Math.Max(0, value)) && !_loading) Refresh(); }
    }

    public NuzlockeResult? Result { get; private set; }
    public IReadOnlyList<NuzlockeRouteViewModel> Routes { get; private set; } = [];
    public string Summary { get; private set; } = "";
    public string Note { get; private set; } = "";
    public string Unsupported { get; private set; } = "";
    public bool IsUnsupported => Unsupported.Length > 0;
    public bool HasRoutes => Routes.Count > 0;
    public bool IsEmpty => !IsUnsupported && Routes.Count == 0;

    public RelayCommand RefreshCommand => _refresh ??= new(Refresh);
    private RelayCommand? _refresh;
    public RelayCommand CopyCommand => _copy ??= new(() => _ = CopyAsync());
    private RelayCommand? _copy;

    public void Refresh()
    {
        if (_sav is not { } sav)
            return;
        var result = NuzlockeReport.Build(sav, _graveyard - 1);
        Result = result;
        Unsupported = result.Unsupported ?? "";
        Routes = [.. result.Routes.Select(r => new NuzlockeRouteViewModel(r, goTo))];
        Summary = result.Unsupported is not null ? ""
            : $"{result.Routes.Count} rotas · {result.Alive} vivos ({result.InParty} na equipe) · {result.Dead} mortos · {result.Dupes} repetidos · {result.Extras} capturas a mais";
        var notes = new List<string>();
        if (!result.HasDates && result.Routes.Count > 0)
            notes.Add("Este jogo não guarda a data de captura: o primeiro de cada rota é o que está antes (equipe, depois as caixas em ordem).");
        if (result.Outsiders.Count > 0)
            notes.Add($"{result.Outsiders.Count} Pokémon de trocas ou de outros jogos ficaram de fora.");
        if (result.Eggs > 0)
            notes.Add($"{result.Eggs} ovo(s) ainda não chocado(s) ficaram de fora.");
        Note = string.Join(" ", notes);
        foreach (var p in (string[])[nameof(Result), nameof(Routes), nameof(Summary), nameof(Note), nameof(Unsupported), nameof(IsUnsupported), nameof(HasRoutes), nameof(IsEmpty)])
            Raise(p);
    }

    private async Task CopyAsync()
    {
        if (_sav is null || Result is not { Unsupported: null } result || CopyText is null)
            return;
        await CopyText(NuzlockeReport.ToText(result, CoreAdapter.GetGameName(_sav)));
        status("Relatório de Nuzlocke copiado.");
    }
}

/// <summary>Uma linha do relatório: a rota, o primeiro encontro e as capturas a mais.</summary>
public sealed class NuzlockeRouteViewModel(NuzlockeRoute route, Action<int, int> goTo)
{
    public string Route => route.Route;
    public string Name => route.First.Name;
    public string Level => $"Nv. {route.First.Pkm.CurrentLevel}";
    public string Where => route.First.Where;
    public string Date => route.First.Date is { } d ? d.ToString("dd/MM/yyyy") : "";
    public bool HasDate => route.First.Date is not null;
    public Bitmap? Sprite => SpriteService.GetSprite(route.First.Pkm);
    public NuzlockeStatus Status => route.First.Status;
    public string StatusText => NuzlockeReport.StatusText(route.First.Status);
    public bool IsDead => Status == NuzlockeStatus.Dead;
    public bool IsParty => Status == NuzlockeStatus.Party;
    public bool IsDupe => route.IsDupe;
    public string DupeText => route.IsDupe ? $"Repetido ({route.DupeOf})" : "";
    public bool HasExtras => route.Extras.Count > 0;
    public string ExtrasText => route.Extras.Count == 1 ? "+1 a mais" : $"+{route.Extras.Count} a mais";
    public string ExtrasTip => string.Join(Environment.NewLine, route.Extras.Select(e => $"{e.Name} · {e.Where}"));
    public bool Hatched => route.First.Hatched;
    public RelayCommand GoCommand => new(() => goTo(route.First.Box, route.First.Slot));
}
