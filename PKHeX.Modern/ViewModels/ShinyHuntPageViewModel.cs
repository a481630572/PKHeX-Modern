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
/// shiny lock, golpes perigosos), fases, troféus, CSV e o "Achei!" lido do save. Só lê o save; as caçadas ficam em
/// <see cref="ShinyHuntStore"/>.
/// </summary>
public sealed class ShinyHuntPageViewModel : PageViewModel
{
    private SaveFile? _sav;
    private readonly ShinyHuntData _data;
    private readonly Action<string> _status;
    private readonly Action<int, int>? _goTo;
    private readonly Stack<long> _undo = new();
    private bool _loading;

    /// <param name="goTo">Abre um Pokémon do save aberto (caixa, posição; caixa −1 = equipe), para o "Ir" dos troféus.</param>
    public ShinyHuntPageViewModel(Action<string> status, Action<int, int>? goTo = null)
    {
        _status = status;
        _goTo = goTo;
        _data = ShinyHuntStore.Load();
        AddOneCommand = new RelayCommand(() => Add(1), () => HasHunt && !IsFinished);
        AddStepCommand = new RelayCommand(() => Add(Math.Max(1, Step)), () => HasHunt && !IsFinished);
        UndoCommand = new RelayCommand(UndoStep, () => _undo.Count > 0);
        ResetCommand = new RelayCommand(ResetCount, () => HasHunt && Count > 0);
        NewHuntCommand = new RelayCommand(() => _ = ClearHuntAsync(), () => HasHunt);
        LogPhaseCommand = new RelayCommand(() => AddPhase(SpeciesOf(PhaseSpeciesName), ""), () => HasHunt && !IsFinished);
        ExportCsvCommand = new RelayCommand(() => _ = ExportCsvAsync(), () => HasHunt || _data.Trophies.Count > 0);
    }

    private ShinyHunt? Hunt => _data.Current;

    public override string Title => "Caçada shiny";
    public override string Icon => "✨";

    /// <summary>Perguntas e o arquivo do CSV (definidos pelo MainViewModel e pela janela).</summary>
    public Func<string, string, string, Task<bool>>? Confirm { get; set; }
    public Func<byte[], string, Task<bool>>? SaveCsv { get; set; }

