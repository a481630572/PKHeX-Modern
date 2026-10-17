using System;
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

// Caçada shiny: chances por método, gravação da caçada, contador, ficha do alvo e o "Achei!" pelo save (saves sintéticos).
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-shiny-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups"); ShinyHuntStore.Folder = Path.Combine(work, "data");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk) { pk.Version = sav.Version; return pk; }
    throw new Exception("Sem encontro: " + species);
}
bool Near(double p, double oneIn, double tol = 0.6) => Math.Abs(1 / p - oneIn) <= tol;
double P(SaveFile sav, string key, int a = 0, int b = 0, bool charm = false) => ShinyOdds.For(sav).First(m => m.Key == key).Probability(a, b, charm);

// ---------- Chances ----------
var b2 = BlankSaveFile.Get(GameVersion.B2); var sv = BlankSaveFile.Get(GameVersion.SL); var xy = BlankSaveFile.Get(GameVersion.X);
var pt = BlankSaveFile.Get(GameVersion.Pt); var gs = BlankSaveFile.Get(GameVersion.C); var sm = BlankSaveFile.Get(GameVersion.SN);
var lgpe = BlankSaveFile.Get(GameVersion.GP); var swsh = BlankSaveFile.Get(GameVersion.SW);
Check("base 1/8192 na Gen 5 e 1/4096 no SV", Near(P(b2, "full"), 8192) && Near(P(sv, "full"), 4096));
Check("Shiny Charm: 3 tentativas", Near(P(sv, "full", charm: true), 1365.7));
Check("Masuda + Charm no SV: 1/512", Near(P(sv, "masuda", charm: true), 512.4));
Check("surto 60+ e sanduíche 3 com Charm: 1/512", Near(P(sv, "outbreak9", 60, 3, true), 512.4));
Check("Masuda na Gen 4: 5 tentativas", Near(P(pt, "masuda"), 1638.8, 1));
Check("Poké Radar corrente 40: ~1/200", Near(P(pt, "radar4", 40), 199.8));
Check("ovo com pai shiny na Gen 2: 1/64", Near(P(gs, "g2breed"), 64));
Check("pesca em corrente 20: 41 tentativas", Near(P(xy, "fishing", 20), 100.4));
Check("SOS 31+ com Charm: 15 tentativas", Near(P(sm, "sos", 31, 0, true), 273.6));
Check("Catch Combo 31+, Lure e Charm: 15 tentativas", Near(P(lgpe, "combo", 31, 1, true), 273.6));
Check("Dynamax Adventure com Charm: 1/100", Near(P(swsh, "dynamax", 0, 0, true), 100));
Check("encontros para 50% em 1/4096", ShinyOdds.EncountersFor(1.0 / 4096, 0.5) == 2839);
Check("Gen 1 sem métodos", ShinyOdds.For(BlankSaveFile.Get(GameVersion.RD)).Count == 0);
Check("Shiny Charm: só nos jogos que têm o item", ShinyOdds.HasCharmItem(b2) && !ShinyOdds.HasCharmItem(BlankSaveFile.Get(GameVersion.B)) && !ShinyOdds.SaveHasCharm(b2));

// ---------- Golpes perigosos e locais ----------
var bw = BlankSaveFile.Get(GameVersion.B); bw.OT = "Demo"; CoreAdapter.Activate(bw);
var boom = Make(bw, 504); boom.Move1 = (ushort)Move.Explosion; boom.Move2 = (ushort)Move.Roar; boom.Move3 = (ushort)Move.Tackle;
var risks = ShinyHuntInfo.RisksOf(boom);
Check("golpes perigosos com o que fazer", risks.Count == 2 && risks[0].Move == "Explosion" && risks[0].Counter.Contains("Damp") && risks[1].Danger.Contains("acaba"));
var places = ShinyHuntInfo.Locations(bw, 504);
Check("locais do alvo no jogo", places.Count > 0 && places.All(l => l.Levels.StartsWith("Nv.")) && places.Any(l => l.Location.Contains("Route")));
CoreAdapter.Activate(pt);
var geodude = ShinyHuntInfo.Locations(pt, 74);
Console.WriteLine("   Geodude Pt: " + string.Join(" | ", geodude.Select(l => $"{l.Location} {l.Levels} [{string.Join(",", l.Risks.Select(r => r.Move))}]")));
Check("dados reais: Geodude com Self-Destruct em algum lugar do Platinum", geodude.Any(l => l.Risks.Any(r => r.Move is "Self-Destruct" or "Selfdestruct" or "Explosion")));
CoreAdapter.Activate(bw);
Check("família pela forma básica", ShinyHuntInfo.Family(bw, 505) == 504);

