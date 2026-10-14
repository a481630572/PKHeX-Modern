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

// Pagina Treinador: insignias (Gen 1 a 8), genero, moedas do Game Corner, BP e datas da Gen 4/5. Copias dos saves
// reais + BD/SP sintetico; grava, reabre e confere.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-trainer-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int k = 10) { for (int i = 0; i < k; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
SaveFile Reopen(string file)
{
    var output = Path.Combine(work, "out-" + file);
    Check($"[{file}] exportou", vm.Export(output));
    var sav = CoreAdapter.LoadSave(output)!;
    Check($"[{file}] checksums válidos depois de gravar", sav.ChecksumsValid, sav.ChecksumInfo);
    return sav;
}
var trainer = (TrainerPageViewModel)vm.Pages.First(p => p is TrainerPageViewModel);

var bd = (SAV8BS)BlankSaveFile.Get(GameVersion.BD); bd.OT = "Demo";
File.WriteAllBytes(Path.Combine(work, "bd.sav"), bd.Write().ToArray());

// arquivo, regioes esperadas, moedas?, BP?, datas?
var cases = new (string File, string[] Regions, bool Coins, bool BP, bool Dates)[]
{
    ("red.sav", ["Kanto"], true, false, false),
    ("cr.sav", ["Johto", "Kanto"], true, false, false),
    ("em.sav", ["Hoenn"], true, true, false),
    ("fr.sav", ["Kanto"], true, false, false),
    ("hg.sav", ["Johto", "Kanto"], true, true, true),
    ("bw.sav", ["Unova"], false, true, true),
    ("y.sav", ["Kalos"], false, true, false),
    ("or.sav", ["Hoenn"], false, true, false),
    ("moon.sav", [], false, true, false),
    ("sh.sav", ["Galar"], false, true, false),
    ("bd.sav", ["Sinnoh"], false, false, false),
};
foreach (var (file, regions, coins, bp, dates) in cases)
{
    if (!File.Exists(Path.Combine(work, file))) { Console.WriteLine($"     [{file}] sem o save: pulado"); continue; }
    vm.Open(Path.Combine(work, file)); Pump();
    vm.CurrentPage = trainer; Pump();
    Check($"[{file}] seções certas", trainer.BadgeGroups.Select(g => g.Region).SequenceEqual(regions) && trainer.HasCoins == coins && trainer.HasBP == bp && trainer.HasDates == dates,
        $"insígnias [{string.Join(", ", trainer.BadgeGroups.Select(g => g.Region))}] moedas {trainer.HasCoins} BP {trainer.HasBP} datas {trainer.HasDates}");
    foreach (var g in trainer.BadgeGroups) g.NoneCommand.Execute(null);
    if (trainer.BadgeGroups.Count > 0)
    {
        trainer.BadgeGroups[0].Badges[0].Has = true; // a primeira da primeira regiao
        trainer.BadgeGroups[^1].AllCommand.Execute(null); // todas da ultima
    }
    if (coins) trainer.Coins = 1234;
    if (bp) trainer.BP = 777;
    if (trainer.HasGender) trainer.GenderIndex = 1;
    var start = new DateTime(2010, 3, 14);
    if (dates) { trainer.AdventureStart = start; Console.WriteLine($"     [{file}] início na tela: {trainer.AdventureStart:g}"); }
    if (file == "hg.sav") Shot("trainer-hg");
    var saved = Reopen(file);
    var groups = TrainerExtras.GetBadges(saved);
    bool badgesOk = groups.Count == regions.Length && (groups.Count == 0 || (groups[^1].Names.Select((_, i) => groups[^1].Get(i)).All(x => x)
        && (groups.Count == 1 || (groups[0].Get(0) && Enumerable.Range(1, 7).All(i => !groups[0].Get(i))))));
    Check($"[{file}] insígnias gravadas", badgesOk);
    if (coins) Check($"[{file}] moedas gravadas", TrainerExtras.GetCoins(saved) == 1234, $"{TrainerExtras.GetCoins(saved)}");
    if (bp) Check($"[{file}] BP gravados", TrainerExtras.GetBP(saved) == 777, $"{TrainerExtras.GetBP(saved)}");
    if (TrainerExtras.HasGender(saved)) Check($"[{file}] gênero gravado", saved.Gender == 1);
    if (dates) Check($"[{file}] início da aventura gravado", TrainerExtras.GetDate(saved.SecondsToStart).Date == start, $"{TrainerExtras.GetDate(saved.SecondsToStart):d}");
}

// Crystal tem genero; Red nao.
vm.Open(Path.Combine(work, "red.sav")); Pump(); vm.CurrentPage = trainer; Pump();
Check("[Red] sem escolha de gênero", !trainer.HasGender);

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
