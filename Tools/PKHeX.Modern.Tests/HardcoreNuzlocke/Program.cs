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

// Modo Hardcore (so mover) e relatorio de Nuzlocke, com saves sinteticos.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-hardcore-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk) return pk;
    throw new Exception("Sem encontro: " + species);
}

// ---------- Nuzlocke ----------
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "Demo"; CoreAdapter.Activate(sav);
var probe = Make(sav, 504);
ushort Loc(string name) => CoreAdapter.GetMetLocations(probe).First(x => x.Text == name).Value is var v ? (ushort)v : (ushort)0;
PKM At(ushort species, string route, int day)
{
    var pk = Make(sav, species);
    pk.Version = sav.Version; // o encontro pode vir do Black 2 (mesmo contexto); o relatorio so conta os deste jogo
    pk.MetLocation = Loc(route);
    pk.MetDate = new DateOnly(2012, 3, day);
    return pk;
}
sav.SetPartySlotAtIndex(At(504, "Route 1", 1), 0);          // Patrat: primeiro da Route 1, na equipe
sav.SetBoxSlotAtIndex(At(506, "Route 1", 2), 0, 0);         // Lillipup: captura a mais na Route 1
sav.SetBoxSlotAtIndex(At(505, "Route 2", 3), 0, 1);         // Watchog: primeiro da Route 2, repetido (familia do Patrat)
sav.SetBoxSlotAtIndex(At(509, "Route 3", 4), 1, 0);         // Purrloin: na caixa do cemiterio
var traded = At(519, "Route 3", 5); traded.OriginalTrainerName = "Outro"; traded.ID32 = 1234567;
sav.SetBoxSlotAtIndex(traded, 0, 2);                        // de troca: fica de fora
var egg = At(495, "Route 1", 6); egg.IsEgg = true;
sav.SetBoxSlotAtIndex(egg, 0, 3);                           // ovo: fica de fora
((IBoxDetailName)sav).SetBoxName(1, "Cemitério");

Check("cemitério pelo nome da caixa", NuzlockeReport.GuessGraveyard(sav) == 1);
var r = NuzlockeReport.Build(sav, 1);
Check("três rotas na ordem das datas", r.Unsupported is null && string.Join(",", r.Routes.Select(x => x.Route)) == "Route 1,Route 2,Route 3");
Check("Route 1: Patrat primeiro, Lillipup a mais", r.Routes[0].First.Pkm.Species == 504 && r.Routes[0].Extras.Count == 1 && r.Routes[0].Extras[0].Pkm.Species == 506);
Check("Route 2: Watchog repetido da Route 1", r.Routes[1].IsDupe && r.Routes[1].DupeOf == "Route 1");
Check("situação: equipe, vivo e morto", r.Routes[0].First.Status == NuzlockeStatus.Party && r.Routes[1].First.Status == NuzlockeStatus.Alive && r.Routes[2].First.Status == NuzlockeStatus.Dead);
Check("contagens", r.Alive == 2 && r.InParty == 1 && r.Dead == 1 && r.Dupes == 1 && r.Extras == 1 && r.HasDates);
Check("troca e ovo de fora", r.Outsiders.Count == 1 && r.Outsiders[0].Pkm.Species == 519 && r.Eggs == 1);
var text = NuzlockeReport.ToText(r, "Black");
Check("texto para colar", text.Contains("Route 1: Patrat") && text.Contains("repetido (Route 1)") && text.Contains("+1 a mais: Lillipup") && text.Contains("Morto"));
Check("sem cemitério ninguém morre", NuzlockeReport.Build(sav, -1).Dead == 0);
Check("Gen 1 explica que não dá", NuzlockeReport.Build(BlankSaveFile.Get(GameVersion.RD), -1).Unsupported is { Length: > 0 });
var em = BlankSaveFile.Get(GameVersion.E); em.OT = "Demo"; CoreAdapter.Activate(em);
PKM Em(ushort sp) { var pk = Make(em, sp); pk.Version = em.Version; return pk; }
em.SetBoxSlotAtIndex(Em(263), 0, 0); em.SetBoxSlotAtIndex(Em(261), 0, 1);
var rem = NuzlockeReport.Build(em, -1);
Check("Gen 3: sem data, ordem das caixas", rem.Unsupported is null && !rem.HasDates && rem.Routes.Count >= 1 && rem.Routes[0].First.Pkm.Species == 263);

