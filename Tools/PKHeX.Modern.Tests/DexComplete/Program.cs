using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Completar/limpar a Pokedex do save pela pagina Pokedex, da Gen 1 a 9 (copias dos saves reais + BD/SP sintetico):
// grava, reabre e confere; Ctrl+Z desfaz; Legends: Arceus nao oferece.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-dex-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false, SavesFolder = Path.Combine(work, "vazia") });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int k = 10) { for (int i = 0; i < k; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms = 20000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } return done(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
SaveFile Reopen(string file)
{
    var output = Path.Combine(work, "out-" + file);
    Check($"[{file}] exportou", vm.Export(output));
    var sav = CoreAdapter.LoadSave(output)!;
    Check($"[{file}] checksums válidos depois de gravar", sav.ChecksumsValid, sav.ChecksumInfo);
    return sav;
}
SaveFile Active() => vm.ActiveTab!.Sav;
var dex = vm.Pokedex;

var bd = (SAV8BS)BlankSaveFile.Get(GameVersion.BD); bd.OT = "Demo";
File.WriteAllBytes(Path.Combine(work, "bd.sav"), bd.Write().ToArray());

foreach (var file in new[] { "red.sav", "cr.sav", "em.sav", "hg.sav", "bw.sav", "y.sav", "moon.sav", "sh.sav", "bd.sav", "sv.sav", "za.sav" })
{
    if (!File.Exists(Path.Combine(work, file))) { Console.WriteLine($"     [{file}] sem o save: pulado"); continue; }
    vm.Open(Path.Combine(work, file)); Pump();
    vm.CurrentPage = dex; Pump();
    var sav = Active();
    Check($"[{file}] oferece completar a Pokédex", dex.CanEditDex && dex.HasShinyOption == (sav.Generation >= 5));
    if (file == "za.sav") // este save ja tem a Pokedex de Lumiose completa: limpa antes para conferir o completar
    {
        var full = DexCompletion.Count(sav);
        dex.DexActionCommand.Execute("Clear");
        Check("[za.sav] limpou para testar", Wait(() => DexCompletion.Count(Active()).Seen == 0), $"antes {full}");
    }
    var before = DexCompletion.Count(sav);
    dex.IncludeShiny = sav.Generation >= 5;
    dex.DexActionCommand.Execute("Complete");
    Check($"[{file}] completou", Wait(() => DexCompletion.Count(Active()).Caught > before.Caught || before.Caught == sav.MaxSpeciesID));
    var after = DexCompletion.Count(Active());
    Console.WriteLine($"     [{file}] antes {before.Seen}/{before.Caught}, depois {after.Seen}/{after.Caught} de {sav.MaxSpeciesID}");
    if (sav.Generation <= 7)
        Check($"[{file}] todas vistas e capturadas", after.Seen == sav.MaxSpeciesID && after.Caught == sav.MaxSpeciesID);
    else
        Check($"[{file}] mais capturadas que antes", after.Caught > before.Caught && after.Seen >= after.Caught);
    if (file == "hg.sav") Shot("dex-hg");
    var saved = Reopen(file);
    Check($"[{file}] Pokédex gravada", DexCompletion.Count(saved) == after, $"{DexCompletion.Count(saved)}");

    if (file == "y.sav")
    {
        vm.UndoCommand.Execute(null); Pump();
        Check("[y.sav] Ctrl+Z na Pokédex volta como era", DexCompletion.Count(Active()) == before, $"{DexCompletion.Count(Active())}");
        vm.RedoCommand.Execute(null); Pump();
        Check("[y.sav] Ctrl+Y refaz", DexCompletion.Count(Active()) == after);
        dex.DexActionCommand.Execute("CaughtNone");
        Wait(() => DexCompletion.Count(Active()).Caught == 0);
        Check("[y.sav] tirar as capturadas mantém as vistas", DexCompletion.Count(Active()) is { Caught: 0 } c && c.Seen == after.Seen, $"{DexCompletion.Count(Active())}");
        dex.DexActionCommand.Execute("Clear");
        Wait(() => DexCompletion.Count(Active()).Seen == 0);
        Check("[y.sav] limpar zera vistas e capturadas", DexCompletion.Count(Active()) == (0, 0), $"{DexCompletion.Count(Active())}");
    }
}

// Legends: Arceus (sintetico): Pokedex de pesquisa por tarefas, sem "completar tudo" no PKHeX.
var pla = BlankSaveFile.Get(GameVersion.PLA);
var plaDex = new PokedexPageViewModel(new AppSettings(), (_, _) => { }); plaDex.Load(pla);
Check("[Legends: Arceus] não oferece completar (pesquisa por tarefas)", !plaDex.CanEditDex && !DexCompletion.IsSupported(pla));

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