    public RelayCommand AddOneCommand { get; }
    public RelayCommand AddStepCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand NewHuntCommand { get; }
    public RelayCommand LogPhaseCommand { get; }
    public RelayCommand ExportCsvCommand { get; }

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        _loading = true;
        SpeciesNames = [.. Enumerable.Range(1, sav.MaxSpeciesID).Where(s => s < CoreAdapter.SpeciesNames.Count).Select(s => CoreAdapter.SpeciesNames[s])];
        Methods = ShinyOdds.For(sav);
        CharmInSave = ShinyOdds.SaveHasCharm(sav);
        if (Hunt is { } h && IsSameGame(h))
            SelectedMethod = Methods.FirstOrDefault(m => m.Key == h.Method) ?? Methods.FirstOrDefault();
        else
            SelectedMethod = Methods.FirstOrDefault();
        _loading = false;
        RaiseAll();
        _ = LoadLocationsAsync();
    }

    private bool IsSameGame(ShinyHunt h) => _sav is { } s && h.Version == (int)s.Version;

    private ushort SpeciesOf(string? name)
    {
        var index = SpeciesNames.ToList().FindIndex(n => string.Equals(n, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? (ushort)(index + 1) : (ushort)0;
    }

    // Alvo
    public IReadOnlyList<string> SpeciesNames { get; private set; } = [];
    public bool HasHunt => Hunt is not null;
    public bool HasNoHunt => !HasHunt;
    public bool IsOtherGame => Hunt is not null && _sav is not null && !IsSameGame(Hunt);
    public string OtherGameText => Hunt is null || _sav is null ? ""
        : string.Format(Loc.T("Esta caçada é do {0} e o save aberto é do {1}: o contador continua, mas as chances e os locais são do jogo aberto."),
            EncounterDatabase.GetVersionName((GameVersion)Hunt.Version), EncounterDatabase.GetVersionName(_sav.Version));
    public string TargetName
    {
        get => Hunt is { } h && h.Species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[h.Species] : _typed;
        set
        {
            _typed = value ?? "";
            if (SpeciesOf(_typed) is > 0 and var species)
                StartHunt(species);
        }
    }
    private string _typed = "";
    public Bitmap? TargetSprite => Hunt is { } h ? SpriteService.GetSpeciesSprite(h.Species, true, h.Form, 0, _sav?.Context ?? EntityContext.Gen9) : null;

    /// <summary>Começa (ou troca) a caçada: os shiny que já estão no save não contam para o "Achei!".</summary>
    public void StartHunt(ushort species)
    {
        if (_sav is not { } sav || species == 0)
            return;
        if (Hunt is { } current && current.Species == species && IsSameGame(current))
            return;
        _data.Current = new ShinyHunt
        {
            Species = species,
            Version = (int)sav.Version,
            Method = SelectedMethod?.Key ?? "full",
            Charm = CharmInSave,
            KnownShinies = [.. ShinyHuntInfo.AllShinies(sav).Select(x => ShinyHuntInfo.KeyOf(x.Pk))],
            KnowsAllShinies = true,
        };
        _undo.Clear();
        Save();
        RaiseAll();
        _ = LoadLocationsAsync();
        _status(string.Format(Loc.T("Caçada de {0} começada. Espaço ou + conta um encontro."), CoreAdapter.SpeciesNames[species]));
    }

    /// <summary>Nova caçada: a atual sai (a concluída continua nos troféus); pergunta antes se tiver encontros contados.</summary>
    private async Task ClearHuntAsync()
    {
        if (Hunt is { IsFinished: false, Count: > 0 } h && Confirm is { } confirm
            && !await confirm("Nova caçada", string.Format(Loc.T("A caçada de {0} tem {1:N0} encontros e ainda não achou o shiny. Apagar e começar outra?"),
                CoreAdapter.SpeciesNames[h.Species], h.Count), "Apagar caçada"))
            return;
        _data.Current = null;
        _typed = "";
        _undo.Clear();
        Save();
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
            // Trocar a lista (outro save) faz a caixa de seleção mandar "nenhum" de volta: ignora e mantém a escolha
            if (value is null && Methods.Count > 0)
            {
                Raise();
                return;
            }
            if (!Set(ref _method, value) || _loading)
                return;
            if (Hunt is { } h && value is not null && IsSameGame(h))
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
        get => Hunt?.Charm ?? CharmInSave;
        set { if (Hunt is { } h) { h.Charm = value; Save(); } RaiseOdds(); }
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
        get => Hunt?.Param1 ?? 0;
        set { if (Hunt is { } h) { h.Param1 = Math.Max(0, value); Save(); } RaiseOdds(); }
    }
    public int Param2
    {
        get => Hunt?.Param2 ?? 0;
        set { if (Hunt is { } h) { h.Param2 = Math.Max(0, value); Save(); } RaiseOdds(); }
    }
    public string MethodNote => SelectedMethod?.Note ?? "";
    public string MethodSource => SelectedMethod is { } m ? Loc.T("Fonte: ") + m.Source : "";
    public bool MethodUnverified => SelectedMethod is { Verified: false };
    public string MissingNote => ShinyOdds.Missing(_sav);
    public bool HasMissingNote => MissingNote.Length > 0;
    public bool HasMethods => Methods.Count > 0;
    public bool NoMethods => _sav is not null && Methods.Count == 0;

    // Contador e chances
    public long Count => Hunt?.Count ?? 0;
    public string CountText => Count.ToString("N0");
    private int _step = 5;
    public int Step { get => _step; set => Set(ref _step, Math.Max(1, value)); }
    public double Probability => SelectedMethod is { } m ? m.Probability(Param1, Param2, Charm && CanUseCharm) : 0;
    public string OddsText => ShinyOdds.OneIn(Probability);
    public double ChanceNow => ShinyOdds.ChanceBy(Probability, Count);
    public string ChanceText => string.Format(Loc.T("{0:0.0%} de chance de já ter achado"), ChanceNow);
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
                    return string.Format(Loc.T("Faltam {0:N0} encontros para {1:0%}"), n - Count, target);
            }
            return "Já passou de 99%: a próxima pode ser a sua.";
        }
    }
    public string TimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(Hunt?.Seconds ?? 0);
            return string.Format(Loc.T("{0}h {1:00}min caçando"), (int)t.TotalHours, t.Minutes);
        }
    }
    public string PaceText => Hunt is { Seconds: > 60 } h ? string.Format(Loc.T("{0:N0} encontros por hora"), h.Count / (h.Seconds / 3600)) : "";

    private void Add(long step)
    {
        if (Hunt is not { IsFinished: false } h)
            return;
        _undo.Push(h.Count);
        h.Add(step, DateTime.Now);
        Save();
        RaiseCount();
    }

    private void UndoStep()
    {
        if (Hunt is not { } h || _undo.Count == 0)
            return;
        h.Count = _undo.Pop();
        Save();
        RaiseCount();
    }

    private void ResetCount()
    {
        if (Hunt is not { } h)
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

    // Fases
    private string _phaseSpecies = "";
    /// <summary>Espécie do shiny da fase (opcional; dá para identificar depois na lista).</summary>
    public string PhaseSpeciesName { get => _phaseSpecies; set => Set(ref _phaseSpecies, value ?? ""); }
    public IReadOnlyList<HuntPhaseViewModel> Phases => Hunt is not { } h ? []
        : [.. h.Phases.Select((p, i) => new HuntPhaseViewModel(p, i + 1, i > 0 ? h.Phases[i - 1].At : 0, this)).Reverse()];
    public bool HasPhases => Hunt is { Phases.Count: > 0 };
    public string PhaseText => Hunt is { Phases.Count: > 0 } h
        ? string.Format(Loc.T("Fase {0}: {1:N0} encontros desde o último shiny"), h.Phases.Count + 1, h.PhaseCount) : "";

    /// <summary>Registra uma fase no contador atual (shiny que não era o alvo).</summary>
    public void AddPhase(ushort species, string where)
    {
        if (Hunt is not { IsFinished: false } h)
            return;
        h.Phases.Add(new HuntPhase { At = h.Count, When = DateTime.Now, Species = species, Where = where });
        PhaseSpeciesName = "";
        Save();
        RaisePhases();
        _status(string.Format(Loc.T("Fase {0} registrada no encontro {1:N0}."), h.Phases.Count, h.Count));
    }

    internal void SetPhaseSpecies(HuntPhase phase, string name)
    {
        if (SpeciesOf(name) is > 0 and var species && phase.Species != species)
        {
            phase.Species = species;
            Save();
        }
    }

    internal void RemovePhase(HuntPhase phase)
    {
        if (Hunt is not { } h || !h.Phases.Remove(phase))
            return;
        Save();
        RaisePhases();
    }

    // Achei!
    public bool IsFinished => Hunt?.IsFinished == true;
    public string FoundText => Hunt is { FoundAt: { } at } h
        ? string.Format(Loc.T("✨ Achou em {0:dd/MM/yyyy}, com {1:N0} encontros. {2}"), at, h.Count, h.FoundWhere) : "";

    /// <summary>
    /// Shiny novo no save (o que não estava lá quando a caçada começou), ou null. <c>IsTarget</c>: da família do alvo
    /// (conclui a caçada); senão, vira uma fase.
    /// </summary>
    public (PKM Pk, string Where, bool IsTarget)? FindNewShiny()
    {
        if (_sav is not { } sav || Hunt is not { IsFinished: false } h || !IsSameGame(h))
            return null;
        var family = ShinyHuntInfo.Family(sav, h.Species);
        var all = ShinyHuntInfo.AllShinies(sav).ToList();
        if (!h.KnowsAllShinies)
        {
            // Caçada da etapa 1: só conhecia a família do alvo; os outros shiny que já estão no save não são fases novas
            foreach (var x in all.Where(x => ShinyHuntInfo.Family(sav, x.Pk.Species) != family))
                h.KnownShinies.Add(ShinyHuntInfo.KeyOf(x.Pk));
            h.KnowsAllShinies = true;
            Save();
        }
        foreach (var hit in all)
            if (!h.KnownShinies.Contains(ShinyHuntInfo.KeyOf(hit.Pk)))
                return (hit.Pk, hit.Where, ShinyHuntInfo.Family(sav, hit.Pk.Species) == family);
        return null;
    }

    /// <summary>
    /// Resposta ao "Achei!": do alvo, conclui a caçada (e guarda nos troféus); de outra espécie, registra a fase.
    /// Recusando, só lembra do Pokémon para não perguntar de novo.
    /// </summary>
    public void Resolve(PKM pk, string where, bool isTarget, bool accept)
    {
        if (Hunt is not { } h)
            return;
        h.KnownShinies.Add(ShinyHuntInfo.KeyOf(pk));
        if (accept && isTarget)
        {
            h.FoundAt = DateTime.Now;
            h.FoundKey = ShinyHuntInfo.KeyOf(pk);
            h.MethodName = SelectedMethod?.Name ?? h.Method;
            h.FoundWhere = string.Join(" · ", new[] { ShinyHuntInfo.CaughtText(pk), where }.Where(x => x.Length > 0));
            _data.Trophies.Add(h.Copy());
        }
        else if (accept)
        {
            AddPhase(pk.Species, where);
        }
        Save();
        RaiseAll();
    }

    // Troféus
    public IReadOnlyList<HuntTrophyViewModel> Trophies => [.. _data.Trophies.AsEnumerable().Reverse().Select(t => new HuntTrophyViewModel(t, this))];
    public bool HasTrophies => _data.Trophies.Count > 0;

    /// <summary>O troféu está no save aberto? Devolve a caixa e a posição (caixa −1 = equipe).</summary>
    internal (int Box, int Slot)? FindTrophy(ShinyHunt trophy)
    {
        if (_sav is not { } sav || trophy.Version != (int)sav.Version || trophy.FoundKey.Length == 0)
            return null;
        foreach (var x in ShinyHuntInfo.AllShinies(sav))
            if (ShinyHuntInfo.KeyOf(x.Pk) == trophy.FoundKey)
                return (x.Box, x.Slot);
        return null;
    }

    internal void GoToTrophy(ShinyHunt trophy)
    {
        if (FindTrophy(trophy) is { } at)
            _goTo?.Invoke(at.Box, at.Slot);
        else
            _status("Este Pokémon não está no save aberto (abra o save do jogo da caçada).");
    }

    internal async Task RemoveTrophyAsync(ShinyHunt trophy)
    {
        if (Confirm is { } confirm && !await confirm("Remover troféu",
                string.Format(Loc.T("Remover o troféu de {0}? O Pokémon continua no save."), CoreAdapter.SpeciesNames[trophy.Species]), "Remover"))
            return;
        _data.Trophies.Remove(trophy);
        Save();
        RaiseAll();
    }

    private async Task ExportCsvAsync()
    {
        if (SaveCsv is null)
            return;
        if (await SaveCsv(ShinyHuntCsv.Encode(_data), "shiny-hunts.csv"))
            _status(string.Format(Loc.T("Caçadas exportadas: {0} troféu(s)."), _data.Trophies.Count));
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
        if (_sav is not { } sav || Hunt is not { } h)
            return;
        var species = h.Species;
        IsLoadingLocations = true;
        try
        {
            var list = await Task.Run(() => ShinyHuntInfo.Locations(sav, species));
            if (!ReferenceEquals(_sav, sav) || Hunt?.Species != species)
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

    private void Save() => ShinyHuntStore.Save(_data);

    private void RaiseCount()
    {
        foreach (var p in (string[])[nameof(Count), nameof(CountText), nameof(ChanceNow), nameof(ChanceText), nameof(OverOddsText), nameof(NextMilestoneText),
                     nameof(TimeText), nameof(PaceText), nameof(PhaseText)])
            Raise(p);
        foreach (var c in new[] { AddOneCommand, AddStepCommand, UndoCommand, ResetCommand, NewHuntCommand, LogPhaseCommand, ExportCsvCommand })
            c.NotifyCanExecuteChanged();
    }

    private void RaisePhases()
    {
        foreach (var p in (string[])[nameof(Phases), nameof(HasPhases), nameof(PhaseText)])
            Raise(p);
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
                     nameof(IsFinished), nameof(FoundText), nameof(Locations), nameof(HasLocations), nameof(NoLocations), nameof(AnyShinyLock), nameof(Trophies), nameof(HasTrophies)])
            Raise(p);
        RaisePhases();
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

/// <summary>Uma fase da caçada: em qual encontro, quantos desde a anterior, quando e qual shiny (editável).</summary>
public sealed class HuntPhaseViewModel(HuntPhase phase, int number, long previousAt, ShinyHuntPageViewModel page)
{
    public string Title => string.Format(Loc.T("Fase {0} · encontro {1:N0} ({2:N0} na fase)"), number, phase.At, phase.At - previousAt);
    public string When => phase.When.ToString("dd/MM/yyyy HH:mm");
    public string Where => phase.Where;
    public bool HasWhere => phase.Where.Length > 0;
    public IReadOnlyList<string> SpeciesNames => page.SpeciesNames;
    public string SpeciesName
    {
        get => phase.Species > 0 && phase.Species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[phase.Species] : "";
        set => page.SetPhaseSpecies(phase, value ?? "");
    }
    public RelayCommand RemoveCommand => new(() => page.RemovePhase(phase));
}

/// <summary>Uma caçada concluída: o shiny, o jogo, o método, os encontros, as fases e onde ele está.</summary>
public sealed class HuntTrophyViewModel(ShinyHunt hunt, ShinyHuntPageViewModel page)
{
    public Bitmap? Sprite => SpriteService.GetSpeciesSprite(hunt.Species, true, hunt.Form, 0, EntityContext.Gen9);
    public string Title => hunt.Species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[hunt.Species] : $"#{hunt.Species}";
    public string Game => EncounterDatabase.GetVersionName((GameVersion)hunt.Version)
        + (hunt.MethodName.Length > 0 ? " · " + Loc.T(hunt.MethodName) : "");
    public string Summary => string.Format(Loc.T("{0:N0} encontros · {1} fase(s)"), hunt.Count, hunt.Phases.Count);
    public string Date => hunt.FoundAt is { } at ? at.ToString("dd/MM/yyyy") : "";
    public string Where => hunt.FoundWhere;
    public RelayCommand GoCommand => new(() => page.GoToTrophy(hunt));
    public RelayCommand RemoveCommand => new(() => _ = page.RemoveTrophyAsync(hunt));
}
