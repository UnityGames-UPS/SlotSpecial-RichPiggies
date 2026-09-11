using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Reels + symbol presentation.
///
/// REEL MODEL (reworked from the CNY template to the Rich Piggies / reference-game model):
///
///   Each column is one RectTransform ("Slot") holding a fixed strip of <see cref="SlotSymbolView"/>
///   cells. Spinning tweens that transform from <see cref="spinTopY"/> down to
///   <see cref="spinBottomY"/> on an infinite Restart loop at a constant <see cref="reelSpeed"/>.
///
///   The result is written into the strip WHILE the reel is still looping
///   (<see cref="PopulateResultMatrix"/>), so by the time the column is told to stop the
///   correct symbols are already in place. Stopping just kills the loop, snaps the column
///   back to spinTopY and runs a single OutBack tween down to <see cref="restY"/>, which
///   parks the result cells inside the mask.
///
///   The old CNY model (7 images per reel, sprite-shifting every cycle, "2 + row" visible
///   window, snap-the-result-in-at-stop) is gone — all geometry now comes from the
///   serialized <see cref="allCells"/> / <see cref="resultCells"/> lists.
/// </summary>
public class SlotView : MonoBehaviour
{
  [Header("References")]
  [SerializeField] private GameManager gameManager;

  [Header("Symbol Sprites — index == server symbol id")]
  [Tooltip("Resting sprite for every symbol id. Element 0 is id 0, element 1 is id 1, and so on. " +
           "Ids covered by symbolSkeletons below may be left empty.")]
  [SerializeField] private Sprite[] symbolSprites;

  [Tooltip("Spine skeleton for symbol ids rendered in Spine rather than as a sprite — " +
           "index == symbol id, same as symbolSprites. Only ids 0/1/2 (BusinessPig, " +
           "LadyPig, BeachPig) are populated; a null entry means 'this id is a sprite'.")]
  [SerializeField] private Spine.Unity.SkeletonDataAsset[] symbolSkeletons;

  [Header("Win Animations — one entry per animated symbol")]
  [SerializeField] private List<SymbolWinAnim> symbolWinAnims = new List<SymbolWinAnim>();

  [Header("Reel Containers — one RectTransform per column")]
  [SerializeField] private RectTransform[] reelTransforms;

  [Tooltip("EVERY cell of each column's strip, top to bottom. Used for blur filler.")]
  [SerializeField] private List<SlotColumn> allCells = new List<SlotColumn>();

  [Tooltip("Only the cells that land inside the mask at restY — 5 columns x 3 rows, " +
           "row 0 = top. These receive the server result.")]
  [SerializeField] private List<SlotColumn> resultCells = new List<SlotColumn>();

  [Header("Reel Tween — positions are local Y on the column transform")]
  [Tooltip("Travel speed in local units/second. Every move derives its duration from this " +
           "so intro, loop and stop all run at the same visual speed.")]
  [SerializeField] private float reelSpeed = 2857f;
  [Tooltip("Y the column snaps to before each downward pass. Must be above restY.")]
  [SerializeField] private float spinTopY = 1900f;
  [Tooltip("Y the column travels down to before wrapping back to spinTopY.")]
  [SerializeField] private float spinBottomY = -1900f;
  [Tooltip("Y the column settles at. This is what puts resultCells inside the mask.")]
  [SerializeField] private float restY = 0f;
  [SerializeField] private float stopOvershoot = 0.9f;

  [Header("Reel Stagger")]
  [SerializeField] private float reelStartStagger = 0.08f;
  [SerializeField] private float reelStopStagger = 0.6f;
  [SerializeField] private float turboStopStagger = 0.2f;

  [Header("Win Animation Settings")]
  [Tooltip("How long a stage holds when NONE of its symbols has an animation configured.")]
  [SerializeField] private float winSymbolLoopDuration = 1.5f;

  [Tooltip("Delay between darkening the grid / lifting the winning cells and actually " +
           "starting their animations. Keep at 0 unless you want a beat before the symbols " +
           "move — staging already guarantees they all start on the same frame, and any " +
           "value here shows as a dark gap between back-to-back stages.")]
  [SerializeField] private float winAnimationStartDelay = 0f;

  [Header("Win Presentation — stages")]
  [Tooltip("Pause between the reels settling and the win presentation starting, so the " +
           "landed grid is readable before it is darkened.")]
  [SerializeField] private float winPresentationDelay = 1f;

  [Tooltip("Black panel that darkens the whole grid while symbols animate. Non-winning " +
           "cells stay behind it; animating cells are reparented above it.")]
  [SerializeField] private GameObject winDarkenOverlay;

  [Tooltip("Empty RectTransform rendered ABOVE winDarkenOverlay. Animating cells are " +
           "reparented here for the duration of their animation, then put back.")]
  [SerializeField] private RectTransform winAnimationLayer;

  [Tooltip("How many times stage 2 walks the full list of winning lines before the " +
           "presentation settles into its idle loop.")]
  [SerializeField] private int phase2Repeats = 2;

  [Tooltip("Pause between stages, and between repeats of the idle loop.")]
  [SerializeField] private float phaseGapDelay = 1f;

  [Tooltip("Pause after each individual line in stage 2.")]
  [SerializeField] private float phase2LineDelay = 1f;

  [Tooltip("Which column of a winning line carries its payout text. 2 = the 3rd reel.")]
  [SerializeField] private int winTextColumnIndex = 2;

  [Tooltip("The win popup shown between stage 1 and stage 2. Stage 1 keeps looping " +
           "underneath it. Leave this unassigned and no win popup ever plays.")]
  [SerializeField] private WinPopupController winPopup;

  [Tooltip("The free-spin trigger / intro / outro presentation. Cancelled alongside the win " +
           "popup when a new spin kills the presentation, for the same reason: it holds the " +
           "isSpecialWinActive gate the game loop parks on.")]
  [SerializeField] private FreeSpinPresenter freeSpinPresenter;

  [Header("Mystery Locker Reveal")]
  [Tooltip("Closed-locker sprite shown over a Mystery cell from the moment the result " +
           "arrives until the reels stop. Must be fully opaque — the revealed symbol is " +
           "already sitting underneath it.")]
  [SerializeField] private Sprite lockerRestingSprite;

  [Tooltip("The locker's open-and-exit PNG sequence, in order. Played exactly once, on " +
           "every revealing cell at the same time, after the last reel settles.")]
  [SerializeField] private List<Sprite> lockerRevealFrames = new List<Sprite>();

  [Tooltip("ImageAnimation speed for the reveal sequence. Note the frame delay scales with " +
           "frame count, so the total duration grows with the SQUARE of it.")]
  [SerializeField] private float lockerAnimationSpeed = 5f;

  [Tooltip("Beat between the reels settling and the lockers opening, so the player registers " +
           "that a Mystery landed before it reveals. Only ever waited on a spin that actually " +
           "has a Mystery — a normal spin pays nothing for this.")]
  [SerializeField] private float lockerRevealDelay = 1f;

  [Header("Coin Overlays")]
  [Tooltip("The pig / jackpot meter UI. Coins fly to destinations resolved from it, and it " +
           "owns the meter texts they update on arrival.")]
  [SerializeField] private PigMeterController pigMeters;

  [Tooltip("Layer the coins are lifted onto for their flight, so they draw above the reels " +
           "and the meter UI. Must sit above winAnimationLayer in the hierarchy.")]
  [SerializeField] private RectTransform coinFlightLayer;

  [Tooltip("Coin travel speed in world units/second. Every coin moves at THIS speed, so a " +
           "coin on reel 1 and one on reel 5 look like the same object travelling — the far " +
           "one simply takes longer. Set to 0 to fall back to the fixed coinFlightDuration " +
           "below, which makes every coin take the same time regardless of distance.")]
  [SerializeField] private float coinFlightSpeed = 1600f;

  [Tooltip("Floor and ceiling on the derived duration, so a coin that starts almost on its " +
           "pig still reads as a flight and one crossing the whole screen does not drag.")]
  [SerializeField] private float coinMinFlightDuration = 0.35f;
  [SerializeField] private float coinMaxFlightDuration = 1.1f;

  [Tooltip("Fixed flight time, used only when coinFlightSpeed is 0.")]
  [SerializeField] private float coinFlightDuration = 0.8f;

  [Tooltip("Ease applied to a coin's flight.")]
  [SerializeField] private Ease coinFlightEase = Ease.InOutQuad;

  [Tooltip("How long a coin takes to shrink away once it lands.")]
  [SerializeField] private float coinShrinkDuration = 0.2f;

  [Tooltip("Ease on that shrink. InQuad collapses straight down. Note a 'Back' ease " +
           "overshoots at the START of a tween, so InBack would swell the coin ~10% before " +
           "it disappears rather than bouncing on landing.")]
  [SerializeField] private Ease coinShrinkEase = Ease.InQuad;

  [Tooltip("Gap between one coin LAUNCHING and the next. Flights overlap: coin 2 leaves " +
           "while coin 1 may still be in the air, or its jackpot coin still travelling.")]
  [SerializeField] private float coinLaunchStagger = 1f;

