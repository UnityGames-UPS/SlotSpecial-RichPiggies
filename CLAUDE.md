# CLAUDE.md — SlotSpecial-RichPiggies

Unity 6 (6000.3.9f1) WebGL slot machine client. Server-authoritative: the client is a
presentation layer only — all outcomes come from the backend over Socket.IO.

> **Status:** early development. This repo was cloned from a **Chinese New Year** slot
> template and renamed (`productName: RichPiggies`). Most code, art, and symbol IDs are
> still the CNY template's. Treat existing gameplay features as *scaffolding to be
> replaced*, not as spec.

---

## 1. Working rules (read first)

### Unity assets — read, never write
You **may read** `.prefab`, `.unity`, `.asset`, `.meta`, `.controller`, `.anim`, and
`ProjectSettings/*` files to understand wiring (which GameObject holds which component,
what a `[SerializeField]` is bound to, GUID → asset resolution).

You **must not edit, create, delete, move, or rename** any of them, and must not run
scripts/`sed`/`yq` that rewrite them. Unity YAML carries GUIDs, fileIDs, and local
ordering that break silently when hand-edited, and the user owns all Editor work.

Also off-limits: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.slnx`
(all Unity-generated).

**Instead of editing, emit an "Editor TODO" checklist** at the end of your response —
each item concrete enough to execute in the Inspector without re-deriving context:

```
### Editor TODO (do these in Unity, I did not touch any asset)
1. Scene `Assets/Scenes/GameScene.unity` → `Canvas/SlotArea/SlotView`
   - New field `pigWildSprite` (SlotView.cs:31) → assign `Assets/Graphics/UI/pig_wild.png`
2. Prefab `Assets/Prefabs/SlotIcon.prefab`
   - Add `ImageAnimation` component; set AnimationSpeed = 12, doLoopAnimation = true
