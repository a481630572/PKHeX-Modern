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

// Natureza mantendo o shiny (Gen 3), conjuntos prontos da mochila e caixa travada. Usa uma copia do FireRed.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n} {extra}"); if (!ok) fails++; }
var frPath = Path.Combine(work, "fr.sav");
if (!File.Exists(frPath)) { Console.WriteLine("PULADO: sem fr.sav"); return 0; }

// --- Natureza mantendo o shiny, fora do editor (Gen 3: natureza = PID % 25)
var fr = CoreAdapter.LoadSave(frPath)!; CoreAdapter.Activate(fr);
var shinyCrit = EncounterCriteria.Unrestricted with { Shiny = Shiny.Always, Nature = Nature.Adamant };
// Amostra: um estatico (Method 1) que saia shiny e legal.
var pk = new ushort[] { (ushort)Species.Snorlax, (ushort)Species.Lapras, (ushort)Species.Hitmonlee, (ushort)Species.Eevee, (ushort)Species.Articuno }
    .SelectMany(sp => EncounterDatabase.SearchEncounters(fr, sp, true))
    .Select(e => EncounterDatabase.ToEntity(fr, e, shinyCrit, out string? _) as PK3)
    .First(p => p is { IsShiny: true } && new LegalityAnalysis(p).Valid)!;
Check("amostra shiny legal", pk.IsShiny && new LegalityAnalysis(pk).Valid);
var raw = (PK3)pk.Clone(); var gender = raw.Gender; var abit = raw.PID & 1;
Check("PID com natureza nova e shiny", CoreAdapter.SetShinyPIDWithNature(raw, Nature.Jolly) && raw.IsShiny && raw.Nature == Nature.Jolly && raw.Gender == gender && (raw.PID & 1) == abit);

// --- Editor no modo legal: troca a natureza, continua shiny e legal
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Func<bool> done, int ms = 60000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } Pump(); }
var open = vm.OpenAsync(frPath); Wait(() => open.IsCompleted);
var sav = (SaveFile)typeof(MainViewModel).GetField("_sav", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
sav.SetBoxSlotAtIndex(pk, 1, 0); vm.Boxes.CurrentBox = 1; vm.CurrentPage = vm.Boxes; Pump();
var t = vm.SelectSlotAsync(vm.Boxes.Slots[0]); Wait(() => t.IsCompleted);
var ed = vm.Editor!;
Check("editor no modo legal", vm.LegalMode && ed.IsLegal);
ed.Nature = (int)Nature.Timid; Wait(() => !ed.IsLegalizing);
Check("natureza trocada no modo legal", ed.Nature == (int)Nature.Timid, $"nature={ed.Nature}");
Check("continua shiny", ed.IsShiny);
Check("continua legal", ed.IsLegal);

// --- Mochila: conjuntos prontos
var bag = vm.Pages.OfType<BagPageViewModel>().First(); bag.Load(sav);
Check("Poké Balls habilitado", bag.GiveAllBallsCommand.CanExecute(null));
Check("remédios desligado na Gen 3", !bag.GiveAllMedicineCommand.CanExecute(null));
bag.GiveAllBallsCommand.Execute(null); bag.SaveCommand.Execute(null);
var balls = CoreAdapter.GetBag(sav).Pouches.First(p => p.Type == InventoryType.Balls).Items.Where(i => i.Index != 0).ToList();
Check("Poké Balls do jogo, 99 de cada, em ordem", balls.Count >= 11 && balls.All(b => b.Count == 99) && balls.Select(b => b.Index).SequenceEqual(balls.Select(b => b.Index).OrderBy(x => x)), $"n={balls.Count}");
bag.GiveAllBerriesCommand.Execute(null); bag.SaveCommand.Execute(null);
var berries = CoreAdapter.GetBag(sav).Pouches.First(p => p.Type == InventoryType.Berries).Items.Count(i => i.Index != 0);
Check("berries", berries >= 40, $"n={berries}");
bag.SelectedPouch = bag.Pouches.First(p => p.Type == InventoryType.KeyItems);
Check("itens-chave fora do 'bolso aberto'", !bag.FillCurrentPouchCommand.CanExecute(null));

// --- Caixa travada
sav.SetBoxSlotAtIndex(pk, 0, 0); sav.SetBoxSlotAtIndex(raw, 0, 1);
vm.Boxes.CurrentBox = 1; vm.Boxes.CurrentBox = 0; Pump();
var before = Enumerable.Range(0, sav.BoxSlotCount).Select(i => sav.GetBoxSlotAtIndex(0, i).Data.ToArray()).ToList();
var undoBefore = vm.UndoTip;
vm.Boxes.ToggleLockCommand.Execute(null); Pump();
Check("caixa travada", vm.Boxes.IsLocked && BoxLocks.IsLocked(sav, 0) && vm.Boxes.BoxTabs[0].IsLocked);
var src = vm.Boxes.Slots.First(s => !s.IsEmpty);
var dst = vm.Party.Slots.Count > 0 ? vm.Party.Slots[^1] : vm.Boxes.Slots.Last();
try { vm.MoveSlot(src, vm.Boxes.Slots.First(s => s.IsEmpty && s != src) ?? vm.Boxes.Slots[^1], false); } catch (BoxLockedException) { }
Pump();
bool same = Enumerable.Range(0, sav.BoxSlotCount).All(i => sav.GetBoxSlotAtIndex(0, i).Data.SequenceEqual(before[i]));
Check("mover dentro da caixa travada é recusado", same && vm.Status.Contains("travada"), vm.Status);
Check("desfazer não mudou", vm.UndoTip == undoBefore, vm.UndoTip);
var sort = vm.Boxes.SortOptions.First();
sort.SortAllCommand!.Execute(null); Pump();
same = Enumerable.Range(0, sav.BoxSlotCount).All(i => sav.GetBoxSlotAtIndex(0, i).Data.SequenceEqual(before[i]));
Check("ordenar todas pula a travada", same && vm.Status.Contains("travada"), vm.Status);
vm.UndoCommand.Execute(null); Pump();
vm.Boxes.ToggleLockCommand.Execute(null); Pump();
Check("destravada", !vm.Boxes.IsLocked && !BoxLocks.IsLocked(sav, 0));

win.Close();
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHA(S)");
return fails == 0 ? 0 : 1;