  [Tooltip("Beat between a coin reaching its pig and that pig sending its jackpot coin on.")]
  [SerializeField] private float jackpotCoinDelay = 0.15f;

  [Header("Symbol Info Card")]
  [SerializeField] private SymbolInfoCard symbolInfoCard;

  // Indexed by column so a tween always maps back to its own reelTransform.
  private Tween[] reelTweens = new Tween[0];
  private readonly List<Tween> winTweens = new List<Tween>();
  private Coroutine winAnimationCoroutine;

  // True for the whole win presentation, so the darkening panel is raised once and stays up
  // across every stage. Individual stages must NOT own it: with the inter-stage delays at 0
  // the panel would switch off and back on within a frame or two of itself, which reads as
  // a snap. Only the end of the presentation, or the next spin, takes it down.
  private bool winOverlayHeld;

  // The FREE-SPIN presentation's claim on the same panel, tracked separately because it has
  // to survive things winOverlayHeld does not — in particular the spin the free-spin intro
  // starts underneath its own popups, whose StartSpin releases the win hold. Only
  // FadeOutOverlay clears this one.
  private bool featureOverlayHeld;

  // So the "no win popup assigned" error is reported once, not on every winning spin.
  private bool warnedNoWinPopup;

  private Dictionary<int, SymbolWinAnim> winAnimById;

  // allCells minus resultCells, per column. These are the blur-only cells, reshuffled on
  // every loop step so the strip never visibly repeats. Built once from the Inspector
  // wiring — nothing here assumes which strip indices the result cells occupy.
  private List<SlotSymbolView>[] fillerCellsByColumn;

  internal List<List<int>> currentDisplayMatrix;

  // Cells currently covered by a Mystery locker, populated the moment the result arrives and
  // drained when the reveal finishes.
  private readonly List<SlotSymbolView> activeLockers = new List<SlotSymbolView>();

  // Cells showing a coin this spin, in server order, each paired with the plan saying what
  // that coin does to its meter when it lands. Populated the moment the result arrives,
  // drained when the flights finish.
  private readonly List<SlotSymbolView> activeCoinCells = new List<SlotSymbolView>();
  private readonly List<CoinPlan> activeCoinPlans = new List<CoinPlan>();
  private ServerMeters pendingMeters;

  // In-flight coin chains and the jackpot coins they spawned, tracked so a spin interrupting
  // the beat can stop them and destroy the instances instead of leaking them onto the layer.
  private readonly List<Coroutine> coinRoutines = new List<Coroutine>();
  private readonly List<GameObject> spawnedJackpotCoins = new List<GameObject>();

  // Jackpot coins (coinOverlays ids 15-20) during Yellow free spins, in server order. Each is
  // stamped on its cell with the result and flies straight to its jackpot panel to fill a
  // hole. Drained after the pig coin flights.
  private struct JackpotHit
  {
    internal int row;
    internal int col;
    internal string tier;
    internal SlotSymbolView cell;   // null when the coin could not be stamped
  }

  private readonly List<JackpotHit> pendingJackpotHits = new List<JackpotHit>();

  private bool isSpinning;

  /// <summary>Column count, taken from the wired result grid (falls back to config, then 5).</summary>
  private int ReelCount =>
      (resultCells != null && resultCells.Count > 0) ? resultCells.Count
      : (gameManager != null && gameManager.gameConfig != null ? gameManager.gameConfig.reelCount : 5);

  /// <summary>Visible rows per column, taken from the wired result grid.</summary>
  private int RowCount =>
      (resultCells != null && resultCells.Count > 0 && resultCells[0] != null && resultCells[0].cells != null && resultCells[0].cells.Count > 0)
          ? resultCells[0].cells.Count
          : (gameManager != null && gameManager.gameConfig != null ? gameManager.gameConfig.rowCount : 3);

  #region Initialization

  private void Awake()
  {
    BuildWinAnimLookup();
    BuildFillerCellIndex();
    InitializeMatrix();
  }

  /// <summary>
  /// Split each column's strip into "receives the result" and "blur only", from the
  /// Inspector wiring alone. A cell listed in resultCells is excluded from its column's
  /// filler set by reference, so the result cells can sit at any strip indices.
  /// </summary>
  private void BuildFillerCellIndex()
  {
    int columnCount = allCells != null ? allCells.Count : 0;
    fillerCellsByColumn = new List<SlotSymbolView>[columnCount];

    for (int col = 0; col < columnCount; col++)
    {
      var filler = new List<SlotSymbolView>();
      fillerCellsByColumn[col] = filler;

      var strip = allCells[col]?.cells;
      if (strip == null) continue;

      var resultSet = new HashSet<SlotSymbolView>();
      if (resultCells != null && col < resultCells.Count && resultCells[col]?.cells != null)
      {
        foreach (var cell in resultCells[col].cells)
          if (cell != null) resultSet.Add(cell);
      }

      foreach (var cell in strip)
        if (cell != null && !resultSet.Contains(cell)) filler.Add(cell);

      if (resultSet.Count > 0 && filler.Count == strip.Count)
      {
        Debug.LogError(
            $"[SlotView] Column {col}: none of its resultCells appear in allCells. " +
            "allCells must list the whole strip INCLUDING the result cells, or the result " +
            "cells will be overwritten by filler on every loop step.");
      }
    }
  }

  private void Start()
  {
    FreezeColumnLayouts();
    EnforceWinLayerOrder();
    DisableAllOverlays();
    SetupSymbolButtons();

    // Randomise EVERY cell, result cells included — there is no result yet. Deliberately
    // in Start rather than Awake: putting a Spine symbol on a cell calls
    // SkeletonGraphic.Initialize, and component Awake order across GameObjects is not
    // guaranteed, so this waits until every cell's own components are up.
    ShuffleAllCells();
  }

  /// <summary>
  /// Bake the column layouts and then switch them off for the rest of the session.
  ///
  /// Each reel column is driven by a VerticalLayoutGroup + ContentSizeFitter, which is how
  /// the cells get their positions in the Editor. But the win presentation reparents a
  /// winning cell out of its column onto the animation layer, and a layout group reflows
  /// its remaining children the moment one leaves — the whole column would visibly jump.
  /// The layout has already written every cell's anchored position by now and those
  /// positions never change again, so disabling the components freezes the strip exactly
  /// as laid out. Runtime-only; the Editor keeps its layout tooling.
  /// </summary>
  private void FreezeColumnLayouts()
  {
    if (reelTransforms == null) return;

    // Make sure the layout pass has actually run before we take its output away.
    Canvas.ForceUpdateCanvases();

    foreach (var reel in reelTransforms)
    {
      if (reel == null) continue;

      var fitter = reel.GetComponent<ContentSizeFitter>();
      if (fitter != null) fitter.enabled = false;

      var layout = reel.GetComponent<LayoutGroup>();
      if (layout != null) layout.enabled = false;
    }
  }

  /// <summary>
  /// Guarantee the win presentation's render order regardless of how the scene is wired.
  ///
  /// UI draws in sibling order, so the darkening panel must come after the reel columns
  /// and the animation layer must come after the panel. Getting that backwards in the
  /// Inspector is invisible until a win plays and every winning symbol animates BEHIND the
  /// black panel, so it is pinned here instead of trusted.
  /// </summary>
  private void EnforceWinLayerOrder()
  {
    if (winDarkenOverlay == null)
      Debug.LogError("[SlotView] winDarkenOverlay is not assigned — wins will not darken the grid.");

    if (winAnimationLayer == null)
    {
      Debug.LogError("[SlotView] winAnimationLayer is not assigned — winning symbols cannot be " +
                     "lifted above the darkening panel, so the panel stays off during wins.");
      return;
    }

    if (winDarkenOverlay != null && winDarkenOverlay.transform.parent == winAnimationLayer.parent)
      winDarkenOverlay.transform.SetAsLastSibling();

    winAnimationLayer.SetAsLastSibling();
  }

  private void BuildWinAnimLookup()
  {
    winAnimById = new Dictionary<int, SymbolWinAnim>();
    if (symbolWinAnims == null) return;
    foreach (var entry in symbolWinAnims)
    {
      if (entry == null) continue;
      winAnimById[entry.symbolId] = entry;
    }
  }

  private void InitializeMatrix()
  {
    currentDisplayMatrix = new List<List<int>>();
    for (int col = 0; col < ReelCount; col++)
    {
      var column = new List<int>();
      for (int row = 0; row < RowCount; row++) column.Add(0);
      currentDisplayMatrix.Add(column);
    }
  }

  private void DisableAllOverlays()
  {
    SetWinOverlayActive(false);
    HideAllWinLineTexts();
    if (symbolInfoCard) symbolInfoCard.HideCard();
  }

