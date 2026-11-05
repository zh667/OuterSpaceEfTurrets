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
