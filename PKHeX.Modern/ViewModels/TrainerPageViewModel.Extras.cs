using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Insignias, genero, moedas, BP e datas do treinador (mudam de jogo para jogo; ver <see cref="TrainerExtras"/>).</summary>
public sealed partial class TrainerPageViewModel
{
    public IReadOnlyList<BadgeGroupViewModel> BadgeGroups { get; private set; } = [];
    public bool HasBadges => BadgeGroups.Count > 0;

    private void LoadExtras(SaveFile sav)
    {
        BadgeGroups = [.. TrainerExtras.GetBadges(sav).Select(g => new BadgeGroupViewModel(g, () => Changed?.Invoke()))];
    }

    // Genero
    public bool HasGender => _sav is not null && TrainerExtras.HasGender(_sav);
    public IReadOnlyList<string> Genders { get; } = ["♂ Masculino", "♀ Feminino"];
    public int GenderIndex
    {
        get => _sav?.Gender is 1 ? 1 : 0;
        set { if (_sav is null || value is < 0 or > 1 || value == GenderIndex) return; _sav.Gender = (byte)value; Changed?.Invoke(); Raise(); }
    }

    // Moedas e BP
    public bool HasCoins => _sav is not null && TrainerExtras.HasCoins(_sav);
    public int MaxCoins => _sav?.MaxCoins ?? 0;
    public decimal Coins
    {
        get => _sav is null ? 0 : TrainerExtras.GetCoins(_sav);
        set { if (_sav is null) return; TrainerExtras.SetCoins(_sav, (uint)Math.Clamp(value, 0, MaxCoins)); Changed?.Invoke(); Raise(); }
    }
    public bool HasBP => _sav is not null && TrainerExtras.HasBP(_sav);
    public decimal BP
    {
        get => _sav is null ? 0 : TrainerExtras.GetBP(_sav);
        set { if (_sav is null) return; TrainerExtras.SetBP(_sav, (int)value); Changed?.Invoke(); Raise(); }
    }
    public bool HasCurrency => HasCoins || HasBP;

    // Datas (Gen 4 e 5)
    public bool HasDates => _sav is not null && TrainerExtras.HasDates(_sav);
    public DateTime? AdventureStart
    {
        get => HasDates ? TrainerExtras.GetDate(_sav!.SecondsToStart) : null;
        set { if (!HasDates || value is not { } d) return; _sav!.SecondsToStart = TrainerExtras.ToSeconds(d.Date + AdventureStart!.Value.TimeOfDay); Changed?.Invoke(); Raise(); }
    }
    public DateTime? FameDate
    {
        get => HasDates ? TrainerExtras.GetDate(_sav!.SecondsToFame) : null;
        set { if (!HasDates || value is not { } d) return; _sav!.SecondsToFame = TrainerExtras.ToSeconds(d.Date + FameDate!.Value.TimeOfDay); Changed?.Invoke(); Raise(); }
    }
}

public sealed class BadgeGroupViewModel : ViewModelBase
{
    private readonly BadgeGroup _group;
    private readonly Action _changed;

    public BadgeGroupViewModel(BadgeGroup group, Action changed)
    {
        _group = group; _changed = changed;
        Badges = [.. group.Names.Select((name, i) => new BadgeViewModel(name, i, this))];
    }

    public string Region => _group.Region;
    public IReadOnlyList<BadgeViewModel> Badges { get; }
    public string Summary => $"{Badges.Count(b => b.Has)}/{Badges.Count}";
    public RelayCommand AllCommand => new(() => SetAll(true));
    public RelayCommand NoneCommand => new(() => SetAll(false));

    private void SetAll(bool value)
    {
        for (int i = 0; i < Badges.Count; i++) _group.Set(i, value);
        foreach (var b in Badges) b.Refresh();
        _changed(); Raise(nameof(Summary));
    }

    internal bool Get(int index) => _group.Get(index);
    internal void Set(int index, bool value) { _group.Set(index, value); _changed(); Raise(nameof(Summary)); }
}

public sealed class BadgeViewModel(string name, int index, BadgeGroupViewModel owner) : ViewModelBase
{
    public string Name => name;
    public bool Has { get => owner.Get(index); set { if (value == Has) return; owner.Set(index, value); Raise(); } }
    internal void Refresh() => Raise(nameof(Has));
}