  private void SetupSymbolButtons()
  {
    if (resultCells == null) return;
    for (int col = 0; col < resultCells.Count; col++)
    {
      var column = resultCells[col];
      if (column == null || column.cells == null) continue;
      for (int row = 0; row < column.cells.Count; row++)
      {
        var cell = column.cells[row];
        if (cell == null) continue;

        // The handler lives on the cell ROOT, not on the icon: the icon GameObject is
        // switched off whenever the cell is showing a Spine symbol, which would kill the
        // tap target for ids 0-2. Both Image and SkeletonGraphic are Graphics, so a
        // raycast hits whichever child is active and the pointer event bubbles up here.
        var handler = cell.GetComponent<SymbolButtonHandler>();
        if (handler == null) handler = cell.gameObject.AddComponent<SymbolButtonHandler>();
        handler.Init(col, row, this);
      }
    }
  }

  #endregion

  #region Symbol lookup

  private Sprite GetSymbolSprite(int symbolId)
  {
    if (symbolSprites == null || symbolSprites.Length == 0)
    {
      Debug.LogError("[SlotView] symbolSprites is empty — assign one sprite per symbol id in the Inspector.");
      return null;
    }

    if (symbolId < 0 || symbolId >= symbolSprites.Length)
    {
      Debug.LogWarning($"[SlotView] symbolId {symbolId} is outside symbolSprites (length {symbolSprites.Length}).");
      return symbolSprites[0];
    }

    if (symbolSprites[symbolId] == null)
    {
      Debug.LogError($"[SlotView] Sprite for symbol id {symbolId} is not assigned.");
      return symbolSprites[0];
    }

    return symbolSprites[symbolId];
  }

  /// <summary>
  /// Spine skeleton for this id, or null when the id is a plain sprite. Never logs —
  /// a null here is the normal case for every id except 0/1/2.
  /// </summary>
  private Spine.Unity.SkeletonDataAsset GetSymbolSkeleton(int symbolId)
  {
    if (symbolSkeletons == null || symbolId < 0 || symbolId >= symbolSkeletons.Length) return null;
    return symbolSkeletons[symbolId];
  }

  /// <summary>
  /// Put <paramref name="symbolId"/> on a cell, letting SlotSymbolView pick between its
  /// sprite and its Spine child. The single place symbols reach the screen.
  /// </summary>
  private void ApplySymbol(SlotSymbolView cell, int symbolId)
  {
    if (cell == null) return;

    Spine.Unity.SkeletonDataAsset skeleton = GetSymbolSkeleton(symbolId);

    // Only look up a sprite when there is no skeleton — GetSymbolSprite logs an error for
    // an unassigned id, and ids 0/1/2 are legitimately sprite-less.
    Sprite sprite = skeleton != null ? null : GetSymbolSprite(symbolId);

    cell.SetSymbol(symbolId, sprite, skeleton);
  }

  /// <summary>A random id that is allowed to appear as blur filler during the spin.</summary>
  private int RandomFillerSymbolId()
  {
    int[] pool = RichPiggiesSymbols.SpinFillerSymbolIds;

    return pool[Random.Range(0, pool.Length)];
  }

  private SlotSymbolView ResultCell(int col, int row)
  {
    if (resultCells == null || col < 0 || col >= resultCells.Count) return null;
    var column = resultCells[col];
    if (column == null || column.cells == null || row < 0 || row >= column.cells.Count) return null;
    return column.cells[row];
  }

  #endregion

  #region Result population

  /// <summary>
  /// Write the server result into the visible cells. Called WHILE the reels are still
  /// looping — the cells are somewhere off-screen at that moment, so by the time the
  /// stop tween parks them at restY they already read correctly.
  /// </summary>
  internal void PopulateResultMatrix(List<List<int>> resultMatrix)
  {
    if (resultMatrix == null)
    {
      Debug.LogError("[SlotView] PopulateResultMatrix received a null matrix.");
      return;
    }

    currentDisplayMatrix = resultMatrix;

    for (int col = 0; col < resultMatrix.Count && col < ReelCount; col++)
    {
      var column = resultMatrix[col];
      if (column == null) continue;

      for (int row = 0; row < column.Count && row < RowCount; row++)
      {
        int symbolId = column[row];
        var cell = ResultCell(col, row);
        if (cell == null)
        {
          Debug.LogError($"[SlotView] resultCells has no cell at col {col}, row {row} — check the Inspector wiring.");
          continue;
        }
        ApplySymbol(cell, symbolId);
      }
    }
  }

  /// <summary>Place a matrix with no spin at all (init / reconnect).</summary>
  internal void SetInitialMatrix(List<List<int>> matrix)
  {
    if (matrix == null) return;

    ShuffleAllCells();
    PopulateResultMatrix(matrix);

    if (reelTransforms == null) return;
    for (int col = 0; col < reelTransforms.Length; col++)
    {
      if (reelTransforms[col] == null) continue;
      var p = reelTransforms[col].localPosition;
      reelTransforms[col].localPosition = new Vector3(p.x, restY, p.z);
    }
  }

  /// <summary>
  /// Randomise EVERY cell of every strip, result cells included. Only valid when there
  /// is no result on the reels yet — init, and the moment a new spin starts.
  /// </summary>
  private void ShuffleAllCells()
  {
    if (allCells == null) return;
    foreach (var column in allCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
      {
        if (cell == null) continue;
        ApplySymbol(cell, RandomFillerSymbolId());
      }
    }
  }

  /// <summary>
  /// Randomise every cell of one column, result cells included. Only call while that
  /// column's strip is off-screen and carries no live result.
  /// </summary>
  private void ShuffleColumnAll(int col)
  {
    if (allCells == null || col < 0 || col >= allCells.Count) return;

    var strip = allCells[col]?.cells;
    if (strip == null) return;

    for (int i = 0; i < strip.Count; i++)
    {
      if (strip[i] == null) continue;
      ApplySymbol(strip[i], RandomFillerSymbolId());
    }
  }

  /// <summary>
  /// Randomise one column's blur-only cells, leaving its result cells untouched. Fired
  /// from the looping tween's OnStepComplete, i.e. once per downward pass, so the strip
  /// shows fresh symbols every wrap instead of visibly repeating the same order. Safe to
  /// call after PopulateResultMatrix — the result cells are excluded by reference.
  /// </summary>
  private void ShuffleColumnFiller(int col)
  {
    if (fillerCellsByColumn == null) BuildFillerCellIndex();
    if (col < 0 || col >= fillerCellsByColumn.Length) return;

    var filler = fillerCellsByColumn[col];
    if (filler == null) return;

    for (int i = 0; i < filler.Count; i++)
    {
      ApplySymbol(filler[i], RandomFillerSymbolId());
    }
  }

  private void ResetAllCellVisuals()
  {
    if (allCells == null) return;
    foreach (var column in allCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
        if (cell != null) cell.ResetVisualState();
    }
  }

  #endregion

  #region Spin

  /// <summary>Duration needed to travel between two Y positions at reelSpeed.</summary>
  private float DurationFor(float fromY, float toY)
      => Mathf.Abs(toY - fromY) / Mathf.Max(reelSpeed, 0.0001f);

  /// <summary>
  /// Start every column spinning. Yields until the first and last columns have finished
  /// their intro pass and handed over to the infinite loop, so the caller knows the
  /// reels are genuinely in motion before it starts waiting on the network.
  /// </summary>
  internal IEnumerator StartSpin()
  {
    if (symbolInfoCard != null) symbolInfoCard.HideCard();

    // KillWinTweens first: the win presentation's stage 3 is an infinite loop that would
    // otherwise keep reparenting cells and re-showing the darkening overlay right through
    // the new spin. It also returns every cell to its home slot.
    KillWinTweens();
    KillAllTweens();
    DisableAllOverlays();
    ResetAllCellVisuals();

    // ResetAllCellVisuals already hid any locker still up from an interrupted reveal; this
    // drops our claim on those cells so the new round starts from an empty list.
    activeLockers.Clear();

    // Coins need more than the cell reset: a chain interrupted mid-flight leaves a running
    // coroutine and possibly an instantiated jackpot coin behind.
    StopCoinFlights();

    // Deliberately NOT shuffling here. At this moment the column is parked at restY with
    // the previous result on screen; overwriting it now would pop the symbols before the
    // reel has moved. The intro pass carries the old result off-screen first, and each
    // column shuffles its whole strip at the handover point instead (see StartColumn).
    isSpinning = true;

    int count = reelTransforms != null ? reelTransforms.Length : 0;
    reelTweens = new Tween[count];

    var intro = new List<Tween>(count);
    for (int col = 0; col < count; col++)
      intro.Add(StartColumn(col, col * reelStartStagger));

    if (intro.Count == 0) yield break;

    yield return intro[0].WaitForCompletion();
    yield return intro[intro.Count - 1].WaitForCompletion();
  }

