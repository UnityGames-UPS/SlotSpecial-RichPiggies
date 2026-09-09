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
                         $" (activeFeature: {(lastResult.activeFeature != null && lastResult.activeFeature.Count > 0 ? string.Join(", ", lastResult.activeFeature) : "none")}, " +
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
      double reelStopBalance = lastResult.playerData != null ? lastResult.playerData.balance : 0;

      playerData = new PlayerData
      {
        balance = reelStopBalance,
        currentBetIndex = lastResult.playerData != null ? lastResult.playerData.currentBetIndex : currentBetIndex
      };
    }

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

    // [FREE SPINS TODO] Rich Piggies triggers free spins on any combination of Blue /
    // Yellow / Red piggies, so the trigger presentation is net-new and lands here — animate
    // the triggering piggies, hand off to a popup, then resume. The free-spin STATE machine
    // below (StartFreeSpins / EndFreeSpins) is intact and stays inert while the server sends
    // no freeSpinData.

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

    // CRITICAL FIX: Update free spin counter IMMEDIATELY when server response arrives
    // This ensures the display shows the exact server-authoritative played count without lag
    if (isInFreeSpins && result.serverSpinsRemaining >= 0)
    {
      freeSpinsRemaining = result.serverSpinsRemaining;
      freeSpinsUsed = result.serverSpinsUsed;
      // The wheel used to award extra free spins that were withheld from this count until
      // the player pressed Take on its popup. With the wheel gone the server total is shown
      // as-is; a Rich Piggies retrigger will need its own deferral if it awards mid-round.
      uiManager.UpdateFreeSpinCount(freeSpinsUsed, result.serverTotalSpins);
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
    while (uiManager.IsSpecialWinActive)
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
