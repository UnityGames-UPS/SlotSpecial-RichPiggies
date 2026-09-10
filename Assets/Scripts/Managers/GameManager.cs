using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
  [Header("References")]
  [SerializeField] internal SocketIOManager socketManager;
  [SerializeField] internal UIManager uiManager;
  [SerializeField] internal PigMeterController pigMeters;
  [SerializeField] private PopupManager popupManager;
  [SerializeField] private SlotView slotView;
  [SerializeField] private FreeSpinPresenter freeSpinPresenter;

  [Header("Spin Settings")]
  [SerializeField] private float normalSpinDuration = 3.5f;
  [SerializeField] private float turboSpinDuration = 2.0f;
  [SerializeField] private float quickSpinCycleDuration = 0.8f;

  internal GameConfig gameConfig;
  internal PlayerData playerData;
  internal SpinResult lastResult;

  internal GameState currentState;
  internal SpinSpeed currentSpinSpeed;

  internal int currentBetIndex;
  internal double currentBetAmount;

  internal bool isAutoPlaying;
  internal int autoPlayTotalRounds;
  internal int autoPlayRemainingRounds;

  internal bool isInFreeSpins;
  internal int freeSpinsRemaining;
  internal int freeSpinsUsed;
  internal bool waitingForFreeSpinStart;

  /// <summary>
  /// The live round's bookkeeping — contributors, total spins, accumulated win. Null outside a
  /// round. The server sends only freeSpinsRemaining, so everything else the UI needs is
  /// tracked here. See <see cref="FreeSpinRound"/>.
  /// </summary>
  internal FreeSpinRound currentRound;

  /// <summary>
  /// Set the moment a trigger arrives in the spin payload and consumed once the win
  /// presentation is done. Held rather than acted on immediately because the trigger
  /// presentation must play AFTER the coin flights and the win lines, not instead of them.
  /// </summary>
  private FreeSpinRound pendingRound;

  internal bool isInitialized;
  internal bool initializationFailed;

  private Coroutine spinCoroutine;
  private bool stopRequested;

  #region Initialization

  private void Start()
  {
    currentState = GameState.Initializing;
    currentSpinSpeed = SpinSpeed.Normal;
    waitingForFreeSpinStart = false;
    isInitialized = false;
    initializationFailed = false;
  }

  internal void OnInitDataReceived(GameConfig config, PlayerData player, List<List<int>> initialMatrix)
  {
    gameConfig = config;
    playerData = player;
    currentBetIndex = playerData.currentBetIndex;
    UpdateBetAmount();

    // if (initialMatrix != null && slotView != null)
    // {
    //     slotView.SetInitialMatrix(initialMatrix);
    // }

    // Seed the pig / jackpot meters from the init payload's live state. Done after
    // UpdateBetAmount, because the six jackpot payouts are multiplier x current bet.
    if (pigMeters != null) pigMeters.SeedFromInit(gameConfig.features);

    isInitialized = true;
    currentState = GameState.Idle;

    uiManager.OnGameInitialized();
  }

  #endregion

  #region Bet Management

  internal void IncreaseBet()
  {
    if (currentState != GameState.Idle || isAutoPlaying) return;
    if (gameConfig == null || gameConfig.availableBets == null || gameConfig.availableBets.Count == 0) return;

    int maxIndex = gameConfig.availableBets.Count - 1;
    int nextIndex = currentBetIndex + 1;
    if (nextIndex > maxIndex)
    {
      nextIndex = 0;
    }

    if (nextIndex == maxIndex)
    {
      AudioManager.Instance?.PlayMaxBetReached();
    }
    else
    {
      AudioManager.Instance?.PlayBetPlusMinus();
    }

    SetBetIndex(nextIndex);
  }

  internal void DecreaseBet()
  {
    if (currentState != GameState.Idle || isAutoPlaying) return;
    if (gameConfig == null || gameConfig.availableBets == null || gameConfig.availableBets.Count == 0) return;

    int maxIndex = gameConfig.availableBets.Count - 1;
    int nextIndex = currentBetIndex - 1;
    if (nextIndex < 0)
    {
      nextIndex = maxIndex;
    }

    if (nextIndex == maxIndex)
    {
      AudioManager.Instance?.PlayMaxBetReached();
    }
    else
    {
      AudioManager.Instance?.PlayBetPlusMinus();
    }

    SetBetIndex(nextIndex);
  }

  internal void SetBetIndex(int index)
  {
    currentBetIndex = index;
    UpdateBetAmount();
    uiManager.UpdateBetDisplay();

    // The jackpot meters hold multipliers; what the player sees is multiplier x bet, so all
    // six have to be repainted whenever the bet moves.
    if (pigMeters != null) pigMeters.RefreshJackpotTexts();

    if (slotView != null) slotView.OnBetChanged();
  }

  private void UpdateBetAmount()
  {
    currentBetAmount = gameConfig.availableBets[currentBetIndex];
  }

  #endregion

  #region Spin Control

  internal void RequestSpin()
  {
    if (waitingForFreeSpinStart) return;

    if (currentState != GameState.Idle) return;
    if (!socketManager.isConnected) return;

    double totalPay = GetTotalPay();
    if (!isInFreeSpins && playerData.balance < totalPay)
    {
      if (popupManager != null)
      {
        popupManager.ShowInsufficientFundsError();
      }
      return;
    }

    StartSpin();
  }

  internal void RequestStop()
  {
    if (currentState != GameState.Spinning) return;

    if (isAutoPlaying)
    {
      StopAutoPlay();
      return;
    }

    // The free-spin intro borrows this same button. Its popups are holding the reels open
    // artificially, so a press there has to cut the ANNOUNCEMENT as well as landing the reels
    // — otherwise the reels would stop and then keep spinning again to wait for the popups.
    // A no-op outside the intro.
    freeSpinPresenter?.RequestIntroStop();

    // Free spins stop exactly like a base spin: the player is watching the same reels and has
    // the same reason to want them down now.
    stopRequested = true;
    uiManager.DisableSpinButtonDuringStop();
  }

  private void StartSpin()
  {
    if (lastResult != null)
    {
      ProcessSpinResult();
    }

    lastResult = null;
    currentState = GameState.Spinning;
    stopRequested = false;

    // Deduct total pay from balance on spin start (except in free spins)
    if (!isInFreeSpins)
    {
      playerData.balance -= GetTotalPay();
      if (playerData.balance < 0) playerData.balance = 0;
    }

    uiManager.OnSpinStarted();

    // The request goes out immediately so the network round-trip overlaps the reel
    // animation. SlotView.StartSpin is a coroutine now, so SpinRoutine drives it.
    socketManager.SendSpinRequest(currentBetIndex, isInFreeSpins);

    if (spinCoroutine != null)
      StopCoroutine(spinCoroutine);
    spinCoroutine = StartCoroutine(SpinRoutine());
  }

  private IEnumerator SpinRoutine()
  {
    // Earliest moment the reels are allowed to settle, so a fast server never makes
    // the spin look like a stutter. The Stop button cuts this short.
    float minSpinEndTime = Time.time + GetSpinDuration();

    // 1. Spin up. Yields until every column has handed over to its infinite loop.
    if (slotView != null)
    {
      yield return StartCoroutine(slotView.StartSpin());
    }

    // 2. Wait for the server result (already requested in StartSpin).
    while (lastResult == null)
    {
      yield return null;
    }

    // 3. Write the result into the reel strips WHILE they are still looping. The
    //    cells are off-screen at this moment, so the stop tween simply parks the
    //    already-correct symbols at restY — nothing is snapped in at stop time.
    if (slotView != null && lastResult.resultMatrix != null)
    {
      slotView.PopulateResultMatrix(lastResult.resultMatrix);

      // Cover the Mystery cells straight away, still mid-loop. The matrix above already
      // wrote the REVEALED symbol into them, so the locker hides it until the reels settle
      // and SlotView opens every locker as part of its stop sequence.
      slotView.ShowMysteryLockers(lastResult.mysteryReveals);

      // Coins are stamped in the same beat. Their child sits BELOW the locker, so a coin on
      // a Mystery cell stays hidden until that locker opens; a coin on a plain cell is
      // simply visible when the column parks. The meters are passed alongside because the
      // client works out which coin moved which meter by diffing them.
      slotView.ShowCoinOverlays(lastResult.coinOverlays, lastResult.meters);

      LatchFreeSpinTrigger();
    }

    // 4. Cosmetic hold, interruptible by Stop.
    //
    //    IsIntroBlocking extends it indefinitely for the FIRST free spin: its reels keep
    //    looping under the black overlay while the per-pig intro popups play, for however
    //    long that takes. The intro's own Stop button clears the flag by way of stopRequested.
    while ((Time.time < minSpinEndTime || IsFreeSpinIntroHolding) && !stopRequested)
    {
      yield return null;
    }

    currentState = GameState.Stopping;

    if (slotView != null)
    {
      bool immediate = stopRequested || currentSpinSpeed == SpinSpeed.QuickSpin;
      bool turbo = currentSpinSpeed != SpinSpeed.Normal;

      yield return StartCoroutine(slotView.StopSpin(
          immediate,
          turbo,
          () => AudioManager.Instance?.PlayReelStop(),
          OnReelsStoppedComplete));
    }
    else
    {
      OnReelsStoppedComplete();
    }
  }

  private bool IsFreeSpinIntroHolding =>
      freeSpinPresenter != null && freeSpinPresenter.IsIntroBlocking;

  /// <summary>
  /// Read a free-spin trigger out of the spin payload and hold it until the presentation is
  /// ready for it.
  ///
  /// Called while the reels are still looping, so the round's numbers are latched from THIS
  /// response — in particular the Blue and Red meter values, which the server resets once the
  /// round ends and which the intro popups have to announce.
  /// </summary>
  private void LatchFreeSpinTrigger()
  {
    if (isInFreeSpins || pendingRound != null) return;

    var contributors = PigFeatures.Parse(lastResult.triggeredFeatures);
    if (contributors == PigFeature.None) return;

    pendingRound = FreeSpinRound.FromTrigger(contributors, lastResult.serverSpinsRemaining,
                                             lastResult.meters, lastResult.winAmount);
  }

  private void OnReelsStoppedComplete()
  {
    if (lastResult != null)
    {
      double reelStopBalance = lastResult.playerData != null ? lastResult.playerData.balance : 0;

      playerData = new PlayerData
      {
        balance = reelStopBalance,
        currentBetIndex = lastResult.playerData != null ? lastResult.playerData.currentBetIndex : currentBetIndex
      };
    }

    // Fold this free spin into the round total BEFORE the HUD is updated below — the win field
    // reads currentRound.accumulatedWin, so doing it after (as ProcessSpinResult used to) left
    // the bottom bar showing the previous spin's total all the way through this one.
    if (isInFreeSpins && currentRound != null && lastResult != null)
      currentRound.RecordSpin(lastResult.winAmount, lastResult.serverSpinsRemaining);

    // The HUD updates straight away and the state returns to Idle; the win presentation
    // plays out on top and does not gate that. SlotView fires OnWinAnimationComplete at the
    // end of its stage 1. Controls are the exception — see below.
    uiManager.OnSpinStopping(lastResult);
    uiManager.EnableControlsAfterWinAnimation();
    uiManager.OnSpinCompleted(lastResult);

    // A winning spin keeps its controls locked through the presentation. The win popup only
    // opens a couple of seconds in (winPresentationDelay plus one stage-1 pass), and handing
    // spin back in the meantime lets the player cut a popup they never saw. This has to come
    // AFTER OnSpinCompleted, which re-enables the spin button itself.
    //
    // The unlock is in ProcessSpecialFeaturesAfterWin rather than here, so it happens exactly
    // once whether or not a popup actually appears — a win with no popup would otherwise
    // stay locked forever.
    if (lastResult != null && lastResult.winAmount > 0)
      uiManager.DisableControlsDuringWinAnimation();

    currentState = GameState.Idle;

    // The coin flights have already landed by now — SlotView runs them inside StopSpin, before
    // this fires — so this is the moment the brief calls for: the triggering pigs light up and
    // the others darken, and the win presentation then plays on top of that.
    if (pendingRound != null && pigMeters != null)
      pigMeters.EnterTriggerState(pendingRound.contributors);

    if (lastResult != null && lastResult.winLines != null && lastResult.winLines.Count > 0)
    {
      slotView.ShowWinLineAnimation(lastResult.winLines, OnWinAnimationComplete);
    }
    else
    {
      OnWinAnimationComplete();
    }
  }

  private void OnWinAnimationComplete()
  {
    StartCoroutine(ProcessSpecialFeaturesAfterWin());
  }

  private IEnumerator ProcessSpecialFeaturesAfterWin()
  {
    // Wait for special win popup to finish before starting special features
    while (uiManager.IsSpecialWinActive)
    {
      yield return null;
    }

    // The single guaranteed unlock for the lock OnReelsStoppedComplete puts on a winning
    // spin. Every path through the presentation passes here, popup or not. Redundant after
    // OnWinPopupClosed, which has already done it — and harmless, because the method is
    // idempotent and no-ops while any special win is still flagged.
    uiManager.EnableControlsAfterWinAnimation();

    // The free-spin trigger presentation lands here, after the coin flights and the whole win
    // presentation have played. It does NOT fall through to ResumeAfterSpecialFeature: the
    // presenter starts the first free spin itself, once the player has dismissed its popup.
    if (pendingRound != null)
    {
      var round = pendingRound;
      pendingRound = null;

      // Settle the BASE-game spin that triggered before entering the round. Without this it
      // would be resolved later, from inside StartSpin, by which time isInFreeSpins is true —
      // and its win, which the player has already been paid, would be folded into the
      // free-spin round total that the congratulations panel announces.
      if (lastResult != null)
      {
        playerData = lastResult.playerData;
        uiManager.OnSpinCompleted(lastResult);
        lastResult = null;
      }

      StartFreeSpins(round);
      yield break;
    }

    ResumeAfterSpecialFeature();
  }

  private void ResumeAfterSpecialFeature()
  {
    if (isAutoPlaying || isInFreeSpins)
    {
      StartCoroutine(DelayBeforeNextRound());
    }
    else
    {
      ProcessSpinResult();
    }
  }

  private IEnumerator DelayBeforeNextRound()
  {
    float delayTime = currentSpinSpeed == SpinSpeed.QuickSpin ? 0.3f : 0.5f;
    yield return new WaitForSeconds(delayTime);

    // Wait for special win popup using the flag and active state
    while (uiManager.IsSpecialWinActive)
    {
      yield return null;
    }

    ProcessSpinResult();
  }

  private float GetSpinDuration()
  {
    return currentSpinSpeed switch
    {
      SpinSpeed.Normal => normalSpinDuration,
      SpinSpeed.Turbo => turboSpinDuration,
      SpinSpeed.QuickSpin => quickSpinCycleDuration,
      _ => normalSpinDuration
    };
  }

  internal void OnSpinResultReceived(SpinResult result)
  {
    lastResult = result;

    // The server's remaining count is taken as soon as it lands, so the round-over check
    // never runs on a stale value.
    //
    // The DISPLAYED count is deliberately not touched here. It ticks in UIManager.OnSpinStarted
    // instead, as the next spin's reels start — updating it now would advance the number
    // partway through the spin the player is still watching.
    if (isInFreeSpins && result.serverSpinsRemaining >= 0)
    {
      freeSpinsRemaining = result.serverSpinsRemaining;
      freeSpinsUsed = currentRound != null ? currentRound.totalSpins - freeSpinsRemaining : 0;
    }
  }

  private void ProcessSpinResult()
  {
    playerData = lastResult.playerData;

    uiManager.OnSpinCompleted(lastResult);

    // Extract server-authoritative values before nullifying lastResult. Only remaining and
    // isRoundOver are real — serverSpinsUsed and serverTotalRoundWin are not populated by the
    // converter for Rich Piggies, so the round's totals come from currentRound instead.
    int serverSpinsRemaining = lastResult.serverSpinsRemaining;
    bool isRoundOver = lastResult.isRoundOver;

    // Note: freeSpinsRemaining already updated in OnSpinResultReceived
    // Keeping this for safety in case OnSpinResultReceived wasn't called
    if (isInFreeSpins && freeSpinsRemaining != serverSpinsRemaining)
    {
      freeSpinsRemaining = serverSpinsRemaining;
    }


    // NOTE: the round's running total is folded in at REEL STOP (OnReelsStoppedComplete), not
    // here. This method runs after the win presentation, long after the HUD has already been
    // asked to show the new total — accumulating here left the bottom bar one spin behind for
    // the whole round.
    //
    // A free-spin TRIGGER is not handled here either: it is latched from
    // payload.triggeredFeatures while the reels are still spinning and presented after the win
    // animation, in ProcessSpecialFeaturesAfterWin.

    lastResult = null;

    if (isAutoPlaying && !isInFreeSpins)
    {
      if (autoPlayTotalRounds != -1)
      {
        autoPlayRemainingRounds--;
      }

      uiManager.UpdateAutoPlayCount();

      if (autoPlayTotalRounds != -1 && autoPlayRemainingRounds <= 0)
      {
        currentState = GameState.Idle;
        StopAutoPlay();
      }
      else
      {
        // Before requesting the next spin, verify the player can still afford it.
        // If not, stop autoplay (restores all UI) then show the popup.
        double totalPay = GetTotalPay();
        if (playerData.balance < totalPay)
        {
          currentState = GameState.Idle;
          StopAutoPlay();
          if (popupManager != null) popupManager.ShowInsufficientFundsError();
        }
        else
        {
          currentState = GameState.Idle;
          RequestSpin();
        }
      }
    }
    else if (isInFreeSpins)
    {
      // Free spin counter already updated in OnSpinResultReceived
      // No need to update again here

      if (isRoundOver || freeSpinsRemaining <= 0)
      {
        EndFreeSpins();
      }
      else
      {
        currentState = GameState.Idle;
        StartCoroutine(DelayBeforeNextFreeSpin());
      }
    }
    else
    {
      currentState = GameState.Idle;
    }
  }

  #endregion

  #region Spin Speed Control

  internal void SetSpinSpeed(SpinSpeed speed)
  {
    currentSpinSpeed = speed;
  }

  #endregion



  #region Auto Play

  internal void StartAutoPlay(int rounds)
  {
    if (currentState != GameState.Idle) return;

    // Check balance BEFORE locking any UI — if insufficient, show popup and bail.
    double totalPay = GetTotalPay();
    if (playerData.balance < totalPay)
    {
      if (popupManager != null) popupManager.ShowInsufficientFundsError();
      return;
    }

    isAutoPlaying = true;
    autoPlayTotalRounds = rounds;
    autoPlayRemainingRounds = rounds;

    uiManager.OnAutoPlayStarted();
    RequestSpin();
  }

  internal void StopAutoPlay()
  {
    isAutoPlaying = false;
    autoPlayRemainingRounds = 0;

    uiManager.OnAutoPlayStopped();
  }

  // NOTE: autoplay is not resumed after a free-spin round. A trigger ends it outright (see
  // StartFreeSpins), so the suspend/restore pair the CNY template had has been removed rather
  // than left in place describing behaviour that no longer happens.

  #endregion

  #region Free Spins

  /// <summary>
  /// Enter a free-spin round and hand the screen to <see cref="FreeSpinPresenter"/>.
  ///
  /// The round does NOT start spinning here. waitingForFreeSpinStart blocks RequestSpin until
  /// the presenter's trigger popup is dismissed, at which point it calls
  /// <see cref="StartFirstFreeSpin"/> itself.
  /// </summary>
  private void StartFreeSpins(FreeSpinRound round)
  {
    currentRound = round;

    isInFreeSpins = true;
    freeSpinsRemaining = round.totalSpins;
    freeSpinsUsed = 0;
    waitingForFreeSpinStart = true;
    AudioManager.Instance?.PlayFreeSpinBg();

    // A trigger ENDS autoplay outright rather than suspending it — the round takes the screen
    // over completely, and handing it back to an autoplay the player set up minutes ago is not
    // what they would expect. The CNY template's suspend/restore pair has been removed
    // entirely rather than left inert.
    if (isAutoPlaying) StopAutoPlay();

    uiManager.OnFreeSpinsStarted(round.totalSpins);

    currentState = GameState.Idle;

    if (freeSpinPresenter == null)
    {
      // Without the presenter there is no popup to dismiss, so nothing would ever clear
      // waitingForFreeSpinStart and the game would sit idle forever.
      Debug.LogError("[GameManager] A free-spin round triggered but freeSpinPresenter is not " +
                     "assigned — starting the round with no trigger presentation. Assign it " +
                     "in the Inspector.", this);
      StartFirstFreeSpin();
      return;
    }

    freeSpinPresenter.BeginTrigger(round);
  }

  /// <summary>
  /// Called by the presenter as its trigger popup closes. Starts the reels immediately — the
  /// intro popups play over the top of them, held open by IsIntroBlocking.
  /// </summary>
  internal void StartFirstFreeSpin()
  {
    waitingForFreeSpinStart = false;
    RequestSpin();
  }

  private IEnumerator DelayBeforeNextFreeSpin()
  {
    yield return new WaitForSeconds(0.3f);

    // Wait for special win popup if it's still active or pending
    while (uiManager.IsSpecialWinActive)
    {
      yield return null;
    }

    RequestSpin();
  }

  private void EndFreeSpins()
  {
    StartCoroutine(EndFreeSpinsRoutine());
  }

  /// <summary>
  /// Close the round out. isInFreeSpins is cleared only AFTER the outro finishes, so the HUD
  /// keeps its free-spin dressing — the counter, the accumulated win, the locked controls —
  /// right through the congratulations panel rather than snapping back behind it.
  /// </summary>
  private IEnumerator EndFreeSpinsRoutine()
  {
    if (freeSpinPresenter != null && currentRound != null)
    {
      freeSpinPresenter.BeginOutro(currentRound);
      yield return freeSpinPresenter.WaitForCompletion();
    }

    isInFreeSpins = false;
    freeSpinsRemaining = 0;
    AudioManager.Instance?.PlayMainBg();

    double totalRoundWin = currentRound != null ? currentRound.accumulatedWin : 0;
    int totalSpinsUsed = currentRound != null ? currentRound.spinsUsed : 0;
    currentRound = null;

    uiManager.OnFreeSpinsEnded(totalRoundWin, totalSpinsUsed);

    currentState = GameState.Idle;
  }

  #endregion

  #region Connection Events

  internal void OnDisconnected()
  {
    if (spinCoroutine != null)
    {
      StopCoroutine(spinCoroutine);
      spinCoroutine = null;
    }

    if (isAutoPlaying)
    {
      StopAutoPlay();
    }

    currentState = GameState.Idle;
    // Note: The disconnection popup is shown by SocketIOManager.OnSocketDisconnected()
    // to avoid duplicates. GameManager only cleans up state here.
  }

  internal void ExitGame()
  {
    socketManager.CloseSocket();

  }

  #endregion

  #region Helper Methods

  internal double GetTotalPay()
  {
    double divisor = (gameConfig != null && gameConfig.creditDivisor > 0) ? gameConfig.creditDivisor : 25;
    return currentBetAmount * divisor;
  }

  internal bool CanAffordBet()
  {
    double totalPay = GetTotalPay();
    return playerData.balance >= totalPay;
  }

  internal bool IsSpinning()
  {
    return currentState == GameState.Spinning || currentState == GameState.Stopping;
  }

  /// <summary>
  /// Returns true if at least one scatter symbol appears anywhere in the result matrix.
  /// Uses the server-configured scatterSymbolId (default 12) as the reference ID.
  /// </summary>
  private bool ResultMatrixHasScatter(List<List<int>> matrix)
  {
    if (matrix == null) return false;

    int scatterId = gameConfig != null ? gameConfig.scatterSymbolId : 12;

    foreach (var col in matrix)
    {
      if (col == null) continue;
      foreach (int sym in col)
      {
        if (sym == scatterId) return true;
      }
    }

    return false;
  }

  #endregion
}
