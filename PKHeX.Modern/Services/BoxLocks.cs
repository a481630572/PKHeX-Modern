using System;
using System.Collections.Generic;
using System.IO;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Caixas travadas: nada muda nelas (arrastar, excluir, aplicar, ordenar, colar, lote, Living Dex...) ate destravar.
/// Guardadas nas preferencias, por arquivo de save. Quem garante e o <see cref="SlotHistory.Record"/>, por onde toda
/// alteracao de slot passa antes de gravar: com uma caixa travada no meio, ele avisa e recusa a operacao inteira.
/// </summary>
public static class BoxLocks
{
    /// <summary>Preferencias onde as travas ficam (definidas pelo MainViewModel).</summary>
    public static AppSettings? Settings { get; set; }

    /// <summary>Uma operacao foi recusada por causa de uma caixa travada (mensagem para a barra de status).</summary>
    public static Action<string>? Report { get; set; }

    private static string? KeyOf(SaveFile sav)
    {
        var path = sav.Metadata.FilePath;
        if (string.IsNullOrEmpty(path))
            return null;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static List<int>? Get(SaveFile sav, bool create)
    {
        if (Settings is null || KeyOf(sav) is not { } key)
            return null;
        if (Settings.LockedBoxes.TryGetValue(key, out var list) || !create)
            return list;
        return Settings.LockedBoxes[key] = [];
    }

    public static bool IsLocked(SaveFile sav, int box) => box >= 0 && Get(sav, false)?.Contains(box) == true;

    /// <summary>Da para travar (o save tem arquivo; um save sem caminho nao guarda a trava).</summary>
    public static bool CanLock(SaveFile sav) => Settings is not null && KeyOf(sav) is not null;

    public static void Set(SaveFile sav, int box, bool locked)
    {
        if (Get(sav, locked) is not { } list || list.Contains(box) == locked)
            return;
        if (locked)
            list.Add(box);
        else
            list.Remove(box);
        if (list.Count == 0 && KeyOf(sav) is { } key)
            Settings!.LockedBoxes.Remove(key);
        Settings!.Save();
    }

    internal static BoxLockedException Block(SaveFile sav, int box)
    {
        var message = $"A caixa {CoreAdapter.GetBoxName(sav, box)} está travada: destrave em “⋯ Caixa” para mudar algo nela.";
        Report?.Invoke(message);
        return new BoxLockedException(message);
    }
}

/// <summary>Operacao recusada porque mexeria numa caixa travada (o aviso ja foi dado pela barra de status).</summary>
public sealed class BoxLockedException(string message) : InvalidOperationException(message);