3. New prefab needed: `JackpotMeterSpace` — Image (empty/filled states) + ImageAnimation
```

Rules for the checklist: name the exact asset path and GameObject path, cite the script
file:line that introduced the requirement, state the exact value to set, and flag any
field that must be *removed* because you deleted the serialized member.

### C# code — free to edit
Everything under `Assets/Scripts/` is yours. When adding a `[SerializeField]`, always
add a matching Editor TODO — a new serialized field is null in the scene until the user
wires it, and a silent `NullReferenceException` at runtime is the usual failure.

Never delete or rename an existing `[SerializeField]` without flagging it — the scene
loses the binding.

### Other conventions
- **Never run the Unity Editor or a build from the CLI.** The user compiles and plays.
- Third-party code is vendored under `Assets/Spine/`, `Assets/Plugins/Demigiant`
  (DOTween), `Assets/ProceduralUIImage/`, `Assets/TextMesh Pro/` — do not modify.
- `Assets/OldAssets/` is Chinese New Year art kept for reference; it will be deleted.
  Don't add code paths that depend on it.
- Members are `internal` by default across managers (single assembly). Follow that.
- Async work is Unity coroutines + DOTween. There is no UniTask/async-await in this codebase.

---

## 2. Target game — Rich Piggies (Orion Stars)

The game being cloned is the **Orion Stars** *Rich Piggies*, not any of the similarly
named NetGame / Light & Wonder titles — public reviews of "Rich Piggies" describe a
different game and are **not** a valid source. The spec below is transcribed from the
in-game rules screens supplied by the user and is authoritative. Where this section and
a web source disagree, this section wins.

Every feature below is **simulated on the backend**. The client's job is to render state
and play the animation — never to roll, evaluate, or accumulate anything.

### Line pays
- Wins pay on **adjacent reels beginning with the leftmost reel** (a left-to-right
  payline game). **Only the highest winner is paid per winning combination**, and
  **multiple line wins are added together**.
- The paytable reflects the current bet configuration.

| Pay group | Symbols | 5 | 4 | 3 |
|---|---|---|---|---|
| 1 | Boss pig (cigar, on cash) | 200 | 125 | 20 |
| 2 | Lobster-dinner pig | 125 | 60 | 15 |
| 3 | Sunglasses pig · gold medallion | 100 | 40 | 15 |
| 4 | Yacht · A · K | 60 | 25 | 10 |
| 5 | Q · J · 10 | 60 | 20 | 5 |

Groups 3–5 pay "of a kind" — the symbols in a group share one payout row.

### Paylines — 25, on a 5×3 grid
The rules screen is titled **PAY LINES** and shows **25 numbered fixed line patterns**
(lines 1–25) drawn on a 5-column × 3-row grid. Its footer reads *"25 WAYS TO WIN"* with a
valid (✓) and an invalid (✗) example, and *"Way wins awarded for left to right adjacent
symbol combinations."*

Reconciling the two labels: this is a **25 fixed-payline** game. The "ways" wording
describes the adjacency rule already stated in the paytable — a combination must start on
the **leftmost reel** and run across **adjacent** reels, so 3-of-a-kind on reels 1-2-3 pays
even though the line continues to reels 4 and 5. The ✗ example is a combination with a gap
(not starting at reel 1, or skipping a reel). It is **not** a 243/1024-ways engine.

Confirmed line shapes: **1 = middle row** straight, **2 = top row** straight,
**3 = bottom row** straight. Lines 4–25 are the usual V / inverted-V / zigzag / stepped
variants.

> **Do not hand-transcribe lines 4–25 from the screenshot.** The exact per-line
> coordinates come from the server as `initData.gameData.lines` and land in
> `GameConfig.paylines` ([GameDataModels.cs](Assets/Scripts/Core/GameDataModels.cs)) —
> that array is the single source of truth. Use the screenshot only to sanity-check the
> rendered overlay against the real game.

Client consequences:
- `paylineCount` becomes **25** (template ships 243), and `totalLines` will be 25.
- `GameConfig.rowCount` is derived as `totalLines == 243 ? 3 : totalLines == 1024 ? 4 : 3`.
  With 25 it returns 3 — **correct, but only by falling through to the default**. Fix the
  derivation rather than relying on that.
- The template has **no line-path renderer**. Phase 2 currently boxes individual winning
  symbols; a 25-payline game normally draws the **line's path** across the grid. Assume a
  polyline/overlay per line is net-new work, driven by `GameConfig.paylines`.

### Symbols
- **WILD** (fan of cash) — substitutes for all symbols **except** the Blue, Yellow, and
  Red Piggy bonus symbols.
- **Mystery** (bronze/orange medallion tile) — see below.
- **Blue / Yellow / Red Piggy** — the three bonus symbols. They drive both the Free Spins
  trigger and the three persistent meters.

### Mystery Reveal
Base game, **before pays are evaluated**: every Mystery symbol on the grid is replaced by
one and the same reveal — WILD, boss pig, lobster pig, sunglasses pig, medallion, yacht,
A, K, Q, J, or 10. Additionally, a Mystery **may reveal a Blue, Yellow, or Red Piggy on
top of a replaced symbol**, also before pays are evaluated.

During Free Spins, Mystery symbols are replaced the same way (WILD, the three pigs, yacht,
medallion, A, K, Q, J, 10) before pays are evaluated.

Client consequence: there is a **reveal beat between reel stop and win presentation**.
The current loop has no such step — see §8.

### Free Spins trigger
- **Any combination** of Blue, Yellow, and Red Piggies may trigger Free Spins and awards
  **1× total bet**.
- The **size** of a Blue/Yellow/Red Piggy has no effect on triggering. (Implies oversized
  / multi-cell piggy symbols exist on the reels — confirm the rendering contract with the
  backend; the client currently only draws 1×1 symbols.)
- Free Spins use an **alternate reel set**. Winning combinations there are identical to
  the base game **except Blue, Yellow, and Red Piggies do not appear**.
- Accumulation of enhancements changes with the bet option — **all three meters below are
  per-bet-option**.

### The three persistent meters
When a piggy lands but does *not* trigger Free Spins, it feeds its meter instead:

| Piggy | Non-trigger effect | Meter cap |
|---|---|---|
| Blue | may add **1 free spin** to the Blue meter, randomly | 100 free spins |
| Yellow | may add **10x-100x current bet** to one of the MEGA / GRAND / MAJOR / MAXI / MINOR / MINI meters | — |
| Red | may add **15 wilds** to the Red meter; if the meter already shows **75**, it may add **25** instead | 100 wilds |

Which piggy triggered Free Spins then selects the variant:

**Triggered with Blue** — award the number of free spins currently displayed in the Blue
meter. If Free Spins were *not* triggered with Blue, award **7**. At the end of a
Blue-triggered round the Blue meter **resets to 9**.

**Triggered with Yellow** — during the round, each Mystery symbol may reveal a jackpot
symbol for MEGA, GRAND, MAJOR, MAXI, MINOR, or MINI. Each revealed symbol fills one space
of its meter:

| MEGA | GRAND | MAJOR | MAXI | MINOR | MINI |
|---|---|---|---|---|---|
| 6 | 5 | 4 | 3 | 2 | 2 |

When a meter's spaces are all filled, its symbols are cleared, that jackpot is awarded,
and it resets **before any additional jackpot is awarded** (so multiple jackpots in one
round resolve sequentially). At the end of a Yellow-triggered round **all six meters reset**.

**Triggered with Red** — the round uses an alternate reel set containing a number of WILD
positions **equal to the wild count shown in the Red meter**. At the end of a
Red-triggered round the Red meter **resets to 15 wilds**.

### What this means for the client
Net-new presentation work, in rough dependency order:
1. **Mystery reveal step** — a distinct animation phase after reel stop, before win lines.
2. **Three persistent meters**, rendered from server state, per bet option, with
   increment animations and the defined reset points.
3. **Six jackpot meters with discrete filled/empty spaces** (6/5/4/3/2/2) plus a
   fill -> award -> clear sequence.
4. **Three visually distinct Free Spins variants** (blue = spin count, yellow = jackpot
   collection, red = wild-loaded reels).
5. **25-payline win presentation** with a per-line **path overlay**, replacing the
   template's 243-ways symbol-boxing.
6. Possibly **oversized multi-cell piggy symbols**.

Existing plumbing that partly fits: `JackpotData` / `JackpotValues`, the `jackpot:sync`
socket event, and `UIManager.UpdateJackpotDisplay()` already carry **grand / major /
minor / mini**. Rich Piggies needs **six** tiers — **MEGA and MAXI are missing** — and
needs per-meter *space counts*, not just a cash value string. Extend rather than rebuild.

---

## 3. Architecture

```
SocketIOManager  ──socket──▶  GameManager  ──▶  SlotView    (reels, symbol + win anims)
(net + auth)                  (state machine)   UIManager   (HUD, popups, bonus flows)
                                                PopupManager (errors, loading, reconnect)
                                                AudioManager (singleton)
