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
  [SerializeField] internal WheelSpinController wheelController;

  [Header("Spin Settings")]
  [SerializeField] private float normalSpinDuration = 3.5f;
  [SerializeField] private float turboSpinDuration = 2.0f;
  [SerializeField] private float quickSpinCycleDuration = 0.8f;

  [Header("Win Settings")]
  [SerializeField] private double bigWinMultiplierThreshold = 500.0;
  public double BigWinMultiplierThreshold => bigWinMultiplierThreshold;

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
  internal bool wasAutoPlayingBeforeFreeSpins;
  internal int savedAutoPlayRemainingRounds;
  internal int savedAutoPlayTotalRounds;

  internal bool isInFreeSpins;
  internal int freeSpinsRemaining;
  internal int freeSpinsUsed;
  internal bool waitingForFreeSpinStart;

  internal bool isInitialized;
  internal bool initializationFailed;

  private Coroutine spinCoroutine;
  private bool stopRequested;
  private bool waitingForSpecialWin;

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

    // [CNY] USpin bonus wheel — no Rich Piggies equivalent. Kept wired so the scene
    // reference and WheelSpinController still compile; re-purpose or delete later.
    // if (wheelController != null && gameConfig.uSpinSegments != null)
    // {
    //   wheelController.OverrideSegmentsWithData(gameConfig.uSpinSegments);
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
    if (currentState == GameState.Spinning)
    {
      if (isAutoPlaying)
      {
        StopAutoPlay();
      }
      else if (!isInFreeSpins)
      {
        stopRequested = true;
        uiManager.DisableSpinButtonDuringStop();
      }
    }
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

      if (lastResult.triggeredFeatures != null && lastResult.triggeredFeatures.Count > 0)
      {
        Debug.LogWarning("[GameManager] Feature trigger not implemented yet: " +
                         string.Join(", ", lastResult.triggeredFeatures) +
                         $" (activeFeature: {lastResult.activeFeature ?? "none"}, " +
                         $"freeSpinsRemaining: {lastResult.serverSpinsRemaining}). " +
                         "The coin beat still plays; the free-spin round and the meter " +
                         "resets that end it are not built.");
      }
    }

    // 4. Cosmetic hold, interruptible by Stop.
    while (Time.time < minSpinEndTime && !stopRequested)
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

  private void OnReelsStoppedComplete()
  {
    if (lastResult != null)
    {
      double featureDeferredWin = lastResult.GetTotalFeatureDeferredWins();
      double reelStopBalance = lastResult.playerData != null ? (lastResult.playerData.balance - featureDeferredWin) : 0;

      playerData = new PlayerData
      {
        balance = reelStopBalance,
        currentBetIndex = lastResult.playerData != null ? lastResult.playerData.currentBetIndex : currentBetIndex
      };
    }

    // The HUD updates and the controls come back straight away; the win presentation then
    // plays out on top and does not gate the return to Idle. SlotView fires
    // OnWinAnimationComplete at the end of its stage 1.
    uiManager.OnSpinStopping(lastResult);
    uiManager.EnableControlsAfterWinAnimation();
    uiManager.OnSpinCompleted(lastResult);
    currentState = GameState.Idle;

    if (lastResult != null && lastResult.winLines != null && lastResult.winLines.Count > 0)
    {
      slotView.ShowWinLineAnimation(lastResult.winLines, OnWinAnimationComplete);
    }
    else
    {
      OnWinAnimationComplete();
    }

    // [CNY] Big-win popup. Threshold-gated control lock plus TriggerWinPopupWithDelay,
    // which parks the loop on waitingForSpecialWin until the popup resolves. The popup is
    // still CNY-styled, so it stays off until it is re-authored for Rich Piggies.
    //
    // double multiplier = GetTotalPay() > 0 ? (lastResult.winAmount / GetTotalPay()) : 0;
    // if (multiplier >= bigWinMultiplierThreshold)
    // {
    //   uiManager.DisableControlsDuringWinAnimation();
    //   StartCoroutine(TriggerWinPopupWithDelay(1.5f, lastResult));
    // }
  }

  private IEnumerator TriggerWinPopupWithDelay(float delay, SpinResult result)
  {
    double totalPay = GetTotalPay();
    double multiplier = totalPay > 0 ? (result.winAmount / totalPay) : 0;
    if (multiplier < bigWinMultiplierThreshold)
    {
      waitingForSpecialWin = false;
      yield break;
    }

    waitingForSpecialWin = true;

    yield return new WaitForSeconds(delay);

    if (lastResult == result && multiplier >= bigWinMultiplierThreshold)
    {
      uiManager.TriggerBigWinPopup(result, () =>
      {
        waitingForSpecialWin = false;
      });
    }
    else
    {
      waitingForSpecialWin = false;
    }
  }

  private void OnWinAnimationComplete()
  {
    // [WINLINES OFF] The big-win branch existed only to catch up the HUD after the win
    // animation finished. OnReelsStoppedComplete now always calls OnSpinStopping itself
    // before getting here, so re-calling it would just double-update the display.
    //
    // if (lastResult != null)
    // {
    //   double totalPay = GetTotalPay();
    //   double multiplier = totalPay > 0 ? (lastResult.winAmount / totalPay) : 0;
    //   if (multiplier >= bigWinMultiplierThreshold)
    //   {
    //     uiManager.OnSpinStopping(lastResult);
    //   }
    // }

    StartCoroutine(ProcessSpecialFeaturesAfterWin());
  }

  private IEnumerator ProcessSpecialFeaturesAfterWin()
  {
    // Wait for special win popup to finish before starting special features
    while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
    {
      yield return null;
    }

    // [CNY] USpin wheel and MoneyBag pick bonuses have no Rich Piggies equivalent.
    // if (lastResult != null && lastResult.uSpinData != null && lastResult.uSpinData.triggered)
    // {
    //   yield return StartCoroutine(DelayUSpinTriggerResult());
    //   yield break;
    // }
    //
    // if (lastResult != null && lastResult.moneyBagData != null && lastResult.moneyBagData.triggered)
    // {
    //   yield return StartCoroutine(DelayMoneyBagTriggerResult());
    //   yield break;
    // }

    // [CNY] Free-spin trigger presentation animated the single scatter symbol. Rich
    // Piggies triggers on any combination of Blue / Yellow / Red piggies instead, so the
    // presentation is re-authored once the piggy payload is defined. The free-spin STATE
    // machine below (StartFreeSpins / EndFreeSpins) is left intact and stays inert while
    // the server sends no freeSpinData.
    // if (lastResult != null && lastResult.freeSpinData != null && lastResult.freeSpinData.isTriggered && !isInFreeSpins)
    // {
    //   yield return StartCoroutine(DelayScatterTriggerResult());
    //   yield break;
    // }

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

  // ==========================================================================
  // [CNY] Feature trigger presentations. These drove the USpin wheel, the MoneyBag
  // pick bonus and the scatter free-spin trigger — none of which exist in Rich
  // Piggies. Commented out rather than deleted so the beat structure (animate the
  // trigger symbols, wait, hand off to a UIManager popup, resume) can be reused for
  // the piggy triggers and the Mystery reveal.
  // ==========================================================================
  // private IEnumerator DelayScatterTriggerResult()
  // {
  // // Play special feature trigger sound AFTER all reels have stopped
  // AudioManager.Instance?.Play3UspinWinLineLoop();
  //
  // // Start scatter animations together AFTER all reels have stopped
  // // Using 4 loops to match the 6-second delay (4 * 1.5s = 6s)
  // slotView.AnimateAllScatters(4);
  //
  // // Wait for scatter hit animations to play
  // yield return new WaitForSeconds(3.5f);
  // ProcessSpinResult();
  // }
  //
  // private IEnumerator DelayUSpinTriggerResult()
  // {
  // AudioManager.Instance?.Play3UspinWinLineLoop();
  //
  // bool animFinished = false;
  // if (slotView != null)
  // {
  // slotView.AnimateUSpinWin(() =>
  // {
  // animFinished = true;
  // });
  // }
  // else
  // {
  // animFinished = true;
  // }
  //
  // yield return new WaitUntil(() => animFinished);
  //
  // uiManager.TriggerUSpinBonus(lastResult.uSpinData, () =>
  // {
  // AudioManager.Instance?.Stop3UspinWinLineLoop();
  // lastResult.uSpinData.triggered = false;
  // ResumeAfterSpecialFeature();
  // });
  // }
  //
  // private IEnumerator DelayMoneyBagTriggerResult()
  // {
  // AudioManager.Instance?.Play3UspinWinLineLoop();
  //
  // if (slotView != null)
  // {
  // slotView.AnimateMoneyBagWin();
  // }
  //
  // yield return new WaitForSeconds(3.5f);
  //
  // uiManager.TriggerMoneyBagBonus(lastResult.moneyBagData, () =>
  // {
  // lastResult.moneyBagData.triggered = false;
  // ResumeAfterSpecialFeature();
  // });
  // }

  private IEnumerator DelayBeforeNextRound()
  {
    float delayTime = currentSpinSpeed == SpinSpeed.QuickSpin ? 0.3f : 0.5f;
    yield return new WaitForSeconds(delayTime);

    // Wait for special win popup using the flag and active state
    while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
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

    // CRITICAL FIX: Update free spin counter IMMEDIATELY when server response arrives
    // This ensures the display shows the exact server-authoritative played count without lag
    if (isInFreeSpins && result.serverSpinsRemaining >= 0)
    {
      freeSpinsRemaining = result.serverSpinsRemaining;
      freeSpinsUsed = result.serverSpinsUsed;
      int displayTotalSpins = result.serverTotalSpins;

      if (result.uSpinData != null && result.uSpinData.triggered && result.uSpinData.freeGamesAwarded > 0)
      {
        // Defer adding the newly won free spins to total count until wheel spin completes and user presses Take!
        displayTotalSpins -= result.uSpinData.freeGamesAwarded;
        freeSpinsRemaining -= result.uSpinData.freeGamesAwarded;
      }

      uiManager.UpdateFreeSpinCount(freeSpinsUsed, displayTotalSpins);
    }

    if (result.winLines != null)
    {
      for (int i = 0; i < result.winLines.Count; i++)
      {
        var line = result.winLines[i];

      }
    }
  }

  private void ProcessSpinResult()
  {
    playerData = lastResult.playerData;

    uiManager.OnSpinCompleted(lastResult);

    // Extract server-authoritative values before nullifying lastResult
    int serverSpinsRemaining = lastResult.serverSpinsRemaining;
    int serverSpinsUsed = lastResult.serverSpinsUsed;
    double serverTotalRoundWin = lastResult.serverTotalRoundWin;
    bool isRoundOver = lastResult.isRoundOver;

    // Note: freeSpinsRemaining already updated in OnSpinResultReceived
    // Keeping this for safety in case OnSpinResultReceived wasn't called
    if (isInFreeSpins && freeSpinsRemaining != serverSpinsRemaining)
    {
      freeSpinsRemaining = serverSpinsRemaining;
    }


    // Check if free spins were just triggered (initial trigger from base game)
    if (lastResult.freeSpinData != null && lastResult.freeSpinData.isTriggered && !isInFreeSpins)
    {
      StartFreeSpins(lastResult.freeSpinData.spinsAwarded);
      lastResult = null;
      return;
    }

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
        // Always use server-authoritative spinsUsed
        EndFreeSpins(serverTotalRoundWin, serverSpinsUsed);
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
    wasAutoPlayingBeforeFreeSpins = false;

    uiManager.OnAutoPlayStarted();
    RequestSpin();
  }

  internal void StopAutoPlay()
  {
    isAutoPlaying = false;
    autoPlayRemainingRounds = 0;
    wasAutoPlayingBeforeFreeSpins = false;

    uiManager.OnAutoPlayStopped();
  }

  internal bool ShouldResumeAutoPlay()
  {
    return wasAutoPlayingBeforeFreeSpins && (savedAutoPlayTotalRounds == -1 || savedAutoPlayRemainingRounds > 0);
  }

  internal void ResumeAutoPlay()
  {
    if (!ShouldResumeAutoPlay()) return;

    int remaining = savedAutoPlayRemainingRounds;
    int total = savedAutoPlayTotalRounds;
    wasAutoPlayingBeforeFreeSpins = false;

    if (currentState != GameState.Idle) return;

    double totalPay = GetTotalPay();
    if (playerData.balance < totalPay)
    {
      if (popupManager != null) popupManager.ShowInsufficientFundsError();
      return;
    }

    isAutoPlaying = true;
    autoPlayTotalRounds = total;
    autoPlayRemainingRounds = remaining;

    uiManager.OnAutoPlayStarted();
    RequestSpin();
  }

  #endregion

  #region Free Spins

  private void StartFreeSpins(int spins)
  {
    isInFreeSpins = true;
    freeSpinsRemaining = spins;
    freeSpinsUsed = 0;
    waitingForFreeSpinStart = true;
    AudioManager.Instance?.PlayFreeSpinBg();

    int prevTotal = autoPlayTotalRounds;
    int prevRemaining = autoPlayRemainingRounds;

    if (isAutoPlaying)
    {
      StopAutoPlay();
      wasAutoPlayingBeforeFreeSpins = true;
      savedAutoPlayTotalRounds = prevTotal;
      savedAutoPlayRemainingRounds = (prevTotal != -1) ? (prevRemaining - 1) : -1;
    }

    uiManager.OnFreeSpinsStarted(spins);

    currentState = GameState.Idle;
  }

  internal void StartFirstFreeSpin()
  {
    waitingForFreeSpinStart = false;

    StartCoroutine(DelayBeforeFirstFreeSpin());
  }


  private IEnumerator DelayBeforeFirstFreeSpin()
  {
    yield return new WaitForSeconds(0.5f);
    RequestSpin();
  }

  private IEnumerator DelayBeforeNextFreeSpin()
  {
    yield return new WaitForSeconds(0.3f);

    // Wait for special win popup if it's still active or pending
    while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
    {
      yield return null;
    }

    RequestSpin();
  }

  private void EndFreeSpins(double totalRoundWin, int totalSpinsUsed)
  {
    isInFreeSpins = false;
    freeSpinsRemaining = 0;
    AudioManager.Instance?.PlayMainBg();

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

    wasAutoPlayingBeforeFreeSpins = false;
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
