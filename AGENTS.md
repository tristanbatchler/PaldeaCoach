# Paldea Coach: guide for AI assistants

This repo is a command-line tool that reads a **Pokémon Scarlet/Violet save file** and reports on the player's
party, boxes, bag and TM crafting. The user will usually ask you questions in plain English ("is my team
weak to anything?", "should Tinkatuff learn Play Rough?"). Answer them by **running the tool and reasoning over
its output**. Don't answer from general Pokémon knowledge alone when the tool can tell you about *their* team.

## Running it

```bash
./paldea <command> [args]      # builds on first run (needs the .NET 10 SDK), then runs
./paldea help                  # full command list
```

- The save file comes from `$POKEMON_SAVE`, a `.env` file in the repo root (`POKEMON_SAVE=...`), or `./main`.
  If you get "Save file not found", ask the user where their save is (see README → "Finding your save").
- The tool is **read-only**. It never modifies the save, so it's always safe to run.
- The save is only as fresh as the player's last in-game save. If the output doesn't match what they describe
  (wrong levels, missing a Pokémon they just caught), ask them to save in-game (or wait for autosave) and re-run.
- Output is plain text built for humans. Read it, then summarise. Don't paste walls of it back to the user.

## Which command answers which question

| The user asks… | Run | Notes |
|---|---|---|
| "What's my team?", "what moves does X have?" | `summary` | Party with moves, stats, ability, held item, nature, defensive matchups. Start here when you need context. |
| "What's my type coverage like?", "how can I improve my team?", "what am I weak to?" | `coverage` | Offence (types hit super-effectively), defence (shared weaknesses), best swaps from the boxes, and typings worth catching. Add `--keep Name,Name` for members the user won't replace. |
| "Would X be a good addition?", "who should X replace?" | `whatif <pokemon>` | Score change for X replacing each member, and which weaknesses it fixes or creates. X is credited with STAB moves only. |
| "What should I use against X?", "how do I beat this gym leader's Y?" | `vs <pokemon>` | Ranks party members by expected damage ÷ how hard X's types hit them. Run once per opposing Pokémon. |
| "Should X learn move M?", "is M better than N?" | `learn <x>`, `movecalc <x> "M,N,..."`, `coverage` | `learn` shows when/how it learns M (level-up or TMs in the bag). `movecalc` compares expected damage against its current moves. Then check `coverage` for whether M's type adds anything the team lacks. Weigh damage *and* coverage. |
| "Which items should I give my Pokémon?", "how do I maximise my team score?", "what should I do with my TMs / candies?" | `bagplan` | Greedy plan: TMs to teach (including ones craftable right now) and over which move, Exp. Candy results (level-ups, evolutions, new moves), best held item per member, usable evolution items. |
| "What TMs can I craft?", "what materials do I need?" | `craft` (`--all` includes locked recipes) | Shows LP cost, missing materials, and which party members can use each TM. |
| "Can anyone learn TM 79?" | `whocan 79` | |
| "What's in my boxes?" / "in my bag?" | `boxes` / `bag [filter]` | |
| "What's X weak to?" | `dex <name>` | Works without a save. |

Chain commands when a question needs it. "How can I improve coverage?" is often `coverage` → `whatif` on
the most promising suggestions → `learn` for whether the newcomer gets good moves soon.

## Reading the numbers

- **Team score** = % of all SV species/forms the party hits super-effectively (with moves it *actually knows*)
  − 4 × *unanswered weaknesses*. An unanswered weakness is a type that hits more members super-effectively
  than there are members resisting it. Higher is better. Score deltas (`+12.2`) compare options.
- **Damage numbers are relative**, not HP. They come from expected damage: base power × accuracy ×
  multi-hit distribution × crit chance × average damage roll × STAB × attacker's real Atk/SpA. In `vs` they're
  also divided by the target's base Def/SpD and multiplied by type effectiveness. Compare numbers within one output.
- `!!` / `!` in the coverage defence table mark types with 2+ / 1 unanswered weaknesses.

## What the model does NOT account for (mention it when it matters)

- Abilities (Levitate, Thick Fat, Intimidate…), stat stages, weather/terrain, Tera (Tera type is shown but not
  used in maths), status moves' value, speed/turn order, and the opponent's actual moveset (only its types).
- Only the held items listed in `PaldeaCoach/BagPlan.cs` (`HeldEffect`) are modelled for damage.
- Opponents in `vs` are rated by base stats, not their actual level. Trainer battles can differ.
- TM recipes cover base-game TMs 001–171. DLC TMs aren't in `craft`. Recipe data is optional and not in the repo.
  If `craft` says it isn't included, point the user to README → "TM crafting data". Don't commit the generated CSV.
- `bagplan` never overwrites status moves. It assumes the user picked them on purpose.

Use your own Pokémon knowledge to add these caveats on top of the tool's answer, e.g. "the tool says Diglett,
but Tinkaton's Mold Breaker doesn't matter here…". Say clearly which parts come from the tool and which are your judgement.

## Working on the code

- C# / .NET 10, single console project in `PaldeaCoach/`. Save parsing and game data come from
  [PKHeX.Core](https://github.com/kwsch/PKHeX) (NuGet).
- `Program.cs`: command dispatch and output formatting. Add new commands to the `Usage` text, the `commands`
  array and the `switch`.
- `Coverage.cs` (team score), `BattleAdvisor.cs` (damage model), `BagPlan.cs` (TM/candy/held-item planning),
  `TmCrafting.cs`, `Moves.cs` (move data + corrections PokeAPI lacks), `TypeChart.cs`, `Pokedex.cs`, `Party.cs`, `Learnsets.cs`.
- `Data/*.csv` are embedded resources: PokeAPI move data, plus the optional, gitignored `tm_recipes.csv` that users
  generate with `tools/scrape_tm_recipes.py` (Pokémon Database doesn't allow redistributing it).
- Build: `dotnet build PaldeaCoach -c Release`. There are no automated tests. Verify changes by running commands
  against a save (`./paldea coverage`, etc.) and `./paldea dex` (needs no save).
- Never commit save files (`main`) or `.env`. They're gitignored.