InitDataConverter / SpinResult (Assets/Scripts/Core/GameDataModels.cs) — server DTO ↔ client model
```

Single scene: [GameScene.unity](Assets/Scenes/GameScene.unity).
Prefabs: `SlotIcon`, `WinBox`, `Coin`, `star`, `FountainPool`, `GuidePage`, `GuideScroll`.

### Networking — [SocketIOManager.cs](Assets/Scripts/BackEnd/SocketIOManager.cs)
Best HTTP/SocketIO (Tivadar). Token comes from the hosting page via
[JSFunctCalls.cs](Assets/Scripts/BackEnd/JSFunctCalls.cs) (WebGL `[DllImport("__Internal")]`).

| Direction | Event | Payload |
|---|---|---|
| ← | `game:init` | `InitData` — bets, lines, symbols, feature config, balance |
| ← | `result` | `ServerSpinResponse` |
| ← | `balance:sync`, `jackpot:sync`, `AnotherDevice`, `pong` | |
| → | `request` | `{ type: "SPIN", payload: { betIndex, isFreeSpin } }` ([SocketIOManager.cs:560](Assets/Scripts/BackEnd/SocketIOManager.cs#L560)) |
| → | `ping` | heartbeat; missed pongs trigger the reconnect popup |

`testSocketURL` is used under `#if UNITY_EDITOR`, `socketURL` in builds.

---

## 4. The gameplay loop (the important part)

Owner: [GameManager.cs](Assets/Scripts/Managers/GameManager.cs). States are
`GameState { Initializing, Idle, Spinning, Stopping, ShowingWin, FreeSpinMode }`.

The key design point: **reel animation and the network round-trip run concurrently.**
`SpinRoutine` burns a fixed cosmetic duration, then blocks on `lastResult != null`.