  /// <summary>
  /// Intro pass: travel from wherever the column is resting down to spinBottomY, then
  /// wrap to spinTopY and hand over to an infinite Restart loop.
  /// </summary>
  private Tween StartColumn(int col, float delay)
  {
    RectTransform reel = reelTransforms[col];
    if (reel == null) return DOTween.Sequence();

    float startY = reel.localPosition.y;

    Sequence seq = DOTween.Sequence();
    if (delay > 0f) seq.AppendInterval(delay);

    seq.Append(reel.DOLocalMoveY(spinBottomY, DurationFor(startY, spinBottomY)).SetEase(Ease.Linear));

    seq.AppendCallback(() =>
    {
      if (!isSpinning) return;

      var p = reel.localPosition;
      reel.localPosition = new Vector3(p.x, spinTopY, p.z);

      // Strip is fully off-screen here (it has just wrapped from bottom to top), so this
      // is the safe moment to wipe the previous result. Whole strip this time, result
      // cells included — the new result has not arrived yet. Every later pass reshuffles
      // filler only, via OnStepComplete below.
      ShuffleColumnAll(col);

      // OnStepComplete fires at the end of every loop step — the instant the column has
      // travelled spinTopY -> spinBottomY and is about to snap back up. That is the only
      // moment the whole strip is off-screen, so reshuffling the blur cells here is
      // invisible and keeps every pass looking different.
      reelTweens[col] = reel
          .DOLocalMoveY(spinBottomY, DurationFor(spinTopY, spinBottomY))
          .SetLoops(-1, LoopType.Restart)
          .SetEase(Ease.Linear)
          .OnStepComplete(() => ShuffleColumnFiller(col));
    });

    return seq;
  }

  /// <summary>
  /// Stop every column. The result must already have been written by
  /// <see cref="PopulateResultMatrix"/>.
  /// </summary>
  /// <param name="immediate">Land all columns at once (turbo / Stop button) instead of staggered.</param>
  /// <param name="turbo">Use the shorter turbo stagger when not immediate.</param>
  /// <param name="onColumnLand">Invoked once per column as it lands (reel-stop sfx), or once total when immediate.</param>
  internal IEnumerator StopSpin(bool immediate, bool turbo, System.Action onColumnLand, System.Action onComplete)
  {
    int count = reelTransforms != null ? reelTransforms.Length : 0;
    if (count == 0)
    {
      isSpinning = false;
      // Nothing will ever land, so nothing can open the lockers or launch the coins — do not
      // leave either covering the grid through the win presentation. The meters still take
      // the server's values, since those are true whether or not anything animated.
      ClearMysteryLockers();
      if (pigMeters != null && pendingMeters != null) pigMeters.ResyncTo(pendingMeters);
      StopCoinFlights();
      onComplete?.Invoke();
      yield break;
    }

    float stagger = turbo ? turboStopStagger : reelStopStagger;
    bool playedStopOnce = false;

    for (int col = 0; col < count; col++)
    {
      StopColumn(col);

      if (!immediate)
      {
        onColumnLand?.Invoke();
        yield return new WaitForSecondsRealtime(stagger);
      }
      else if (!playedStopOnce)
      {
        onColumnLand?.Invoke();
        playedStopOnce = true;
      }
    }

    // Wait on the last column's settle tween. Guarded because Kill() can null it out.
    Tween last = reelTweens.Length > 0 ? reelTweens[reelTweens.Length - 1] : null;
    if (last != null && last.IsActive()) yield return last.WaitForCompletion();

    KillAllTweens();
    isSpinning = false;

    // Gate onComplete behind the landing beats. GameManager yields on this whole coroutine,
    // so the balance update, the return to Idle and the win presentation all wait for the
    // Mystery reveal without GameManager needing to know it exists.
    yield return StartCoroutine(PlayStopAnimations());

    onComplete?.Invoke();
  }

  private void StopColumn(int col)
  {
    RectTransform reel = reelTransforms[col];
    if (reel == null) return;

    reelTweens[col]?.Kill();

    var p = reel.localPosition;
    reel.localPosition = new Vector3(p.x, spinTopY, p.z);

    reelTweens[col] = reel
        .DOLocalMoveY(restY, DurationFor(spinTopY, restY))
        .SetEase(Ease.OutBack, stopOvershoot);
  }

  /// <summary>
  /// "Symbol reacts as it lands" beats, run after the last column settles and before the
  /// caller's onComplete. Currently just the Mystery reveal; wild flashes and piggy jumps
  /// belong here too when they arrive.
  /// </summary>
  private IEnumerator PlayStopAnimations()
  {
    yield return StartCoroutine(PlayMysteryReveal());
    yield return StartCoroutine(PlayCoinFlights());
    yield return StartCoroutine(PlayJackpotCollections());
  }

  #endregion

  #region Mystery locker reveal

  /// <summary>
  /// Cover every cell named by <paramref name="reveals"/> with a closed locker.
  ///
  /// Called the moment the result arrives, alongside <see cref="PopulateResultMatrix"/> and
  /// so WHILE the reels are still looping: the lockers ride down with the strip and are
  /// already in place when the column parks at restY. Nothing about the symbol underneath
  /// changes — the server's matrix already holds the revealed symbol at these positions, so
  /// the locker is purely a cover that opens later.
  /// </summary>
  internal void ShowMysteryLockers(List<ServerMysteryReveal> reveals)
  {
    activeLockers.Clear();
    if (reveals == null || reveals.Count == 0) return;

    foreach (var reveal in reveals)
    {
      if (reveal?.position == null || reveal.position.Count < 2)
      {
        Debug.LogError("[SlotView] mysteryReveals entry has no usable [row, col] position.", this);
        continue;
      }

      // Server order is [row, col] — the same grid as the matrix, which is stored the other
      // way round on the client.
      int row = reveal.position[0];
      int col = reveal.position[1];

      if (col < 0 || col >= ReelCount || row < 0 || row >= RowCount)
      {
        Debug.LogError($"[SlotView] Mystery reveal at row {row}, col {col} is outside the " +
                       $"{ReelCount}x{RowCount} grid.", this);
        continue;
      }

      var cell = ResultCell(col, row);
      if (cell == null)
      {
        Debug.LogError($"[SlotView] resultCells has no cell at col {col}, row {row} for a " +
                       "Mystery reveal — check the Inspector wiring.", this);
        continue;
      }

      if (!cell.ShowLocker(lockerRestingSprite))
      {
        Debug.LogError($"[SlotView] Cell at col {col}, row {row} has no locker child, so the " +
                       "Mystery reveal cannot be shown there. Check the SlotIcon prefab.", this);
        continue;
      }

      activeLockers.Add(cell);
    }
  }

  /// <summary>
  /// Open every locker at once and wait for all of them. Costs nothing on a spin with no
  /// Mystery, which is the common case.
  /// </summary>
  private IEnumerator PlayMysteryReveal()
  {
    if (activeLockers.Count == 0) yield break;

    // Hold on the landed grid with the lockers still closed, so the reveal reads as its own
    // beat rather than as part of the reels stopping.
    if (lockerRevealDelay > 0f)
      yield return new WaitForSeconds(lockerRevealDelay);

    var staged = new List<SlotSymbolView>(activeLockers.Count);
    int pending = 0;

    foreach (var cell in activeLockers)
    {
      if (cell == null) continue;

      // A cell may only report once. ImageAnimation raises onLoopComplete from inside its
      // own Invoke chain, and a double decrement would end the beat early.
      bool reported = false;
      System.Action onFinished = () =>
      {
        if (reported) return;
        reported = true;
        pending--;
      };

      if (!cell.StageLockerReveal(lockerRevealFrames, lockerAnimationSpeed, onFinished))
      {
        Debug.LogError("[SlotView] A Mystery locker could not stage its reveal — check " +
                       "lockerRevealFrames on SlotView and the locker wiring on SlotIcon. " +
                       "The cell will be uncovered without animating.", this);
        cell.HideLocker();
        continue;
      }

      pending++;
      staged.Add(cell);
    }

    // Every locker opens on the SAME frame, so the reveal reads as one beat.
    foreach (var cell in staged) cell.StartStagedLocker();

    // No safety timeout, for the same reason the win stage has none: a locker that never
    // reports is a bug to be found, and a fallback would hide it by cutting the beat short.
    while (pending > 0) yield return null;

    ClearMysteryLockers();
  }

  /// <summary>Take every locker down and forget them. Idempotent.</summary>
  internal void ClearMysteryLockers()
  {
    foreach (var cell in activeLockers)
      if (cell != null) cell.HideLocker();

    activeLockers.Clear();
  }

  #endregion

  #region Coin overlays

