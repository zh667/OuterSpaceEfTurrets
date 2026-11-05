# 工业防御塔 (EfTurrets) — working rules

Mod for *Outer Space / 秩序：新曙光* (Steam 3080070), started from `E:/projects/outer-space-modkit`.

## Git
- Task branch per change from `main`; Conventional Commits; push the task branch; merge into `main` only after the
  user explicitly approves that merge. Creating a remote repository needs the user's go-ahead.
- Text files are LF. Git runs on Windows only. Commit identity: `zh667 <128960749+zh667@users.noreply.github.com>`.

## Build, install, test
- `python tools/build.py` builds against the installed game's `Outer Space_Data/Managed` (net472, no NuGet);
  `python tools/build.py install` copies `Manifest` and `v_common` to `<game>/mods/EfTurrets` and keeps anything else
  there (the player's data). Enable the mod once in the game's mod menu.
- `python tools/run_isolated.py [--tests ...]` runs `src/SelfTest.cs` in the kit's isolated copy of the game; the
  player's game and saves are only read. Run it before every release; for changes to threading, catch-up or long
  running work, also run a stress scenario (long, frequent, many objects) before release.
- A frozen test copy is dumped automatically (`local/dumps`, threads named); `tools/freeze_dump.py <pid> <dir>` does
  the same for any frozen game process (needs WinDbg's cdb from the Microsoft Store).

## This mod (first batch, 2026-11-05)
- Ten towers built from the original Endfield models (`tools/assets/towers.json`); the game's own turret logic runs them
  (`src/EfTurret.cs` only swaps the picture for 24 pre-rendered directions and reads the muzzle from the frame).
- Pictures: `python tools/assets/render_all.py [force] [ids]` (Blender 5.1, models from the MechDefense extraction under
  `local/endfield/tower-library/reference-assembled`), then `python tools/assets/make_assets.py` -> `local/gen`
  (sprite files, icons, review sheets). `tools/build.py` copies `local/gen` into the package; generated pictures stay
  out of git.
- Sprite layout every tower shares: action 0 idle up, 1 base + click rectangle, 2+d firing towards d, 26+d idle towards d.
- Rows are added in `EfDefLoader.initModDef` with plain numeric ids (towers 7xxx, unlock entries 97xxx, tech ids
  `ef-*`), because the game's own mod-row prefix (`package_id`) breaks `_`-separated references such as `10_<id>`.
- Self-test: run on a save that is on a planet (`--save zh6657 --exact-save`); the mothership stacks five decks on one
  map and towers land on decks the camera does not show.

## Second batch (special towers, 2026-11-05)
- `src/EfSpecialTurret.cs`: the visible shot is a real game bullet (`BulletManager.addDefenseBullet`, the row's
  BulletID/AimType/DamageRange/Drag/Shocktime), so flight, splash and slow are the game's; ice field, corrosion,
  poison, bouncing arcs, electric orb, push, stun, flame band and healing are done by the mod, timed to the landing
  (`Bullet_Throw` flight = distance / (BulletSpeed * 32)).
- Effects only from the game's own assets (`src/EfFx.cs`): F3D prefabs under `Prefab/Eff_Defense`, `Eff_Bullets`,
  `Eff_Groung/{Bomb,Fire,AirBomb}` and the effect sheet (`EffectManager.addEffect_Global`). The user rejected drawn
  cartoon pixel effects ("太动画了"). Weather prefabs (`Eff_Groung/Cloud/cloud1`, Fog, Cold Current, Hot Wave) show
  nothing or throw (Mod_Cloud.Update) when placed alone; `EffectManager.addPrefabEffect` reuses any finished prefab
  effect regardless of path, so do not use it.
- `--tests fxgallery` shows the game's effect prefabs one by one (screenshots `selftest-gal-*`).
- Tests: the player's people join fights, so special towers are judged by what they did themselves (hits, pushes,
  slows, stuns) and any failed fight is retried once from the other side.

## Rules learned the hard way (trade helper)
- Game objects are shared with the game's worker threads. Touch them only under the game's locks in the game's order:
  `FWTradeSession.SyncRoot`, then `FWGameData.ArchiveSyncRoot` (`Kit/GameLock.cs`, tried, never awaited). Skipping
  this froze the game for good.
- Many "getters" settle markets first (`EnsureSettledForUse`: `getPBase`, `GetBasePriceFactor`, `getItemSum`,
  `QuoteTrade`, `GetItemsForUse`...). Check `hasPendingSettlement()` first or read stored fields.
- Compute what the game computes by reading the code the game's UI/checkout actually runs, and compare the mod's
  numbers with the game's own functions in the self-test (one mismatch is a bug).
- Show names the way the game shows them (`LanguageDef`, star definitions' `ReplaceName`), not internal ids.
- `JsonUtility` silently drops nested types; use the game's `Newtonsoft.Json` and read a saved file back once.
- The game reports exceptions to its developers' server: catch in the mod, guard tests.
- Hide the mod's TextMeshPro UI before the game quits (`OnApplicationQuit`), or disabled text meshes throw.
- The game's collector is not incremental (heap ~2 GB, pauses of several hundred ms): keep per-frame allocation low and
  measure before optimising (`Frames` in `Kit/SelfTestBase.cs` counts allocation and collections).
- When the game updates (`Assembly-CSharp.dll` changes), re-check every game member the mod calls before building.
