using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Atalhos "liberar tudo" da pagina Jogo: O-Powers e Super Training (copia do Y), Zygarde Cells (copias do Moon e do
// Ultra Moon), itens do Grand Underground (BD/SP sintetico) e esferas do Underground (Platinum sintetico, em memoria).
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-unlock-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int k = 10) { for (int i = 0; i < k; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms = 10000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } return done(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
SaveFile Reopen(string file)
{
    var output = Path.Combine(work, "out-" + file);
    Check($"[{file}] exportou", vm.Export(output));
    var sav = CoreAdapter.LoadSave(output)!;
    Check($"[{file}] checksums válidos depois de gravar", sav.ChecksumsValid, sav.ChecksumInfo);
    return sav;
}
var g = vm.Game;
ShortcutRowViewModel Run(string file, string name)
{
    g.Tab = 3; Pump();
    var row = g.ShortcutRows.FirstOrDefault(r => r.Name == name);
    Check($"[{file}] atalho “{name}” disponível", row is { IsReady: true }, string.Join(", ", g.ShortcutRows.Select(r => r.Name)));
    if (row is null) return null!;
    row.ApplyCommand.Execute(null);
    Check($"[{file}] “{name}” aplicado", Wait(() => !row.IsReady), row.State);
    return row;
}

// ---------- Pokémon Y: O-Powers e Super Training ----------
vm.Open(Path.Combine(work, "y.sav")); Pump();
vm.CurrentPage = g; Pump();
Run("y.sav", "Todos os O-Powers");
Run("y.sav", "Todos os treinos do Super Training");
Shot("shortcuts-y");
var y = (SAV6XY)Reopen("y.sav");
Check("[Y] O-Powers liberados e no nível 3", Enumerable.Range(0, (int)OPower6Index.Count).All(i => y.OPower.GetState((OPower6Index)i) == OPowerFlagState.Unlocked)
    && y.OPower.GetLevel1(OPower6FieldType.Encounter) == 3);
Check("[Y] treinos liberados", Enumerable.Range(0, 32).All(y.SuperTrain.GetIsRegimenUnlocked) && Enumerable.Range(0, 6).All(y.SuperTrain.GetIsDistributionUnlocked));
g.UndoGame(); Pump();
Check("[Y] desfazer volta os treinos", g.ShortcutRows.First(r => r.Name == "Todos os treinos do Super Training").IsReady);

// ---------- Moon e Ultra Moon: Zygarde Cells ----------
foreach (var (file, total) in new[] { ("moon.sav", 95), ("um.sav", 100) })
{
    vm.Open(Path.Combine(work, file)); Pump();
    vm.CurrentPage = g; Pump();
    var before = (SAV7)CoreAdapter.LoadSave(Path.Combine(work, file))!;
    int missing = Enumerable.Range(0, total).Count(i => before.EventWork.GetZygardeCell(i) != 2);
    Console.WriteLine($"     [{file}] faltavam {missing} células; contador {before.EventWork.ZygardeCellCount}");
    if (missing == 0) continue;
    Run(file, "Todas as Zygarde Cells");
    var s7 = (SAV7)Reopen(file);
    Check($"[{file}] {total} células coletadas e contador somado", Enumerable.Range(0, total).All(i => s7.EventWork.GetZygardeCell(i) == 2)
        && s7.EventWork.ZygardeCellCount == before.EventWork.ZygardeCellCount + missing, $"contador {s7.EventWork.ZygardeCellCount}");
    if (s7 is SAV7USUM)
        Check($"[{file}] recorde 72 acompanha o contador", s7.GetRecord(72) == s7.EventWork.ZygardeCellCount);
    else
        Check($"[{file}] total de células somado", s7.EventWork.ZygardeCellTotal == before.EventWork.ZygardeCellTotal + missing);
}

// ---------- BD/SP sintetico: itens do Grand Underground ----------
var bd = (SAV8BS)BlankSaveFile.Get(GameVersion.BD); bd.OT = "Demo";
File.WriteAllBytes(Path.Combine(work, "bd.sav"), bd.Write().ToArray());
vm.Open(Path.Combine(work, "bd.sav")); Pump();
vm.CurrentPage = g; Pump();
Run("bd.sav", "Itens do Grand Underground no máximo");
var bdSaved = (SAV8BS)Reopen("bd.sav");
var items = bdSaved.Underground.ReadItems().Where(i => i.Type != UgItemType.None).ToList();
Check("[BD] itens do Grand Underground no máximo", items.Count > 0 && items.All(i => i.Count == i.MaxValue), $"{items.Count} itens");

// ---------- Platinum sintetico (em memoria): esferas do Underground ----------
var pt = (SAV4Pt)BlankSaveFile.Get(GameVersion.Pt);
var gp = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); gp.Load(pt);
var spheres = gp.ShortcutRows.First(r => r.Name == "Esferas do Underground cheias");
spheres.ApplyCommand.Execute(null);
Wait(() => !spheres.IsReady, 3000);
var sp = pt.GetUGI_Spheres().ToArray();
Check("[Platinum] 40 esferas grandes de tamanho 99", Enumerable.Range(0, SAV4Sinnoh.UG_POUCH_SIZE).All(i => sp[i] is >= 6 and <= 10 && sp[SAV4Sinnoh.UG_POUCH_SIZE + i] == 99));

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
