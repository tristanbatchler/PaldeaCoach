#!/usr/bin/env python3
"""Generate Data/tm_recipes.csv (enables the `craft` command) from your own copy of
https://pokemondb.net/scarlet-violet/tms. Run from the PaldeaCoach/ folder, then rebuild:

  curl -sL https://pokemondb.net/scarlet-violet/tms -o tms.html
  python3 -I tools/scrape_tm_recipes.py tms.html Data/tm_recipes.csv

The generated CSV is for your own use. It's gitignored, because Pokémon Database doesn't permit
redistributing its content, so please don't commit or publish it.

The page lists recipes as "(TM Machine: 1,500 LP, 3 Rockruff Rock, 3 Klawf Claw)" in the Location column.
It only covers base-game TMs (001-171). Its typos are corrected below; the C# loader warns about any
material name that still doesn't match a game item.
"""
import csv, html, re, sys

FIXES = {  # source spelling -> in-game item name
    "Auzrill Fur": "Azurill Fur", "Fletching Feather": "Fletchling Feather", "Magikaro Scales": "Magikarp Scales",
    "Scyther Claws": "Scyther Claw", "Pineco Hunk": "Pineco Husk", "Shinx Fan": "Shinx Fang",
    "Tarountule Thread": "Tarountula Thread", "Figibax Scales": "Frigibax Scales",
}

src, dst = sys.argv[1], sys.argv[2]
page = open(src, encoding="utf-8").read()
out, missing = [], []
for row in re.findall(r"<tr>(.*?)</tr>", page, re.S):
    cells = [re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", c))).strip()
             for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
    if len(cells) < 8 or not cells[0].isdigit():
        continue
    m = re.search(r"TM Machine: ([\d,]+) LP, (.*?)\)", cells[7])
    if not m:
        missing.append(int(cells[0]))
        continue
    body = re.sub(r"\b(\d+), ", r"\1 ", m.group(2))           # "3, Klawf Claw" -> "3 Klawf Claw"
    body = re.sub(r"(\D) (\d+ )", r"\1, \2", body)             # "Dedenne Fur 3 Shinx Fang" -> missing comma
    for part in [p.strip() for p in body.split(",") if p.strip()]:
        mm = re.match(r"(\d+) (?:\d+ )?(.+)", part)            # "3 3 Drowzee Fur" -> doubled count
        count, name = (int(mm.group(1)), mm.group(2)) if mm else (3, part)  # count missing -> 3 (the usual)
        out.append((int(cells[0]), cells[1], int(m.group(1).replace(",", "")), FIXES.get(name, name), count))

with open(dst, "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["tm", "move", "lp", "material", "count"])
    w.writerows(out)
print(f"{len({o[0] for o in out})} recipes, {len(out)} material rows; no recipe on page for {len(missing)} TMs")
