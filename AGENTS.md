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

## 0.3.0 (2026-11-06): game 0.9.0020, ground marks, placement preview
- Game 0.9.0020 runs the map logic on a simulation thread (`SimThread`, on by default: `UserSaver.simThreaded`).
  Tower `logic()` and everything it calls run there. Never touch Unity objects from it: read positions from
  `SpriteObject.localPos` (not `transform`), use `System.Random` (not `UnityEngine.Random`), and hand effects and
  drawing to the main thread with `MainThread.post` (`EfFx`, `EfGround` do this). Loading a prefab off the main thread
  crashed the game natively (Player.log: `PrefabLoader.getPrefab` under `SimThread:workerLoop`). The game's own
  `EffectManager`/`BulletManager`/`addBuild` already marshal themselves.
- Ground marks (`src/EfGround.cs`): acid/poison pools and ice sheets made by `tools/assets/make_ground.py` from the
  game's terrain pictures, scorch = the game's `img/groundburn.png`. Drawn like the game's burn layer: a `TextureDrawer`
  under `MapManager.mapEventManager` (created with `PlatformTools.setPresent`, or the camera culls it), sorting
  MAP_GROUP/MAP_GROUP_EFFECT, `Light2DWorldLightUtility.SetWorldLight(material, true)` so night darkens them.
  Packaged pictures are in `v_common/Resources/Textures`.
- Placement preview: mod buildings with an icon are previewed by `BuildPlanMod`, which turns the icon by the build
  direction (2 = 180 degrees, the menu's default). `EfPlacement` keeps the mod's towers at direction 1.
- The game's dark smoke/sphere hits (`bomb/Plasma_Big`, `bomb/Laser_Big`, `Eff_Defense/effects/bomb/Laser_Big`) read as
  black blots on light ground; do not repeat them.
- Power: a tower links itself to a power line, generator or battery within 7 cells (game rule, same for the game's
  turrets); unlinked towers do nothing (`--tests power`).
- Test modes: `ground` (marks around a tower), `plan` (preview in four directions + the menu preview), `power`.

## 0.4.0 (2026-11-07): balance against the game's turrets (first planned as 0.3.1)
- Compare every tower with the game's turret of the same tier and role (table `B_Defense`, ids 10-63): damage per
  second = shots x damage / reload (both sides ignore the fire animation), range, HP, power. Up to about 30% above the
  game is fine; more needs a reason (size, short range).
- Ranges: the game's shortest turret range is 15 cells and enemy guns reach 18-20 (pistols), 24-28 (SMGs), 35-38
  (rifles), 45-50 (snipers, rockets) (`T_Weapon.Range`). Only the medic (10) stays below 14 (the flamethrower: 15);
  lobbed area towers sit at 22-26 (the game's missile turret: 30).
- 0.3.1 changes: ranges of jet 14, frost 22, acid 24, chain 18, medic 10, grenade 22, high-explosive 26, poison 26,
  wave 16, flame 12, charge 20; "Downpour" 52 per shot (was 70); high-explosive 80 per shell, reload 4 s (was 130,
  2.6 s); charge orb 20 per tick (was 30). The flame band and the wave sector follow the range.
- Measured (`--tests dps`: each tower alone, three standing monsters 11 cells away, 20 s): the game's bullets miss a
  lot (machine gun tiers 4-16/s, noisy), the mod's special towers never miss and hit groups, so they measured 3-5x their
  peers. Second cut: poison 5 per tick (was 20), charge hop 70 (110) and orb 10 per tick, mortar 60 per shell (it fires
  one shell per muzzle, two per volley), acid 25. The arc hops of chain/charge are fall-point bullets with no blast
  radius: the hop damage is dealt by the mod (before 0.3.1 they did none).

## 0.4.0 (2026-11-07): tilting launchers, turning flamer, effects from game textures
- Launchers (frost, acid, poison: `TowerSpec.Pitch`) are rendered at 0-60 degrees in 10-degree steps (towers.json
  "pitch": the launcher part tilts about its own origin, the trunnion; `bl_dirs.py` writes d<k>_p<j>; checked: contact
  with the fork at every angle is no more than at 0 degrees). Sprite actions for level j >= 1: firing 50+(j-1)*48+d,
  idle 74+(j-1)*48+d (make_assets.py). `EfTurret` keeps `Pitch`: level when idle, tilting at 90 deg/s while aiming to
  the launch angle for the target's distance (15 degrees point blank .. 60 at full range), firing only once there.
- The game's lobbed bullet (Bullet_Throw / FairyPhysics.CalculateParabola) always peaks at half the distance, one shape
  for every range. The launchers fly their own arc (`EfFx.Lob`) at the launcher's angle and deal the landing damage
  themselves (ATK within DamageRange); the mortar (7402) is modelled at a fixed high angle and keeps the game's arc.
- AF1 (7307) turns with its target (24 directions of the whole model); the flame goes along the drawn direction.
- Effects: the game's particle API is stripped (no EmitParams, no start size, velocity over lifetime read-only). Cloning
  prefabs and changing their modules mostly failed (parts switched off in the prefab, tiny local scales, hierarchy
  scaling under the effect layer shrank them to nothing). What works: plain prefab clones (`EfFx.Particle`, with the
  "decal" parts switched off - those were the black blots on light ground) and `EfPuffs`: the mod's own particles drawn
  with TextureDrawers using textures copied out of the game's effect materials (smoke, glow, spark, crackle, shock ring;
  Graphics.Blit + ReadPixels, recoloured per colour). `EfLooks` composes them per tower.
- Ground pools of acid and poison are near-round (make_ground.py round_shape); the ice stays irregular.
- Camera in tests: `CamearControl.setTo(x, y, scale)`; scale is the orthographic size factor (smaller = closer);
  NaN for "keep the position" puts the camera at NaN (black screen).
- Test modes: `dps` (damage per second against standing monsters, the game's turrets for comparison), `fxpreview`
  (close-up sequences of the special towers), `fxlab` (calibration grid), `fxmats` (materials inside the game's prefabs).
- AF1's flame (0.4.1, after user feedback): a continuous stream of the game's flamethrower bullets (Spue/Bullet, moved
  by `EfFx.Lob` with no arc) from the nozzles to the end of the range, 17 of them over 0.6 s, plus a glow and smoke
  (`EfLooks.Flame`). The same picture copied as an 8-frame flipbook into EfPuffs came out faint. Range 15 cells, band
  2.4 cells wide.
- Release note: the v0.4.1 tag holds only the version number and notes (the flame commit's push failed while the network
  was down); the flame code is released as 0.4.2. Check `git log origin/main` after every push before merging.
