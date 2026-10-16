using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

// Set do Showdown → Pokemon legal (EncounterDatabase.LegalizeSet via ShowdownBatch.Build, modo legal).
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n} {extra}"); if (!ok) fails++; }

void Run(string file, string label, string block, Func<PKM, bool> expect)
{
    var path = Path.Combine(work, file);
    if (!File.Exists(path)) { Console.WriteLine($"PULADO {label}: sem {file}"); return; }
    var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    var sw = Stopwatch.StartNew();
    var item = ShowdownBatch.Build(sav, block.Replace("\r", ""), legalMode: true);
    sw.Stop();
    var pk = item.Pk;
    Check($"{label}: legal", pk is not null && item.Legal && new LegalityAnalysis(pk).Valid, $"{item.Note} ({sw.Elapsed.TotalSeconds:0.0}s)");
    if (pk is not null)
        Check($"{label}: manteve o set", expect(pk), $"{item.Note}");
}

Run("sv.sav", "Garchomp SV", """
Garchomp @ Choice Scarf
Ability: Rough Skin
Tera Type: Steel
EVs: 252 Atk / 4 SpD / 252 Spe
Jolly Nature
- Earthquake
- Outrage
- Stone Edge
- Iron Head
""", pk => pk.Ability == (int)Ability.RoughSkin && pk.StatAlignment == Nature.Jolly && ((ITeraType)pk).TeraType == MoveType.Steel
        && pk.EV_ATK == 252 && pk.EV_SPE == 252 && pk.Move1 == (ushort)Move.Earthquake);

Run("fr.sav", "Gengar FireRed", """
Gengar @ Leftovers
Ability: Levitate
EVs: 252 SpA / 4 SpD / 252 Spe
Timid Nature
- Thunderbolt
- Psychic
- Shadow Ball
- Hypnosis
""", pk => pk.Nature == Nature.Timid && pk.EV_SPA == 252 && pk.HeldItem != 0);

Run("fr.sav", "Pikachu shiny FireRed", """
Pikachu (F)
Shiny: Yes
Hasty Nature
- Thunderbolt
""", pk => pk.IsShiny && pk.Nature == Nature.Hasty);

Run("hg.sav", "Dragonite shiny HeartGold", """
Dragonite (M) @ Choice Band
Shiny: Yes
Adamant Nature
- Extreme Speed
- Outrage
- Earthquake
- Fire Punch
""", pk => pk.IsShiny && pk.Nature == Nature.Adamant && pk.HasMove((ushort)Move.ExtremeSpeed));

Run("sh.sav", "Dragapult Shield", """
Dragapult @ Choice Specs
Ability: Infiltrator
EVs: 252 SpA / 4 SpD / 252 Spe
Timid Nature
IVs: 0 Atk
- Shadow Ball
- Draco Meteor
- Flamethrower
- U-turn
""", pk => pk.Ability == (int)Ability.Infiltrator && pk.StatAlignment == Nature.Timid && pk.IV_ATK == 0 && pk.HasMove((ushort)Move.DracoMeteor));

// Especie que nao existe no jogo: recusa com o motivo, sem travar.
{
    var path = Path.Combine(work, "fr.sav");
    if (File.Exists(path))
    {
        var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
        var item = ShowdownBatch.Build(sav, string.Join('\n', "Garchomp", "Jolly Nature", "- Earthquake"), legalMode: true);
        Check("espécie fora do jogo é recusada", item.Pk is null && !item.Legal && item.Note.Length > 0, item.Note);
    }
}

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHA(S)");
return fails == 0 ? 0 : 1;