  /// <summary>
  /// Stamp a coin on every cell named by <paramref name="coinOverlays"/> and work out, from
  /// the meter diff, what each one will do when it lands.
  ///
  /// Called alongside <see cref="PopulateResultMatrix"/> and <see cref="ShowMysteryLockers"/>
  /// the moment the result arrives, so WHILE the reels are still looping. The coin child
  /// sits BELOW the locker in sibling order, so a coin on a Mystery cell is hidden until
  /// that locker opens and appears as part of the reveal. A coin on a plain cell — which
  /// BackendResponses.md section 4 shows does happen — is simply visible when the column
  /// parks.
  /// </summary>
  internal void ShowCoinOverlays(List<ServerCoinOverlay> coinOverlays, ServerMeters meters)
  {
    ClearCoinOverlays();
    ClearJackpotHits();
    pendingMeters = meters;

    if (coinOverlays == null || coinOverlays.Count == 0) return;

    // Jackpot coins (15-20, Yellow free spins) fly straight to their jackpot panel and never
    // touch a pig or a meter diff, so they are split off before the pig coin plans are built.
    coinOverlays = StampJackpotCoins(coinOverlays);
    if (coinOverlays.Count == 0) return;

    if (pigMeters == null)
    {
      Debug.LogError("[SlotView] Coins landed but pigMeters is not assigned — no meter UI to " +
                     "fly them to. Assign PigMeterController in the Inspector.", this);
      return;
    }

    if (coinFlightLayer == null)
    {
      // Without a layer to lift onto, a coin would fly while still parented inside its reel
      // column and be clipped away by the mask the moment it left the cell.
      Debug.LogError("[SlotView] Coins landed but coinFlightLayer is not assigned — they " +
                     "would be clipped by the reel mask. Assign it in the Inspector.", this);
      return;
    }

    // The server now states what each coin did (action / meterValueAfter / addedToJackpot).
    // CoinPlanBuilder reads that where it is present and falls back to diffing the meters —
    // loudly — where it is not.
    var plans = CoinPlanBuilder.Build(pigMeters.CachedMeters, meters, coinOverlays);

    foreach (var plan in plans)
    {
      if (plan.col < 0 || plan.col >= ReelCount || plan.row < 0 || plan.row >= RowCount)
      {
        Debug.LogError($"[SlotView] Coin overlay at row {plan.row}, col {plan.col} is outside " +
                       $"the {ReelCount}x{RowCount} grid.", this);
        continue;
      }

      var cell = ResultCell(plan.col, plan.row);
      if (cell == null)
      {
        Debug.LogError($"[SlotView] resultCells has no cell at col {plan.col}, row {plan.row} " +
                       "for a coin overlay — check the Inspector wiring.", this);
        continue;
      }

      if (!cell.ShowCoin(pigMeters.CoinFrames(plan.coinSymbolId)))
      {
        Debug.LogError($"[SlotView] Cell at col {plan.col}, row {plan.row} has no coin child, " +
                       "so its coin cannot be shown. Check the SlotIcon prefab.", this);
        continue;
      }

      activeCoinCells.Add(cell);
      activeCoinPlans.Add(plan);
    }
  }

  /// <summary>
  /// Send every revealed coin to its pig, then let the yellow ones pass a jackpot coin on.
  ///
  /// Launches are staggered but flights OVERLAP: coin 2 leaves while coin 1 may still be in
  /// the air or its jackpot coin still travelling. The running counter is what holds the
  /// game loop — this coroutine does not return until the last coin of the last chain has
  /// landed, and because StopSpin yields on it before invoking onComplete, the balance
  /// update, the return to Idle and the win-line presentation all wait behind it.
  ///
  /// Costs nothing on a spin with no coins, which is the common case.
  /// </summary>
  private IEnumerator PlayCoinFlights()
  {
    if (activeCoinCells.Count == 0)
    {
      // Even with no coins the server may have moved a meter (a feature resetting one, say).
      // Take its word for it rather than leaving the display on a stale value.
      if (pigMeters != null && pendingMeters != null) pigMeters.ResyncTo(pendingMeters);
      pendingMeters = null;
      yield break;
    }

    int running = 0;

    for (int i = 0; i < activeCoinCells.Count; i++)
    {
      var cell = activeCoinCells[i];
      var plan = activeCoinPlans[i];
      if (cell == null || plan == null) continue;

      running++;
      coinRoutines.Add(StartCoroutine(RunCoin(cell, plan, () => running--)));

      // Gates the LAUNCH only — the previous coin is deliberately still travelling.
      if (i < activeCoinCells.Count - 1 && coinLaunchStagger > 0f)
        yield return new WaitForSeconds(coinLaunchStagger);
    }

    while (running > 0) yield return null;

    // Hard resync: whatever the allocator guessed, the meters end on the server's values.
    if (pigMeters != null && pendingMeters != null) pigMeters.ResyncTo(pendingMeters);

    ClearCoinOverlays();
    pendingMeters = null;
  }

  /// <summary>
  /// One coin's whole chain: fly to its pig, make it jump, apply its meter change, and for a
  /// yellow coin that awarded a tier, send a jackpot coin on to that tier's payout text.
  /// </summary>
  private IEnumerator RunCoin(SlotSymbolView cell, CoinPlan plan, System.Action onDone)
  {
    int coinId = plan.coinSymbolId;

    cell.DetachCoin(coinFlightLayer);

    // The coin has been sitting on the grid on its idle frame since the reveal, through the
    // launch stagger. It only spins while it is actually travelling.
    cell.StartCoinAnimation(pigMeters.CoinFrames(coinId), pigMeters.CoinAnimationSpeed);

    yield return StartCoroutine(CoinFlyer.Fly(
        cell.CoinRect,
        () => pigMeters.PigTarget(coinId),
        FlightDurationFor(cell.CoinRect, pigMeters.PigTarget(coinId)),
        coinFlightEase, coinShrinkDuration, coinShrinkEase,

        // On touchdown, not after the shrink: the coin settles back onto its idle frame while
        // the pig reacts and the meter ticks, so the catch reads as one event rather than
        // three in sequence.
        onTouchdown: () =>
        {
          cell.StopCoinAnimation();
          pigMeters.PlayPigJump(coinId);

          if (plan.movesMeter)
          {
            if (coinId == RichPiggiesSymbols.BlueCoin) pigMeters.SetBlueText(plan.meterValueAfter);
            else if (coinId == RichPiggiesSymbols.RedCoin) pigMeters.SetRedText(plan.meterValueAfter);

            if (coinId == RichPiggiesSymbols.BlueCoin || coinId == RichPiggiesSymbols.RedCoin)
              pigMeters.PlayMeterEffect(coinId);
          }
        },
        onArrive: null));

    cell.ReturnCoinHome();

    foreach (var award in plan.jackpotAwards)
    {
      if (jackpotCoinDelay > 0f) yield return new WaitForSeconds(jackpotCoinDelay);
      yield return StartCoroutine(RunJackpotCoin(coinId, award));
    }

    onDone?.Invoke();
  }

  /// <summary>
  /// The second leg of a yellow coin: a tier-coloured coin leaves the Yellow Pig and lands on
  /// its jackpot payout, which ticks up as it arrives.
  /// </summary>
  private IEnumerator RunJackpotCoin(int fromCoinId, JackpotAward award)
  {
    var origin = pigMeters.PigTarget(fromCoinId);
    var coin = pigMeters.SpawnJackpotCoin(award.tier, origin, coinFlightLayer);
    if (coin != null) spawnedJackpotCoins.Add(coin.gameObject);

    if (coin == null)
    {
      // The flight could not be staged, but the meter still moved on the server — show it
      // rather than silently holding the old number.
      pigMeters.SetJackpotText(award.tier, award.valueAfter);
      pigMeters.PlayJackpotShine(award.tier);
      yield break;
    }

    string tier = award.tier;
    double valueAfter = award.valueAfter;

    pigMeters.StartJackpotCoinAnimation(coin, tier);

    yield return StartCoroutine(CoinFlyer.Fly(
        coin,
        () => pigMeters.JackpotTarget(tier),
        FlightDurationFor(coin, pigMeters.JackpotTarget(tier)),
        coinFlightEase, coinShrinkDuration, coinShrinkEase,

        // Same beat as the pig: the coin settles onto its idle frame and the payout ticks up
        // as it collapses into the meter.
        onTouchdown: () =>
        {
          pigMeters.StopJackpotCoinAnimation(coin);
          pigMeters.SetJackpotText(tier, valueAfter);
          pigMeters.PlayJackpotShine(tier);
        },
        onArrive: null));

    spawnedJackpotCoins.Remove(coin.gameObject);
    Destroy(coin.gameObject);
  }

  /// <summary>
  /// How long this particular coin should take, from how far it actually has to go.
  ///
  /// Same reasoning as the reels' <see cref="DurationFor"/>: a constant duration would mean a
  /// coin on reel 1 and a coin on reel 5 travel at wildly different speeds to land together,
  /// which reads as two different objects. A constant SPEED keeps them looking like the same
  /// coin. Measured in world space, so the portrait layout's smaller slot scale is accounted
  /// for without a second set of numbers.
  ///
  /// The distance is sampled once, at launch. A destination that moves mid-flight (a rotation)
  /// bends the path without re-timing it — the coin just covers the new distance a little
  /// faster or slower, which is far less jarring than a duration that changes underneath it.
  /// </summary>
  private float FlightDurationFor(RectTransform coin, RectTransform target)
  {
    if (coinFlightSpeed <= 0f || coin == null || target == null) return coinFlightDuration;

    float distance = Vector3.Distance(coin.position, target.position);
    return Mathf.Clamp(distance / coinFlightSpeed, coinMinFlightDuration, coinMaxFlightDuration);
  }

  /// <summary>Send every coin home hidden and forget the round's plans. Idempotent.</summary>
  internal void ClearCoinOverlays()
  {
    foreach (var cell in activeCoinCells)
      if (cell != null) cell.ReturnCoinHome();

    activeCoinCells.Clear();
    activeCoinPlans.Clear();
    coinRoutines.Clear();
  }