// ---------- Página e "Achei!" ----------
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
var old = Make(bw, 505); old.SetShiny(); bw.SetBoxSlotAtIndex(old, 0, 0); // shiny da família que já estava no save
var first = Path.Combine(lib, "Black.sav"); File.WriteAllBytes(first, bw.Write().ToArray());
var vm = new MainViewModel(new AppSettings { SavesFolder = lib, CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1400, Height = 950 }; win.Show();
ConfirmDialogViewModel? lastDialog = null;
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Run(Func<Task> action, bool answer = true)
{
    var t = action();
    for (int i = 0; i < 3000 && !t.IsCompleted; i++) { Pump(2); if (vm.Dialog is { } d) { lastDialog = d; d.Complete(answer); } System.Threading.Thread.Sleep(2); }
    Pump(20);
    if (vm.Dialog is { } late) { lastDialog = late; late.Complete(answer); Pump(20); }
}
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
vm.Open(first); Pump();
var hunt = vm.ShinyHunt;
Check("página no fim da barra lateral, sem atalho", vm.Pages.Last() == hunt && hunt.Shortcut == "" && PKHeX.Modern.Controls.LineIcon.HasPath(hunt.Icon));
vm.CurrentPage = hunt; Pump();
hunt.TargetName = "Patrat"; Pump();
Check("caçada começa pelo nome", hunt.HasHunt && hunt.TargetName == "Patrat" && hunt.Count == 0 && hunt.Methods.Count == 2 && !hunt.CanUseCharm);
for (int i = 0; i < 12; i++) hunt.CountKey(true);
hunt.Step = 5; hunt.AddStepCommand.Execute(null);
hunt.CountKey(false);
Check("contador: +1, somar e desfazer", hunt.Count == 12 && hunt.CountText == "12");
Check("chances na tela", hunt.OddsText == "1/8192" && hunt.MilestonesText.Contains("5.678") is var _ && hunt.NextMilestoneText.Contains("50%"));
hunt.SelectedMethod = hunt.Methods[1]; Pump();
Check("trocar o método muda a chance", hunt.OddsText == "1/1365.8" || hunt.OddsText == "1/1365,8");
for (int i = 0; i < 300 && hunt.IsLoadingLocations; i++) { Pump(2); System.Threading.Thread.Sleep(10); }
Check("ficha do alvo carregada", hunt.HasLocations && !hunt.AnyShinyLock);
Shot("shinyhunt");
var saved = ShinyHuntStore.Load();
Check("caçada gravada", saved is { Species: 504, Count: 12, Method: "masuda" } && saved.KnownShinies.Count == 1);

// Outro arquivo do mesmo jogo com um Patrat shiny novo: o "Achei!" pergunta e conclui
var found = Make(bw, 504); found.SetShiny(); bw.SetBoxSlotAtIndex(found, 0, 1);
var second = Path.Combine(lib, "Black2nd.sav"); File.WriteAllBytes(second, bw.Write().ToArray());
Run(() => vm.OpenAsync(second));
Check("Achei!: só o shiny novo, e conclui", lastDialog?.Message.Contains("Patrat") == true && hunt.IsFinished && hunt.FoundText.Contains("12"));
Check("concluída: contador parado", !hunt.AddOneCommand.CanExecute(null));
lastDialog = null; Run(() => vm.OpenAsync(first));
Check("não pergunta de novo", lastDialog is null);

// A página guarda a caçada entre execuções
var reloaded = new ShinyHuntPageViewModel(_ => { }); reloaded.Load(bw);
Check("caçada volta ao abrir o app", reloaded.HasHunt && reloaded.IsFinished && reloaded.Count == 12);
reloaded.NewHuntCommand.Execute(null);
Check("nova caçada limpa", !reloaded.HasHunt && ShinyHuntStore.Load() is null);

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