```
RequestSpin()                          GameManager.cs:150   guards: Idle, connected, balance
  └ StartSpin()                        :180   deducts GetTotalPay(), uiManager.OnSpinStarted(),
                                              slotView.StartSpin(), socket SendSpinRequest(),
                                              starts SpinRoutine
     ├ SpinRoutine()                   :212   waits GetSpinDuration() (Normal 3.5 / Turbo 2.0 /
     │                                        QuickSpin 0.8s) or stopRequested (+0.5s hold),
     │                                        then `while (lastResult == null) yield`
     │                                        → slotView.StopSpin() or QuickStop()
     │ ◀ OnSpinResultReceived()        :464   socket callback; sets lastResult, updates the
     │                                        free-spin counter immediately (server-authoritative)
     └ OnReelsStoppedComplete()        :253   rebuilds playerData at reel-stop balance
                                              (feature wins are deferred, see below), then
                                              branches on big-win vs normal win
        └ OnWinAnimationComplete()     :319   → ProcessSpecialFeaturesAfterWin()
           ├ uSpin triggered           → DelayUSpinTriggerResult()     :371 wheel bonus
           ├ moneyBag triggered        → DelayMoneyBagTriggerResult()  :399 pick bonus
           ├ freeSpin triggered        → DelayScatterTriggerResult()   :356
           └ else ResumeAfterSpecialFeature() :345
              └ ProcessSpinResult()    :495   commits playerData, advances autoplay /
                                              free-spin counters, returns to Idle
```

Things that bite:
- `StartSpin()` calls `ProcessSpinResult()` for the *previous* round if `lastResult` is
  still pending — rapid spins settle the prior round lazily.