  /// <summary>
  /// Abandon a coin beat that a new spin interrupted. Stops every chain still running,
  /// destroys the jackpot coins they spawned — those are instances, not cached children, and
  /// would otherwise sit on the flight layer forever — and sends the symbol coins home.
  /// </summary>
  private void StopCoinFlights()
  {
    foreach (var routine in coinRoutines)
      if (routine != null) StopCoroutine(routine);

    foreach (var coin in spawnedJackpotCoins)
      if (coin != null) Destroy(coin);

    spawnedJackpotCoins.Clear();
    ClearCoinOverlays();
    pendingMeters = null;
    ClearJackpotHits();
  }

  #endregion

  #region Yellow jackpot collection

  /// <summary>
  /// Split the jackpot coins (ids 15-20, Yellow free spins) out of this spin's coinOverlays,
  /// stamp each one on its cell's coin child and queue its flight. Returns the remaining pig
  /// coins for the normal plan builder.
  ///
  /// Stamped the moment the result arrives, like the pig coins: the coin child sits below the
  /// locker, so a jackpot coin on a Mystery cell appears as part of the reveal.
  /// </summary>
  private List<ServerCoinOverlay> StampJackpotCoins(List<ServerCoinOverlay> coinOverlays)
  {
    var pigCoins = new List<ServerCoinOverlay>(coinOverlays.Count);

    foreach (var overlay in coinOverlays)
    {
      string tier = overlay != null ? JackpotTierOf(overlay) : null;
      if (tier == null)
      {
        pigCoins.Add(overlay);
        continue;
      }

      if (overlay.position == null || overlay.position.Count < 2)
      {
        Debug.LogError($"[SlotView] Jackpot coin \"{tier}\" has no usable [row, col] position.", this);
        continue;
      }

      int row = overlay.position[0];
      int col = overlay.position[1];
      var cell = (col >= 0 && col < ReelCount && row >= 0 && row < RowCount) ? ResultCell(col, row) : null;

      if (pigMeters == null || cell == null || !cell.ShowCoin(pigMeters.JackpotCoinFrames(tier)))
      {
        Debug.LogError($"[SlotView] Jackpot coin \"{tier}\" at row {row}, col {col} could not be " +
                       "shown on its cell (pigMeters, resultCells or the SlotIcon coin child is " +
                       "missing). Its hole will still fill.", this);
        pendingJackpotHits.Add(new JackpotHit { row = row, col = col, tier = tier, cell = null });
        continue;
      }

      pendingJackpotHits.Add(new JackpotHit { row = row, col = col, tier = tier, cell = cell });
    }

    return pigCoins;
  }

  /// <summary>Tier key for a jackpot coin overlay, or null for a pig coin.</summary>
  private static string JackpotTierOf(ServerCoinOverlay overlay)
  {
    string tier = RichPiggiesSymbols.JackpotTierName(overlay.coinId);
    if (tier != null) return tier;

    // coinId is authoritative; fall back to the name ("Grand" / "GrandCoin") only if the
    // server ever omits it.
    if (string.IsNullOrEmpty(overlay.coin)) return null;
    string name = overlay.coin.EndsWith("Coin", System.StringComparison.OrdinalIgnoreCase)
        ? overlay.coin.Substring(0, overlay.coin.Length - 4) : overlay.coin;

    foreach (int id in RichPiggiesSymbols.JackpotSymbolIds)
    {
      string candidate = RichPiggiesSymbols.JackpotTierName(id);
      if (string.Equals(candidate, name, System.StringComparison.OrdinalIgnoreCase)) return candidate;
    }

    return null;
  }

  /// <summary>
  /// Fly every stamped jackpot coin straight to its jackpot panel, filling the next hole on
  /// touchdown. Staggered and overlapping like the pig coins; returns once the last one has
  /// landed. Any tier the server paid this spin is then topped up to full, so the win popup
  /// that follows always sits over a completed meter.
  /// </summary>
  private IEnumerator PlayJackpotCollections()
  {
    if (pendingJackpotHits.Count > 0 && pigMeters != null && coinFlightLayer != null)
    {
      int running = 0;

      for (int i = 0; i < pendingJackpotHits.Count; i++)
      {
        var hit = pendingJackpotHits[i];

        running++;
        coinRoutines.Add(StartCoroutine(RunJackpotHit(hit, () => running--)));

        if (i < pendingJackpotHits.Count - 1 && coinLaunchStagger > 0f)
          yield return new WaitForSeconds(coinLaunchStagger);
      }

      while (running > 0) yield return null;
    }
    else if (pendingJackpotHits.Count > 0)
    {
      Debug.LogError("[SlotView] Jackpot coins landed but pigMeters or coinFlightLayer is " +
                     "unassigned, so none can fly.", this);
    }

    ClearJackpotHits();

    if (pigMeters != null)
      foreach (string tier in pigMeters.PendingAwardedTiers)
        pigMeters.ForceFillTier(tier);
  }

  /// <summary>
  /// One jackpot coin: lift it off its cell, fly it to its tier's panel, blast + fill a hole
  /// on touchdown, send it home hidden.
  /// </summary>
  private IEnumerator RunJackpotHit(JackpotHit hit, System.Action onDone)
  {
    string tier = hit.tier;

    // Reserved before launch so overlapping flights fill holes in launch order.
    int holeIndex = pigMeters.ReserveHole(tier);

    var cell = hit.cell;
    if (cell == null || cell.CoinRect == null)
    {
      // Nothing to fly, but the hit still counts on the server — show it.
      pigMeters.PlayHoleFill(tier, holeIndex);
      onDone?.Invoke();
      yield break;
    }

    var frames = pigMeters.JackpotCoinFrames(tier);

    cell.DetachCoin(coinFlightLayer);
    cell.StartCoinAnimation(frames, pigMeters.CoinAnimationSpeed);

    yield return StartCoroutine(CoinFlyer.Fly(
        cell.CoinRect,
        () => pigMeters.JackpotTarget(tier),
        FlightDurationFor(cell.CoinRect, pigMeters.JackpotTarget(tier)),
        coinFlightEase, coinShrinkDuration, coinShrinkEase,
        onTouchdown: () =>
        {
          cell.StopCoinAnimation();
          pigMeters.PlayHoleFill(tier, holeIndex);
        },
        onArrive: null));

    cell.ReturnCoinHome();

    onDone?.Invoke();
  }

  /// <summary>Send every stamped jackpot coin home hidden and forget them. Idempotent.</summary>
  private void ClearJackpotHits()
  {
    foreach (var hit in pendingJackpotHits)
      if (hit.cell != null) hit.cell.ReturnCoinHome();

    pendingJackpotHits.Clear();
  }

  #endregion

  #region Win Line Animation

  /// <summary>
  /// Present the round's wins. Three stages, then an idle loop that runs until the next
  /// spin kills this coroutine:
  ///
  ///   STAGE 1  every winning cell at once, no payout text
  ///   POPUP    the win popup fades in over stage 1 repeating underneath it
  ///   STAGE 2  each line in turn, phase2Repeats times, payout text on its 3rd-reel cell
  ///   STAGE 3  stage 1 on repeat, forever
  ///
  /// <paramref name="onComplete"/> fires at the end of stage 1 so the game loop can return
  /// to Idle while the rest plays out — but the popup, if there is one, is already holding
  /// the loop by then through UIManager.isSpecialWinActive. Autoplay and free spins get the
  /// popup and then stop; they never see stages 2 and 3.
  /// </summary>
  internal void ShowWinLineAnimation(List<WinLine> winLines, System.Action onComplete)
  {
    if (winLines == null || winLines.Count == 0)
    {
      onComplete?.Invoke();
      return;
    }

    KillWinTweens();
    winAnimationCoroutine = StartCoroutine(PlayWinPresentation(winLines, onComplete));
  }

  /// <summary>
  /// A win with no paylines behind it — a jackpot award, the trigger's 1x stake — still gets
  /// the tiered win popup. Same gate ordering as <see cref="PlayWinPresentation"/>: Show()
  /// raises isSpecialWinActive before onComplete lets the game loop check it.
  /// </summary>
  internal void ShowWinPopupOnly(double winAmount, System.Action onComplete)
  {
    KillWinTweens();
    winAnimationCoroutine = StartCoroutine(PlayWinPopupOnly(winAmount, onComplete));
  }

  private IEnumerator PlayWinPopupOnly(double winAmount, System.Action onComplete)
  {
    if (winPresentationDelay > 0f)
      yield return new WaitForSeconds(winPresentationDelay);

    double totalPay = gameManager != null ? gameManager.GetTotalPay() : 0;

    if (winPopup != null && winPopup.ShouldShow(winAmount, totalPay))
      winPopup.Show(winAmount, totalPay, null);
    else if (winPopup == null && !warnedNoWinPopup)
    {
      warnedNoWinPopup = true;
      Debug.LogError("[SlotView] This round won but no win popup played: the 'Win Popup' " +
                     "field on SlotView is unassigned.", this);
    }

    winAnimationCoroutine = null;
    onComplete?.Invoke();
  }

