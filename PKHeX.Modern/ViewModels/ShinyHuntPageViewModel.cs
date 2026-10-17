using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Página "Caçada shiny" (inspirada no Biolume): contador, chances reais por jogo e método, ficha do alvo (locais,
/// shiny lock, golpes perigosos) e o "Achei!" lido do save. Só lê o save; a caçada fica em <see cref="ShinyHuntStore"/>.
/// </summary>
public sealed class ShinyHuntPageViewModel : PageViewModel
{
    private SaveFile? _sav;
    private ShinyHunt? _hunt;
    private readonly Action<string> _status;
    private readonly Stack<long> _undo = new();
    private bool _loading;

    public ShinyHuntPageViewModel(Action<string> status)
    {
        _status = status;
        _hunt = ShinyHuntStore.Load();
        AddOneCommand = new RelayCommand(() => Add(1), () => HasHunt && !IsFinished);
        AddStepCommand = new RelayCommand(() => Add(Math.Max(1, Step)), () => HasHunt && !IsFinished);
        UndoCommand = new RelayCommand(UndoStep, () => _undo.Count > 0);
        ResetCommand = new RelayCommand(ResetCount, () => HasHunt && Count > 0);
        NewHuntCommand = new RelayCommand(ClearHunt, () => HasHunt);
    }

    public override string Title => "Caçada shiny";
    public override string Icon => "✨";

    public RelayCommand AddOneCommand { get; }
    public RelayCommand AddStepCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand NewHuntCommand { get; }

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        _loading = true;
        SpeciesNames = [.. Enumerable.Range(1, sav.MaxSpeciesID).Where(s => s < CoreAdapter.SpeciesNames.Count).Select(s => CoreAdapter.SpeciesNames[s])];
        Methods = ShinyOdds.For(sav);
        CharmInSave = ShinyOdds.SaveHasCharm(sav);
        if (_hunt is { } h && IsSameGame(h))
            SelectedMethod = Methods.FirstOrDefault(m => m.Key == h.Method) ?? Methods.FirstOrDefault();
        else
            SelectedMethod = Methods.FirstOrDefault();
        _loading = false;
        RaiseAll();
        _ = LoadLocationsAsync();
    }

    private bool IsSameGame(ShinyHunt h) => _sav is { } s && h.Version == (int)s.Version;

    // Alvo
    public IReadOnlyList<string> SpeciesNames { get; private set; } = [];
    public bool HasHunt => _hunt is not null;
    public bool HasNoHunt => !HasHunt;
    public bool IsOtherGame => _hunt is not null && _sav is not null && !IsSameGame(_hunt);
    public string OtherGameText => _hunt is null || _sav is null ? ""
        : string.Format(Loc.T("Esta caçada é do {0} e o save aberto é do {1}: o contador continua, mas as chances e os locais são do jogo aberto."),
            EncounterDatabase.GetVersionName((GameVersion)_hunt.Version), EncounterDatabase.GetVersionName(_sav.Version));
    public string TargetName
    {
        get => _hunt is { } h && h.Species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[h.Species] : _typed;
        set
        {
            _typed = value ?? "";
            var index = SpeciesNames.ToList().FindIndex(n => string.Equals(n, _typed.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                StartHunt((ushort)(index + 1));
        }
    }
    private string _typed = "";
    public Bitmap? TargetSprite => _hunt is { } h ? SpriteService.GetSpeciesSprite(h.Species, true, h.Form, 0, _sav?.Context ?? EntityContext.Gen9) : null;

    /// <summary>Começa (ou troca) a caçada: os shiny da família que já estão no save não contam para o "Achei!".</summary>
    public void StartHunt(ushort species)
    {
        if (_sav is not { } sav || species == 0)
            return;
        if (_hunt is { } current && current.Species == species && IsSameGame(current))
            return;
        _hunt = new ShinyHunt
        {
            Species = species,
            Version = (int)sav.Version,
            Method = SelectedMethod?.Key ?? "full",
            Charm = CharmInSave,
            KnownShinies = [.. ShinyHuntInfo.ShiniesOf(sav, species).Select(x => ShinyHuntInfo.KeyOf(x.Pk))],
        };
        _undo.Clear();
        Save();
        RaiseAll();
        _ = LoadLocationsAsync();
        _status(string.Format(Loc.T("Caçada de {0} começada. Espaço ou + conta um encontro."), CoreAdapter.SpeciesNames[species]));
    }

    private void ClearHunt()
    {
        _hunt = null;
        _typed = "";
        _undo.Clear();
        ShinyHuntStore.Save(null);
        Locations = [];
        RaiseAll();
    }

    // Método e Shiny Charm
    public IReadOnlyList<ShinyMethod> Methods { get; private set; } = [];
    private ShinyMethod? _method;
    public ShinyMethod? SelectedMethod
    {
        get => _method;
        set
        {
            if (!Set(ref _method, value) || _loading)
                return;
            if (_hunt is { } h && value is not null && IsSameGame(h))
            {
                h.Method = value.Key;
                Save();
            }
            RaiseOdds();
        }
    }
    public bool CharmInSave { get; private set; }
    public bool CanUseCharm => SelectedMethod?.UsesCharm == true && _sav is { } s && ShinyOdds.HasCharmItem(s);
    public bool Charm
    {
        get => _hunt?.Charm ?? CharmInSave;
        set { if (_hunt is { } h) { h.Charm = value; Save(); } RaiseOdds(); }
    }
    public string CharmNote => CharmInSave ? "O save tem o Shiny Charm na mochila." : "O save ainda não tem o Shiny Charm.";
    public bool HasParam1 => SelectedMethod?.Param1 is not null;
    public bool HasParam2 => SelectedMethod?.Param2 is not null;
    public string Param1Label => SelectedMethod?.Param1?.Label ?? "";
    public string Param2Label => SelectedMethod?.Param2?.Label ?? "";
    public int Param1Max => SelectedMethod?.Param1?.Max ?? 0;
    public int Param2Max => SelectedMethod?.Param2?.Max ?? 0;
    public int Param1
    {
        get => _hunt?.Param1 ?? 0;
        set { if (_hunt is { } h) { h.Param1 = Math.Max(0, value); Save(); } RaiseOdds(); }
    }
    public int Param2
    {
        get => _hunt?.Param2 ?? 0;
        set { if (_hunt is { } h) { h.Param2 = Math.Max(0, value); Save(); } RaiseOdds(); }
    }
    public string MethodNote => SelectedMethod?.Note ?? "";
    public string MethodSource => SelectedMethod is { } m ? Loc.T("Fonte: ") + m.Source : "";
    public bool MethodUnverified => SelectedMethod is { Verified: false };
    public string MissingNote => ShinyOdds.Missing(_sav);
    public bool HasMissingNote => MissingNote.Length > 0;
    public bool HasMethods => Methods.Count > 0;
    public bool NoMethods => _sav is not null && Methods.Count == 0;

    // Contador e chances
    public long Count => _hunt?.Count ?? 0;
    public string CountText => Count.ToString("N0");
    private int _step = 5;
    public int Step { get => _step; set => Set(ref _step, Math.Max(1, value)); }
    public double Probability => SelectedMethod is { } m ? m.Probability(Param1, Param2, Charm && CanUseCharm) : 0;
    public string OddsText => ShinyOdds.OneIn(Probability);
    public double ChanceNow => ShinyOdds.ChanceBy(Probability, Count);
    public string ChanceText => string.Format(Loc.T("{0:P1} de chance de já ter achado"), ChanceNow);
    public string OverOddsText => Probability <= 0 ? "" : string.Format(Loc.T("{0:0.00}× as chances"), Count * Probability);
    public string MilestonesText => Probability <= 0 ? "" : string.Format(Loc.T("50%: {0:N0} · 90%: {1:N0} · 99%: {2:N0} encontros"),
        ShinyOdds.EncountersFor(Probability, 0.5), ShinyOdds.EncountersFor(Probability, 0.9), ShinyOdds.EncountersFor(Probability, 0.99));
    public string NextMilestoneText
    {
        get
        {
            if (Probability <= 0)
                return "";
            foreach (var target in new[] { 0.5, 0.9, 0.99 })
            {
                var n = ShinyOdds.EncountersFor(Probability, target);
                if (Count < n)
                    return string.Format(Loc.T("Faltam {0:N0} encontros para {1:P0}"), n - Count, target);
            }
            return "Já passou de 99%: a próxima pode ser a sua.";
        }
    }
    public string TimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(_hunt?.Seconds ?? 0);
            return string.Format(Loc.T("{0}h {1:00}min caçando"), (int)t.TotalHours, t.Minutes);
        }
    }
    public string PaceText => _hunt is { Seconds: > 60 } h ? string.Format(Loc.T("{0:N0} encontros por hora"), h.Count / (h.Seconds / 3600)) : "";

    private void Add(long step)
    {
        if (_hunt is not { IsFinished: false } h)
            return;
        _undo.Push(h.Count);
        h.Add(step, DateTime.Now);
        Save();
        RaiseCount();
    }

    private void UndoStep()
    {
        if (_hunt is not { } h || _undo.Count == 0)
            return;
        h.Count = _undo.Pop();
        Save();
        RaiseCount();
    }

    private void ResetCount()
    {
        if (_hunt is not { } h)
            return;
        _undo.Push(h.Count);
        h.Count = 0;
        Save();
        RaiseCount();
    }

    /// <summary>Teclado (Espaço/+ e −), tratado pela janela quando esta página está aberta e nada está sendo digitado.</summary>
    public void CountKey(bool up)
    {
        if (up) AddOneCommand.Execute(null);
        else UndoCommand.Execute(null);
    }

    // Achei!
    public bool IsFinished => _hunt?.IsFinished == true;
    public string FoundText => _hunt is { FoundAt: { } at } h
        ? string.Format(Loc.T("✨ Achou em {0:dd/MM/yyyy}, com {1:N0} encontros. {2}"), at, h.Count, h.FoundWhere) : "";

    /// <summary>Shiny novo da família do alvo no save (o que não estava lá quando a caçada começou), ou null.</summary>
    public (PKM Pk, string Where)? FindNewShiny()
    {
        if (_sav is not { } sav || _hunt is not { IsFinished: false } h || !IsSameGame(h))
            return null;
        foreach (var hit in ShinyHuntInfo.ShiniesOf(sav, h.Species))
            if (!h.KnownShinies.Contains(ShinyHuntInfo.KeyOf(hit.Pk)))
                return hit;
        return null;
    }

    /// <summary>Resposta ao "Achei!": conclui a caçada com o Pokémon do save, ou só lembra dele para não perguntar de novo.</summary>
    public void Resolve(PKM pk, string where, bool finish)
    {
        if (_hunt is not { } h)
            return;
        h.KnownShinies.Add(ShinyHuntInfo.KeyOf(pk));
        if (finish)
        {
            h.FoundAt = DateTime.Now;
            var caught = ShinyHuntInfo.CaughtText(pk);
            h.FoundWhere = string.Join(" · ", new[] { caught, where }.Where(x => x.Length > 0));
        }
        Save();
        RaiseAll();
    }

    // Ficha do alvo
    public IReadOnlyList<HuntLocationViewModel> Locations { get; private set; } = [];
    private bool _loadingLocations;
    public bool IsLoadingLocations { get => _loadingLocations; private set => Set(ref _loadingLocations, value); }
    public bool HasLocations => Locations.Count > 0;
    public bool NoLocations => HasHunt && !IsLoadingLocations && Locations.Count == 0;
    public bool AnyShinyLock => Locations.Count > 0 && Locations.All(l => l.ShinyLocked);

    private async Task LoadLocationsAsync()
    {
        if (_sav is not { } sav || _hunt is not { } h)
            return;
        var species = h.Species;
        IsLoadingLocations = true;
        try
        {
            var list = await Task.Run(() => ShinyHuntInfo.Locations(sav, species));
            if (!ReferenceEquals(_sav, sav) || _hunt?.Species != species)
                return;
            Locations = [.. list.Select(l => new HuntLocationViewModel(l))];
        }
        catch
        {
            Locations = [];
        }
        finally
        {
            IsLoadingLocations = false;
        }
        foreach (var p in (string[])[nameof(Locations), nameof(HasLocations), nameof(NoLocations), nameof(AnyShinyLock)])
            Raise(p);
    }

    private void Save() => ShinyHuntStore.Save(_hunt);

    private void RaiseCount()
    {
        foreach (var p in (string[])[nameof(Count), nameof(CountText), nameof(ChanceNow), nameof(ChanceText), nameof(OverOddsText), nameof(NextMilestoneText), nameof(TimeText), nameof(PaceText)])
            Raise(p);
        foreach (var c in new[] { AddOneCommand, AddStepCommand, UndoCommand, ResetCommand, NewHuntCommand })
            c.NotifyCanExecuteChanged();
    }

    private void RaiseOdds()
    {
        foreach (var p in (string[])[nameof(Charm), nameof(CanUseCharm), nameof(HasParam1), nameof(HasParam2), nameof(Param1Label), nameof(Param2Label),
                     nameof(Param1Max), nameof(Param2Max), nameof(Param1), nameof(Param2), nameof(MethodNote), nameof(MethodSource), nameof(MethodUnverified),
                     nameof(Probability), nameof(OddsText), nameof(MilestonesText)])
            Raise(p);
        RaiseCount();
    }

    private void RaiseAll()
    {
        foreach (var p in (string[])[nameof(SpeciesNames), nameof(Methods), nameof(SelectedMethod), nameof(HasHunt), nameof(HasNoHunt), nameof(IsOtherGame), nameof(OtherGameText),
                     nameof(TargetName), nameof(TargetSprite), nameof(CharmInSave), nameof(CharmNote), nameof(MissingNote), nameof(HasMissingNote), nameof(HasMethods), nameof(NoMethods),
                     nameof(IsFinished), nameof(FoundText), nameof(Locations), nameof(HasLocations), nameof(NoLocations), nameof(AnyShinyLock)])
            Raise(p);
        RaiseOdds();
    }
}

/// <summary>Uma linha da ficha do alvo: local, tipo, níveis, shiny lock e golpes perigosos.</summary>
public sealed class HuntLocationViewModel(HuntLocation location)
{
    public string Location => location.Location;
    public string Kind => location.Kind;
    public string Levels => location.Levels;
    public bool ShinyLocked => location.ShinyLocked;
    public IReadOnlyList<RiskMove> Risks => location.Risks;
    public bool HasRisks => location.Risks.Count > 0;
}