- **Deferred feature wins:** at reel stop the balance shown is
  `serverBalance - GetTotalFeatureDeferredWins()` (wheel-multiplier + money-bag cash), so
  the bonus payout lands visually when its popup resolves, not at reel stop.
  See `SpinResult.GetTotalFeatureDeferredWins()` ([GameDataModels.cs:349](Assets/Scripts/Core/GameDataModels.cs#L349)).
- `GetTotalPay() = currentBetAmount * gameConfig.creditDivisor` (default 25). Bet index,
  not bet amount, goes to the server.
- Free spins are entirely server-driven (`serverSpinsRemaining/Used/TotalSpins`,
  `isRoundOver`). Never count them client-side.
- `waitingForSpecialWin` and `uiManager.IsSpecialWinActive` gate the next round; several
  coroutines spin on them.

---

## 5. Win lines — iteration and animation

### Server → client
Server sends **243-ways** wins as `payload.waysWins[]`, each with
`matchedPositions: [{row, col}]`. `InitDataConverter.ConvertWinningLines()`
([GameDataModels.cs:697](Assets/Scripts/Core/GameDataModels.cs#L697)) flattens each to
`WinLine { lineId, symbolId, positions, winAmount }` where

```
flatIndex = row * 5 + col        // encode
row = flatIndex / 5;  col = flatIndex % 5   // decode (SlotView)
```

The `* 5` is **hardcoded** in both directions. Any change to reel count means changing
both sites.

Note the axis flip: `SpinResult.resultMatrix` is **column-major** (`matrix[col][row]`,
built by `ConvertReelsToMatrix`), while `WinLine.positions` are row-major flat indices.

### Presentation — `SlotView.PlayTwoPhaseWinLines()` ([SlotView.cs:903](Assets/Scripts/Managers/SlotView.cs#L903))

**Phase 1 — all wins at once.** Union every `winLine.positions` into a `HashSet<int>`,
show the summed total in `phase1TotalWinText`, play
`AudioManager.PlayWinLinePhase1Start()`, animate every winning cell, then fire
`onComplete` so the game loop can advance.

**Phase 2 — per-line cycle, infinite `while(true)`.** Iterates `winLines` one at a time,
showing the per-line amount over the line's first symbol (only when `winLines.Count > 1`).
It runs until `KillWinTweens()` cancels the coroutine at the next spin.

Phase 2 is **skipped** during free spins, autoplay, or when any special feature triggered
(`skipPhase2`, [SlotView.cs:955](Assets/Scripts/Managers/SlotView.cs#L955)).

### Per-cell animation — `AnimateWinPositions()` ([SlotView.cs:990](Assets/Scripts/Managers/SlotView.cs#L990))
For each flat position:
1. `EnableWinBox(col, row)` — activates `winBoxColumns[col].rows[row]` (the frame overlay).
2. Look up the reel's static `Image` at `reelImagesList[col].images[2 + row]`.
   **The `2 +` offset is fixed: each reel holds 7 images, indices 2..4 are the visible rows.**
3. Grab the `ImageAnimation` on `winAnimationColumns[col].rows[row]`, feed it
   `animationSpriteArrays[symbolId]` (per-symbol sprite-sequence, wired in the Inspector).
4. Cross-fade: static image `DOFade(0)`, animated overlay to alpha 1.
5. `onLoopComplete` counts loops; at `loopCountTarget` (1 in free spins/autoplay, else
   `winSymbolLoopCount`) it stops, hides the overlay, fades the static image back, and
   increments `completedCount`. The coroutine waits on all cells finishing.

If a symbol has no sprite array it is silently `continue`d — a symbol that "doesn't
animate on a win" is almost always a missing `animSprites*` list in the Inspector.

### Big win
Threshold is `GameManager.bigWinMultiplierThreshold` (**500×**, serialized) against
`winAmount / GetTotalPay()`. Above it: controls lock, win-line animation plays, and
`TriggerWinPopupWithDelay(1.5s)` → `UIManager.TriggerBigWinPopup()` →
`ShowUniversalWinPopup(WinPopupType.BigWin, ...)`
([UIManager.cs:1743](Assets/Scripts/Managers/UIManager.cs#L1743)). Below it: HUD updates
and controls re-enable immediately while the win animation plays.

`WinPopupType` — `RegularWin`, `BigWin`, `FreeSpinTrigger`, `MoneyBagCollect`,
`FreeSpinComplete` — all share the one `universalWinPopup` object, toggling child
title/subtitle/amount objects per case.

### Reel motion — [SlotView.cs:366-660](Assets/Scripts/Managers/SlotView.cs#L366)
DOTween, 7 images per reel column, staggered start (`reelStartStagger`) with an
anticipation bounce, then a linear `symbolHeight`-per-cycle loop that shifts sprites down
(`CycleReelSymbols`) and refills index 0 with `Random.Range(0, 10)` — **note the hardcoded
10, it assumes 10 spinnable low/high symbols.** Stopping staggers per reel with an
overshoot-and-settle.

---

## 6. Symbol IDs (Chinese New Year template — will be re-mapped for Rich Piggies)

| ID | Symbol | | ID | Symbol |
|---|---|---|---|---|
| 0 | Lantern | | 7 | J |
| 1 | Hammer | | 8 | 10 |
| 2 | Money Pouch | | 9 | 9 |
| 3 | Coin | | 10 | **Wild** (`wildSymbolId`) |
| 4 | A | | 11 | **USpin / scatter** (`scatterSymbolId`) |
| 5 | K | | 12 | **Money Bag** |
| 6 | Q | | | |

IDs are inferred at init by name-matching in `InitDataConverter.ConvertToGameConfig()`
(`name.Contains("wild") / "uspin" / "moneybag"`), so the server's symbol names must keep
matching those substrings or the constants silently fall back to their defaults.

`GameConfig.rowCount` is derived from `totalLines`: 243→3 rows, 1024→4 rows.

### Target roster (Rich Piggies) — IDs not yet assigned, pending the backend
Boss pig · lobster pig · sunglasses pig · gold medallion · yacht · A · K · Q · J · 10 ·
**WILD** · **Mystery** · **Blue Piggy** · **Yellow Piggy** · **Red Piggy** — 15 symbols vs.
the template's 13. Note the template has a **9** that Rich Piggies does not (its low run
stops at 10), and Rich Piggies has **no single scatter** — three separate bonus piggies
replace `scatterSymbolId`, so the single-int `wildSymbolId` / `scatterSymbolId` model in
`GameConfig` does not survive contact with this spec. Agree the ID map with the backend
before touching `SlotView`'s sprite fields.

---

## 7. File map

| Path | Role |
|---|---|
| [Core/GameDataModels.cs](Assets/Scripts/Core/GameDataModels.cs) | All DTOs, enums, `InitDataConverter`. Start here for protocol questions. |
| [Managers/GameManager.cs](Assets/Scripts/Managers/GameManager.cs) | State machine, spin loop, bets, autoplay, free spins |
| [Managers/SlotView.cs](Assets/Scripts/Managers/SlotView.cs) | Reels, symbol sprites, win-line animation |
| [Managers/UIManager.cs](Assets/Scripts/Managers/UIManager.cs) | HUD, panels, win popups, bonus sequences (2k lines) |
| [Managers/PopupManager.cs](Assets/Scripts/Managers/PopupManager.cs) | Errors, loading, reconnect, exit |
| [Managers/AudioManager.cs](Assets/Scripts/Managers/AudioManager.cs) | `AudioManager.Instance?.PlayX()` singleton |
| [Managers/WheelSpinController.cs](Assets/Scripts/Managers/WheelSpinController.cs) | USpin bonus wheel |
| [Managers/MoneyBagController.cs](Assets/Scripts/Managers/MoneyBagController.cs) | Money-bag pick bonus |
| [Managers/StarFountain.cs](Assets/Scripts/Managers/StarFountain.cs) | Big-win particle/coin burst |
| [Helper/ImageAnimation.cs](Assets/Scripts/Helper/ImageAnimation.cs) | Sprite-sequence player; `onLoopComplete(int)` drives win timing |
| [Helper/OrientationChange.cs](Assets/Scripts/Helper/OrientationChange.cs) | Portrait/landscape; UI has duplicate refs per orientation |
| [Helper/SymbolInfoCard.cs](Assets/Scripts/Helper/SymbolInfoCard.cs) | Tap-a-symbol paytable card |
| [BackEnd/SocketIOManager.cs](Assets/Scripts/BackEnd/SocketIOManager.cs) | Socket, auth, ping/pong, reconnect |
| [BackEnd/JSFunctCalls.cs](Assets/Scripts/BackEnd/JSFunctCalls.cs) | WebGL ↔ host page bridge |

UI code frequently carries **two references for the same control** (landscape + portrait);
the `SetTMPText(a, b, ...)` / `SetButtonInteractable(a, b, ...)` helpers at
[UIManager.cs:364-388](Assets/Scripts/Managers/UIManager.cs#L364) update both. Adding a
control means adding both variants and wiring both in the scene.

---

## 8. Known template debt

Gaps between the Chinese New Year template and the Rich Piggies spec in §2:

- **Wrong win model.** Template is 243-ways (`paylineCount = 243`, server sends
  `waysWins`); Rich Piggies is **25 fixed paylines**, left-to-right from the leftmost reel,
  highest win per combination. `ConvertWinningLines()` and the Phase 1/2 presentation both
  need revisiting once the backend's line payload is defined — in particular whether wins
  arrive with a real `lineId` indexing `GameConfig.paylines` (they currently get a
  synthetic incrementing index).
- **No line-path renderer.** Phase 2 boxes individual symbols; a payline game draws the
  line's path across the grid. Net-new, driven by `GameConfig.paylines`.
- **`rowCount` derivation is wrong for this game.** `totalLines == 243 ? 3 : 1024 ? 4 : 3`
  returns 3 for 25 lines only by hitting the default branch.
- **No Mystery Reveal beat.** `OnReelsStoppedComplete()` goes straight from reel stop to
  win animation. Rich Piggies needs a reveal phase in between (transform every Mystery
  tile, then optionally stamp a piggy on top) *before* wins are shown.
- **No persistent meters.** Nothing in the client holds per-bet-option state. The Blue
  (free spins), Red (wilds), and Yellow (six jackpot meters) meters are all net-new, and
  must be driven by server state — including their reset points.
- **Jackpot tiers incomplete.** `JackpotValues` has grand/major/minor/mini; the spec needs
  **MEGA** and **MAXI** too, plus discrete filled-space counts (6/5/4/3/2/2) rather than a
  single formatted string.
- **No multi-cell symbol support.** The spec's "size of the piggies has no effect on
  triggering" implies oversized piggies; `SlotView` draws a fixed 1×1 sprite per cell.
- **Wrong feature set present.** USpin wheel (`WheelSpinController`), Money Bag pick
  (`MoneyBagController`), and their `GameManager` branches are CNY features with no
  Rich Piggies equivalent — they will be removed or repurposed, so don't build on them.
- **Hardcoded geometry.** `5`, `2 + row`, `images.Count != 7`, `Random.Range(0, 10)` are
  literal in `SlotView` despite `GameConfig.reelCount/rowCount` existing.
- **Phase 2 win-line loop never self-terminates**; it relies on the next spin killing it.
- **CNY assets and fields.** `Assets/OldAssets/` art and the CNY sprite fields in
  `SlotView` (lantern, hammer, money pouch, coin) are placeholders.
- **Dead fields kept only so the UI compiles:** `ExtraSpinsData`, `OverlayScatterData`,
  `stickyWilds`.