  private IEnumerator PlayWinPresentation(List<WinLine> winLines, System.Action onComplete)
  {
    // Let the landed grid be read before anything lights up. The reels have only just
    // settled at this point, so going straight into the darkening panel gives the player
    // no beat to see what they actually landed.
    if (winPresentationDelay > 0f)
      yield return new WaitForSeconds(winPresentationDelay);

    // ---- STAGE 1: everything at once -------------------------------------------------
    var allWinPositions = new HashSet<int>();
    foreach (var winLine in winLines)
    {
      if (winLine.positions == null) continue;
      foreach (int flatIndex in winLine.positions) allWinPositions.Add(flatIndex);
    }

    // The round total, for the win popup below.
    // The server's payload.winAmount is already the whole spin — lines plus any jackpot — and
    // is what lands in the win field, so the popup shows exactly that.
    double totalWinAmount = gameManager != null && gameManager.lastResult != null
        ? gameManager.lastResult.winAmount : 0;

    AudioManager.Instance?.PlayWinLinePhase1Start();

    // Raise the darkening panel once, for the whole presentation. See winOverlayHeld.
    if (winAnimationLayer != null)
    {
      winOverlayHeld = true;
      SetWinOverlayActive(true);
    }

    yield return StartCoroutine(AnimateWinPositions(allWinPositions));
    yield return new WaitForSeconds(phaseGapDelay);

    // ---- WIN POPUP: fades in over stage 1 repeating underneath --------------------------
    double totalPay = gameManager != null ? gameManager.GetTotalPay() : 0;
    bool showPopup = false;
    bool popupDone = false;

    if (winPopup != null)
    {
      showPopup = winPopup.ShouldShow(totalWinAmount, totalPay);
    }
    else if (totalWinAmount > 0 && !warnedNoWinPopup)
    {
      // Once per session, not per spin. A null here is the single most likely reason a win
      // shows its symbol animation but never its popup, and it is invisible otherwise.
      warnedNoWinPopup = true;
      Debug.LogError("[SlotView] This round won but no win popup played: the 'Win Popup' " +
                     "field on SlotView is unassigned. Assign the WinPopupController object " +
                     "to it in the Inspector.", this);
    }

    // Must be started BEFORE onComplete. Show() raises isSpecialWinActive synchronously, and
    // onComplete runs GameManager.ProcessSpecialFeaturesAfterWin, which checks that flag on
    // its very first statement — start the popup after it and the round resolves underneath.
    if (showPopup)
      winPopup.Show(totalWinAmount, totalPay, () => popupDone = true);

    // The game loop advances here — everything below is idle presentation.
    onComplete?.Invoke();

    // The popup arrives as the symbols begin this second pass, and they keep cycling for as
    // long as it is up. popupDone is only tested between passes: AnimateWinPositions has no
    // cancel path, and cutting it mid-pass would strand its cells on winAnimationLayer with
    // their payout text still showing. A pass is short and the fade-out overlaps it.
    while (showPopup && !popupDone)
    {
      yield return StartCoroutine(AnimateWinPositions(allWinPositions));
      yield return new WaitForSeconds(phaseGapDelay);
    }

    // Autoplay and free spins never see the per-line breakdown; the next spin follows.
    if (gameManager != null && (gameManager.isAutoPlaying || gameManager.isInFreeSpins))
    {
      ReleaseWinOverlay();
      yield break;
    }

    // ---- STAGE 2: line by line, a fixed number of passes ------------------------------
    for (int pass = 0; pass < phase2Repeats; pass++)
    {
      foreach (var winLine in winLines)
      {
        if (winLine.positions == null || winLine.positions.Count == 0) continue;

        HideAllWinLineTexts();
        ShowLinePayoutText(winLine);

        yield return StartCoroutine(AnimateWinPositions(winLine.positions));
        yield return new WaitForSeconds(phase2LineDelay);
      }
    }

    HideAllWinLineTexts();
    yield return new WaitForSeconds(phaseGapDelay);

    // ---- STAGE 3: idle loop, all winners together, until the next spin ----------------
    while (true)
    {
      yield return StartCoroutine(AnimateWinPositions(allWinPositions));
      yield return new WaitForSeconds(phaseGapDelay);
    }
  }

  /// <summary>
  /// Put a line's payout on its <see cref="winTextColumnIndex"/> cell — the 3rd reel by
  /// default. Falls back to the last cell of the run if the line has no entry there.
  /// </summary>
  private void ShowLinePayoutText(WinLine winLine)
  {
    int chosenFlat = -1;

    foreach (int flatIndex in winLine.positions)
    {
      DecodeFlatIndex(flatIndex, out int col, out int row);
      if (col == winTextColumnIndex)
      {
        chosenFlat = flatIndex;
        break;
      }
    }

    if (chosenFlat < 0) chosenFlat = winLine.positions[winLine.positions.Count - 1];

    DecodeFlatIndex(chosenFlat, out int textCol, out int textRow);
    ResultCell(textCol, textRow)?.ShowWinText(winLine.winAmount);
  }

  /// <summary>
  /// Server win positions are row-major flat indices: flat = row * reelCount + col.
  /// (SpinResult.resultMatrix is the other way round — column-major.)
  /// </summary>
  private void DecodeFlatIndex(int flatIndex, out int col, out int row)
  {
    int reels = ReelCount;
    row = flatIndex / reels;
    col = flatIndex % reels;
  }

  /// <summary>
  /// Animate one set of cells and wait for every one of them to finish.
  ///
  /// For the duration, the darkening overlay is shown and each animating cell is
  /// reparented onto <see cref="winAnimationLayer"/> so it draws above it — everything not
  /// participating stays behind the black panel. Cells are returned home and the overlay
  /// hidden before this returns.
  /// </summary>
  private IEnumerator AnimateWinPositions(IEnumerable<int> flatPositions)
  {
    if (flatPositions == null) yield break;

    int rowLimit = RowCount;
    int colLimit = ReelCount;

    // Every winning cell of this stage, animated or not — all of them get lifted above the
    // darkening panel, because a winning symbol must be readable even when it has no
    // animation to play. Only the subset that actually staged one is waited on.
    var participating = new List<SlotSymbolView>();
    var staged = new List<SlotSymbolView>();
    // Cells whose animation has ended, waiting to be dropped back to their idle look. They
    // stay on the animation layer — a symbol that finishes early must not sink behind the
    // darkening panel while the rest of its line is still playing.
    var finished = new List<SlotSymbolView>();
    int pending = 0;

    foreach (int flatIndex in flatPositions)
    {
      DecodeFlatIndex(flatIndex, out int col, out int row);
      if (col < 0 || col >= colLimit || row < 0 || row >= rowLimit) continue;

      var cell = ResultCell(col, row);
      if (cell == null) continue;

      if (col >= currentDisplayMatrix.Count || row >= currentDisplayMatrix[col].Count) continue;
      int symbolId = currentDisplayMatrix[col][row];

      // Part of the win regardless of whether it can animate.
      participating.Add(cell);

      if (winAnimById == null) BuildWinAnimLookup();
      if (!winAnimById.TryGetValue(symbolId, out var anim) || anim == null)
      {
        Debug.LogError($"[SlotView] Symbol {symbolId} won at col {col} row {row} but has no " +
                       "symbolWinAnims entry, so it cannot animate. Add one on SlotView.", this);
        continue;
      }

      // Stage only — nothing plays yet. Both kinds run their sequence exactly ONCE and
      // report back when it ends. A cell may only fire its callback a single time, so
      // guard it: Spine's TrackEntry.Complete can raise more than once if the entry is
      // reused, and a double decrement would end the stage early.
      bool reported = false;
      System.Action onFinished = () =>
      {
        if (reported) return;
        reported = true;
        pending--;
        // Queue rather than settling the cell here and now: this runs inside
        // ImageAnimation.AnimationProcess or a Spine TrackEntry.Complete event, and Spine
        // forbids touching AnimationState from inside its own event callbacks. The
        // coroutine drains the queue on the next frame, outside both.
        finished.Add(cell);
      };

      bool didStage = anim.useSpine
          ? cell.StageSpineAnimation(anim.spineAnimation, anim.spineSkin, onFinished)
          : cell.StageFrameAnimation(anim.frames, anim.animationSpeed, onFinished);

      if (!didStage)
      {
        Debug.LogError($"[SlotView] Symbol {symbolId} at col {col} row {row} could not stage " +
                       $"its win animation (useSpine={anim.useSpine}). It will be shown " +
                       "highlighted but static.", this);
        continue;
      }

      pending++;
      staged.Add(cell);
    }

    if (participating.Count == 0) yield break;

    // Lift the winners above the darkening panel. Without a layer to lift them onto they
    // would animate BEHIND it, which looks like the presentation is broken — so in that
    // case PlayWinPresentation never raised the panel and this is a no-op.
    if (winAnimationLayer != null)
      foreach (var cell in participating) cell.MoveToAnimationLayer(winAnimationLayer);

    if (staged.Count == 0)
    {
      // Nothing to wait on — hold the highlight for a fixed beat instead.
      yield return new WaitForSeconds(winSymbolLoopDuration);
    }
    else
    {
      if (winAnimationStartDelay > 0f)
        yield return new WaitForSeconds(winAnimationStartDelay);

      // Every symbol starts on the SAME frame, so a stage stays visually in step.
      foreach (var cell in staged) cell.StartStagedAnimation();

      // Wait on the animations themselves and nothing else. There is deliberately no
      // timeout: a symbol that never reports completion is a bug to be found and fixed,
      // and a fallback would hide it by quietly cutting the stage short.
      while (pending > 0)
      {
        SettleFinishedCells(finished);
        yield return null;
      }

      SettleFinishedCells(finished);
    }

    foreach (var cell in participating)
    {
      // A no-op for anything that already settled; covers the never-animated cells.
      cell.StopWinAnimation();
      // The payout label belongs to the highlight, not to the reel. Drop it as the cell
      // goes back down, otherwise it lingers over a resting symbol through the inter-line
      // delay and only clears when the NEXT line starts.
      cell.HideWinText();
      cell.ReturnHome();
    }
  }