// ---------- Interface ----------
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
var black = Path.Combine(lib, "Black.sav"); CoreAdapter.Activate(sav); File.WriteAllBytes(black, sav.Write().ToArray());
var settings = new AppSettings { SavesFolder = lib, CheckForUpdates = false };
var vm = new MainViewModel(settings);
string? copied = null;
var win = new MainWindow { DataContext = vm, Width = 1400, Height = 900 }; win.Show(); // antes dos ganchos: a janela liga a area de transferencia de verdade
vm.ReadClipboard = () => Task.FromResult<string?>("Pikachu @ Light Ball\nAbility: Static\n- Thunderbolt");
vm.WriteClipboard = t => { copied = t; return Task.CompletedTask; };
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Task t) { for (int i = 0; i < 4000 && !t.IsCompleted; i++) { Pump(2); System.Threading.Thread.Sleep(2); } t.GetAwaiter().GetResult(); Pump(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
vm.Open(black); Pump();

// Aba Nuzlocke da Pokedex
vm.CurrentPage = vm.Pokedex; vm.Pokedex.ShowNuzlockeCommand.Execute(null); Pump();
var nz = vm.Pokedex.Nuzlocke;
Check("aba Nuzlocke: cemitério já escolhido e três rotas", vm.Pokedex.ShowNuzlocke && !vm.Pokedex.ShowCollection && nz.GraveyardIndex == 2 && nz.Routes.Count == 3);
Check("aba Nuzlocke: resumo e aviso dos de fora", nz.Summary.StartsWith("3 rotas · 2 vivos") && nz.Note.Contains("1 Pokémon de trocas"));
nz.GraveyardIndex = 0; Pump();
Check("trocar a caixa do cemitério recalcula", nz.Result!.Dead == 0);
nz.GraveyardIndex = 2;
nz.CopyCommand.Execute(null); Pump();
Console.WriteLine("debug copied=" + (copied ?? "(null)") + " status=" + vm.Status);
Check("copiar relatório", copied?.StartsWith("Nuzlocke · ") == true && copied.Contains("Route 3: Purrloin"));
Shot("nuzlocke");
nz.Routes[1].GoCommand.Execute(null); Pump();
Check("Ir abre o Pokémon no save", vm.Editor?.SpeciesName == "Watchog");
vm.Pokedex.ShowCollectionCommand.Execute(null);

// Modo Hardcore
vm.CurrentPage = vm.Pages.First(p => p is GamePageViewModel);
int pagesBefore = vm.Pages.Count;
vm.Hardcore = true; Pump();
Check("Hardcore: páginas de edição saem da barra lateral", vm.Pages.Count == pagesBefore - 4
    && !vm.Pages.Any(p => p is TrainerPageViewModel or BagPageViewModel or GamePageViewModel or BatchPageViewModel) && vm.CurrentPage == vm.Home);
Check("Hardcore: preferência gravada", settings.Hardcore);
vm.CurrentPage = vm.Boxes; Pump();
Wait(vm.SelectSlotAsync(vm.Boxes.Slots[0]));
Check("Hardcore: editor só leitura", vm.Editor is { ReadOnly: true, CanApply: false, CanToggleShiny: false } e && !e.ApplyCommand.CanExecute(null));
int Stored() => Enumerable.Range(0, vm.Boxes.Slots.Count).Count(i => !vm.Boxes.Slots[i].IsEmpty);
int before = Stored();
Wait(vm.MoveSlotAsync(vm.Boxes.Slots[0], vm.Boxes.Slots[10], true));
Check("Hardcore: copiar bloqueado", Stored() == before && vm.Status.Contains("Modo Hardcore"));
Wait(vm.MoveSlotAsync(vm.Boxes.Slots[0], vm.Boxes.Slots[10], false));
Check("Hardcore: mover continua", Stored() == before && vm.Boxes.Slots[0].IsEmpty && !vm.Boxes.Slots[10].IsEmpty);
vm.CreateCommand.Execute(null); Pump();
Check("Hardcore: criar bloqueado", vm.Status.Contains("Modo Hardcore") && Stored() == before);
int party = vm.Party.Slots.Count(s => !s.IsEmpty);
Wait(vm.PasteShowdownTeamAsync(true));
Check("Hardcore: colar Showdown bloqueado", vm.Party.Slots.Count(s => !s.IsEmpty) == party);
var pkFile = Path.Combine(work, "x.pk5"); File.WriteAllBytes(pkFile, Make(sav, 25).Data.ToArray());
Wait(vm.ImportFileAsync(pkFile));
Check("Hardcore: importar bloqueado", Stored() == before);
Check("Hardcore: Pokédex sem completar nem sincronizar", !vm.Pokedex.CanEditDex && !vm.Pokedex.SyncCommand.CanExecute(null));
Shot("hardcore");
vm.Hardcore = false; Pump();
Wait(vm.SelectSlotAsync(vm.Boxes.Slots[10]));
Check("desligar: páginas e editor voltam", vm.Pages.Count == pagesBefore && vm.Editor is { ReadOnly: false });


// ---------- Fabrica de ovos ----------
var eggSav = BlankSaveFile.Get(GameVersion.B); eggSav.OT = "Demo"; CoreAdapter.Activate(eggSav);
var plan = EggFactory.Plan(eggSav, onlyMissing: true);
Check("plano: ovo da forma básica para cada espécie que falta", plan.Count > 400 && plan.Any(x => x.For == 2 && x.Species == 1) && plan.Any(x => x.For == 25 && x.Species == 172));
Check("plano: lendários ficam de fora", !plan.Any(x => x.For is 150 or 151 or 249));
Check("plano por família: só formas básicas", EggFactory.Plan(eggSav, onlyMissing: false).All(x => x.For == x.Species));
var sw = System.Diagnostics.Stopwatch.StartNew();
var made = EggFactory.Make(eggSav, [.. plan.Where(x => x.For is 1 or 2 or 4 or 25)], requireLegal: true);
Console.WriteLine($"   4 ovos em {sw.ElapsedMilliseconds} ms");
Check("ovos legais gerados", made.Count == 4 && made.All(r => r.Egg is { IsEgg: true } e && new LegalityAnalysis(e).Valid)
    && made[1].Egg!.Species == 1 && made[3].Egg!.Species == 172);
var emEggs = EggFactory.Make(em, [.. EggFactory.Plan(em, onlyMissing: false).Take(3)], requireLegal: true);
Check("Gen 3: ovo com o local de eclosão e nível 0", emEggs.All(x => x.Egg is { IsEgg: true, MetLevel: 0 } e && new LegalityAnalysis(e).Valid));
Check("Gen 1 não tem ovos", !EggFactory.IsSupported(BlankSaveFile.Get(GameVersion.RD)));

// Pela interface: caixas quase cheias (3 vagas), confirma a lista e grava com um desfazer
var filler = Make(eggSav, 504);
for (int b = 0; b < eggSav.BoxCount; b++)
    for (int sl = 0; sl < eggSav.BoxSlotCount; sl++)
        if (!(b == 0 && sl >= eggSav.BoxSlotCount - 3))
            eggSav.SetBoxSlotAtIndex(filler, b, sl);
var eggPath = Path.Combine(lib, "Eggs.sav"); File.WriteAllBytes(eggPath, eggSav.Write().ToArray());
vm.Open(eggPath); Pump();
ConfirmDialogViewModel? lastDialog = null;
void Run(Task t)
{
    for (int i = 0; i < 30000 && !t.IsCompleted; i++)
    {
        Pump(2);
        if (vm.Dialog is { } d) { lastDialog = d; d.Complete(true); }
        System.Threading.Thread.Sleep(2);
    }
    t.GetAwaiter().GetResult(); Pump();
}
int Eggs() => Enumerable.Range(0, eggSav.BoxSlotCount).Count(i => vm.ActiveTab!.Sav.GetBoxSlotAtIndex(0, i) is { IsEgg: true });
vm.Hardcore = true;
Run(vm.EggFactoryAsync(true));
Check("Hardcore: fábrica de ovos bloqueada", lastDialog is null && vm.Status.Contains("Modo Hardcore") && Eggs() == 0);
vm.Hardcore = false;
vm.Boxes.CurrentBox = 0;
Run(vm.EggFactoryAsync(true));
Check("fábrica: lista confirmada e 3 ovos nas vagas", lastDialog?.Details.Count(d => d.StartsWith("🥚")) == 3 && Eggs() == 3 && vm.IsDirty);
vm.UndoCommand.Execute(null); Pump();
Check("fábrica: Ctrl+Z tira os ovos de uma vez", Eggs() == 0);
Check("Pokédex mostra a fábrica no save aberto", vm.Pokedex.CanMakeEggs && vm.Pokedex.MissingEggsCommand is not null);

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