  /// <summary>
  /// Return cells that have finished animating to their idle look, WITHOUT reparenting
  /// them. They keep rendering above the darkening panel until every symbol in the stage
  /// is done, so a line always reads as a whole rather than dimming symbol by symbol.
  /// </summary>
  private void SettleFinishedCells(List<SlotSymbolView> finished)
  {
    if (finished.Count == 0) return;

    foreach (var cell in finished)
      if (cell != null) cell.StopWinAnimation(0f);

    finished.Clear();
  }

  /// <summary>Drop the presentation's claim on the darkening panel and take it down.</summary>
  private void ReleaseWinOverlay()
  {
    winOverlayHeld = false;
    SetWinOverlayActive(false);
  }

  /// <summary>
  /// Claim the darkening panel for a FEATURE rather than a win, and keep it up until
  /// <see cref="FadeOutOverlay"/> releases it.
  ///
  /// The free-spin trigger and outro both run their popups over a darkened grid for far
  /// longer than a win presentation does, and across beats — reels restarting, symbols
  /// resetting — that would otherwise each take the panel down.
  /// </summary>
  internal void HoldOverlayForFeature()
  {
    featureOverlayHeld = true;
    SetWinOverlayActive(true);
    EnforceWinLayerOrder();
  }

  /// <summary>
  /// Stop the win presentation and return every cell to rest, then claim the panel for the
  /// feature about to play over the same dark grid.
  ///
  /// interruptedBySpin is false because this is NOT a spin ending the presentation: the
  /// feature is taking it over, and cancelling the free-spin presenter here would tear down
  /// the very popups the caller is about to open.
  /// </summary>
  internal void SettleSymbolsForFeature()
  {
    KillWinTweens(stopCoroutine: true, interruptedBySpin: false);
    HoldOverlayForFeature();
  }

  /// <summary>
  /// Drop the feature's hold and cut the panel this frame. For a feature presentation being
  /// torn down rather than finishing — nothing is left on screen to fade.
  /// </summary>
  internal void ReleaseFeatureOverlay()
  {
    featureOverlayHeld = false;
    SetWinOverlayActive(false);
  }

  /// <summary>
  /// Release the feature's hold and fade the panel out, rather than cutting it. Yields until
  /// the fade finishes so a caller can sequence the reels' return behind it.
  /// </summary>
  internal IEnumerator FadeOutOverlay(float duration)
  {
    featureOverlayHeld = false;
    winOverlayHeld = false;

    if (winDarkenOverlay == null) yield break;

    var image = winDarkenOverlay.GetComponent<Graphic>();
    if (image == null || duration <= 0f)
    {
      SetWinOverlayActive(false);
      yield break;
    }

    // Cached and restored, because the panel's authored alpha is what every later SetActive
    // path relies on — fading it and leaving it at 0 would make the next win's darkening
    // silently invisible.
    float startAlpha = image.color.a;
    yield return image.DOFade(0f, duration).SetEase(Ease.InQuad).WaitForCompletion();

    SetWinOverlayActive(false);

    var restored = image.color;
    restored.a = startAlpha;
    image.color = restored;
  }

  private void SetWinOverlayActive(bool active)
  {
    // EITHER hold blocks a hide, and the feature hold is the stronger of the two. A free-spin
    // intro deliberately starts a spin while its popups are still up, and that spin's
    // StartSpin -> KillWinTweens -> ReleaseWinOverlay would otherwise take the panel down out
    // from under them. Only FadeOutOverlay clears the feature hold.
    if (!active && (winOverlayHeld || featureOverlayHeld)) return;
    if (winDarkenOverlay != null && winDarkenOverlay.activeSelf != active)
      winDarkenOverlay.SetActive(active);
  }

  #endregion

  #region Overlays & text

  private void HideAllWinLineTexts()
  {
    if (resultCells == null) return;
    foreach (var column in resultCells)
    {
      if (column?.cells == null) continue;
      foreach (var cell in column.cells)
        if (cell != null) cell.HideWinText();
    }
  }



  #endregion

  #region Symbol info card

  internal void HideSymbolInfoCard()
  {
    if (symbolInfoCard != null) symbolInfoCard.HideCard();
  }

  internal void OnBetChanged()
  {
    if (symbolInfoCard != null && symbolInfoCard.gameObject.activeSelf)
      symbolInfoCard.RefreshCard(gameManager);
  }

  internal void OnSymbolClicked(int col, int row, RectTransform symbolRect)
  {
    if (isSpinning)
    {
      if (symbolInfoCard != null) symbolInfoCard.HideCard();
      return;
    }

    if (currentDisplayMatrix == null || col >= currentDisplayMatrix.Count || row >= currentDisplayMatrix[col].Count)
      return;

    int symbolId = currentDisplayMatrix[col][row];
    if (symbolInfoCard != null)
      symbolInfoCard.ShowCard(symbolId, col, row, symbolRect, gameManager);
  }

  #endregion

  #region Queries & cleanup

  internal List<List<int>> GetCurrentDisplayMatrix() => currentDisplayMatrix;

  internal bool IsSpinning() => isSpinning;

  /// <param name="interruptedBySpin">
  /// True when a new SPIN is ending the presentation, which is what makes it right to take
  /// the darkening panel down and cancel the popups. False when a FEATURE is taking the
  /// presentation over instead — see <see cref="SettleSymbolsForFeature"/>.
  /// </param>
  private void KillWinTweens(bool stopCoroutine = true, bool interruptedBySpin = true)
  {
    foreach (var tween in winTweens) tween?.Kill();
    winTweens.Clear();

    if (stopCoroutine && winAnimationCoroutine != null)
    {
      StopCoroutine(winAnimationCoroutine);
      winAnimationCoroutine = null;
    }

    if (resultCells != null)
    {
      foreach (var column in resultCells)
      {
        if (column?.cells == null) continue;
        foreach (var cell in column.cells)
          if (cell != null) cell.ResetVisualState();
      }
    }

    // Release before hiding — the hold is what stops an individual stage taking the panel
    // down mid-presentation, so it has to be dropped here or the panel could never go away.
    if (interruptedBySpin) ReleaseWinOverlay();
    HideAllWinLineTexts();

    // The popup and its coins are part of the presentation, so the next spin takes them down
    // with everything else. Its sequence runs on the popup's own MonoBehaviour, so stopping
    // winAnimationCoroutine above does NOT reach it — without this it would keep holding
    // isSpecialWinActive and the game loop would never advance again.
    //
    // Only on the spin path: a feature settling the symbols is ABOUT to open its own popups,
    // and cancelling them here would tear them down the moment they opened.
    if (interruptedBySpin)
    {
      winPopup?.CancelImmediate();
      freeSpinPresenter?.CancelImmediate();
    }
  }

  private void KillAllTweens()
  {
    for (int i = 0; i < reelTweens.Length; i++)
    {
      reelTweens[i]?.Kill();
      reelTweens[i] = null;
    }
  }

  private void OnDestroy()
  {
    KillAllTweens();
    KillWinTweens();
  }

  #endregion
}

/// <summary>One column of reel cells, top to bottom.</summary>
[System.Serializable]
public class SlotColumn
{
  public List<SlotSymbolView> cells = new List<SlotSymbolView>(16);
}

/// <summary>Win animation configuration for one symbol id.</summary>
[System.Serializable]
public class SymbolWinAnim
{
  [Tooltip("Server symbol id this animation belongs to.")]
  public int symbolId;

  [Tooltip("Tick for symbols animated in Spine (the three piggies) instead of a sprite sequence.")]
  public bool useSpine;

  [Header("Sprite sequence (useSpine off)")]
  public List<Sprite> frames = new List<Sprite>();
  [Tooltip("Passed to ImageAnimation.AnimationSpeed. Leave 0 to keep the prefab's value.")]
  public float animationSpeed = 0f;

  [Header("Spine (useSpine on)")]
  [Tooltip("Spine animation name, e.g. \"Jump\".")]
  public string spineAnimation = "Jump";
  [Tooltip("Spine skin name, e.g. \"Blue\" / \"Yellow\" / \"Red\". Leave empty to keep the current skin.")]
  public string spineSkin = "";
}
