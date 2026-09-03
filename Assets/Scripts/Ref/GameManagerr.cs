using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

public class GameManagerr : MonoBehaviour
{
  [Header("Scripts")]
  [SerializeField] private SlotController slotManager;
  [SerializeField] private UIManager uIManager;
  [SerializeField] private SocketController socketController;
  [SerializeField] private AudioController audioController;

  [Header("For Spins")]
  [SerializeField] private Button SlotStart_Button;
  [SerializeField] private Button StopSpin_Button;
  [SerializeField] private Button ToatlBetMinus_Button;
  [SerializeField] private Button TotalBetPlus_Button;
  [SerializeField] private TMP_Text[] totalBetDigits; // per-digit BET HUD (left-to-right, dot excluded)
  [SerializeField] private bool isSpinning;
  internal bool IsSpinning => isSpinning;
  [SerializeField] private Button TurboON_Button;
  [SerializeField] private Button TurboOFF_Button;

  [Header("For Auto Spins")]
  [SerializeField] private Button AutoSpinStop_Button;
  [SerializeField] internal bool isAutoSpin;
  [SerializeField] private float autoSpinDelay = 1.5f;
  [SerializeField] private float autoSpinHoldDuration = 5f; // hold Spin this long (s) to start auto-spin

  private double currentBalance;
  [SerializeField] private double currentTotalBet;
  internal double CurrentTotalBet => currentTotalBet;
  [SerializeField] internal int betCounter = 0;

  private Coroutine autoSpinRoutine;
  private int _autoSpinRemaining;
  private bool _autoUntilFeature;
  private bool initiated;
  [SerializeField] internal bool turboMode;
  [SerializeField] internal bool immediateStop;
  private Coroutine spinRoutine;

  private Coroutine _holdRoutine;
  private bool _holdConsumed; // true once a hold has started auto-spin, so release won't also single-spin


  void Start()
  {
    // [Superstars] The Spin button now drives both single-spin (tap) and auto-spin (hold). The
    // press/hold detection is installed in SetupSpinHoldButton instead of a plain onClick listener.
    SetupSpinHoldButton();
    // [Superstars] No separate auto-spin button anymore — auto-spin is started by holding Spin.
    // SetButton(AutoSpin_Button, () => StartAutoSpin(-1), true);
    SetButton(AutoSpinStop_Button, () => StartCoroutine(StopAutoSpinCoroutine()));
    SetButton(ToatlBetMinus_Button, () => { OnBetChange(false); }, "betminus");
    SetButton(TotalBetPlus_Button, () => { OnBetChange(true); }, "betplus");
    SetButton(TurboON_Button, () => { ToggleTurboMode(); });
    SetButton(TurboOFF_Button, () => { ToggleTurboMode(); });
    SetButton(StopSpin_Button, () => StartCoroutine(StopSpin()));

    socketController.OnInit = InitGame;
    socketController.OnBalanceSync = HandleBalanceSync;
    uIManager.ToggleAudio = audioController.SetUserMute;
    uIManager.playButtonAudio = (s) => audioController.Play(s);
    uIManager.OnExit = () => socketController.CloseSocket();
    uIManager.OnLowBalConfirm = () => ToggleButtonGrp(true);
    socketController.ShowDisconnectionPopup = uIManager.DisconnectionPopup;

    ApplyTurboButtonVisibility();
  }

  private void SetButton(Button button, Action action, string clickAudio = "uibutton")
  {
    if (button == null) return;

    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() =>
    {
      if (!string.IsNullOrEmpty(clickAudio)) uIManager.playButtonAudio?.Invoke(clickAudio);
      action?.Invoke();
    });
  }

  // Wires press-and-hold on the Spin button: a quick tap runs one spin, holding for
  // autoSpinHoldDuration starts auto-spin. Uses EventTrigger (added at runtime) because Button.onClick
  // fires on release and can't distinguish a tap from a hold.
  private void SetupSpinHoldButton()
  {
    if (SlotStart_Button == null) return;

    SlotStart_Button.onClick.RemoveAllListeners();

    var go = SlotStart_Button.gameObject;
    var trigger = go.GetComponent<EventTrigger>();
    if (trigger == null) trigger = go.AddComponent<EventTrigger>();
    trigger.triggers.Clear();

    AddTrigger(trigger, EventTriggerType.PointerDown, OnSpinPointerDown);
    AddTrigger(trigger, EventTriggerType.PointerUp, OnSpinPointerUp);
    // Dragging off the button cancels the pending hold so a stray release elsewhere can't spin.
    AddTrigger(trigger, EventTriggerType.PointerExit, CancelHold);
  }

  private void AddTrigger(EventTrigger trigger, EventTriggerType type, Action callback)
  {
    var entry = new EventTrigger.Entry { eventID = type };
    entry.callback.AddListener(_ => callback());
    trigger.triggers.Add(entry);
  }

  private void OnSpinPointerDown()
  {
    // EventTrigger ignores Button.interactable, so guard manually: don't react mid-spin / mid-auto.
    if (!SlotStart_Button.interactable || isSpinning || isAutoSpin) return;

    _holdConsumed = false;
    uIManager.playButtonAudio?.Invoke("uibutton");

    if (_holdRoutine != null) StopCoroutine(_holdRoutine);
    _holdRoutine = StartCoroutine(SpinHoldRoutine());
  }

  private IEnumerator SpinHoldRoutine()
  {
    yield return new WaitForSeconds(autoSpinHoldDuration);
    _holdRoutine = null;

    if (!SlotStart_Button.interactable || isSpinning || isAutoSpin) yield break;

    _holdConsumed = true; // release will skip the single spin
    StartAutoSpin(-1);
  }

  private void OnSpinPointerUp()
  {
    CancelHold();
    // Released before the hold threshold -> normal single spin.
    if (!_holdConsumed) ExecuteSpin();
  }

  private void CancelHold()
  {
    if (_holdRoutine != null)
    {
      StopCoroutine(_holdRoutine);
      _holdRoutine = null;
    }
  }

  void InitGame()
  {
    if (!initiated)
    {
      initiated = true;
      betCounter = 0;
      // [Superstars] No paylines in init model; the selected bet is the total stake per spin.
      // (Diamond Riches multiplied lineBet by lines.Count.)
      currentTotalBet = socketController.InitLineBetData.bets[betCounter];
      currentBalance = socketController.PlayerData.balance;
      uIManager.UpdatePlayerInfo();
      TextFormatter.ApplyMoneyDigits(totalBetDigits, currentTotalBet);
      if (currentBalance < currentTotalBet)
      {
        ToggleButtonGrp(false);
        uIManager.LowBalPopup();
      }

      uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    }
    else
    {
      uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    }
  }

  private void HandleBalanceSync(double newBalance)
  {
    currentBalance = newBalance;
    uIManager.SetPlayerBalance(newBalance);

    if (currentBalance < currentTotalBet)
    {
      ToggleButtonGrp(false);
      uIManager.LowBalPopup();
    }
    else if (uIManager.IsLowBalPopupOpen)
    {
      uIManager.CloseLowBalPopup();
      ToggleButtonGrp(true);
    }
  }

  void ExecuteSpin()
  {
    // Block re-entry: during auto / free spin AutoSpinRoutine and RunFreeSpins drive SpinRoutine
    // without populating the spinRoutine field, so the null-check below would otherwise let a stray
    // click launch a parallel SpinRoutine that fights the active one for the reel tweens.
    if (isAutoSpin || isSpinning) return;

    if (spinRoutine != null)
    {
      StopCoroutine(spinRoutine);
      spinRoutine = null;
      isSpinning = false;
    }
    spinRoutine = StartCoroutine(SpinRoutine());
  }

  internal void StartAutoSpin(int count)
  {
    if (isAutoSpin || isSpinning) return;
    _autoUntilFeature = (count < 0);
    _autoSpinRemaining = count;
    isAutoSpin = true;
    SetAutoSpinUI(true);
    autoSpinRoutine = StartCoroutine(AutoSpinRoutine());
  }

  void ToggleTurboMode()
  {
    turboMode = !turboMode;
    ApplyTurboButtonVisibility();
  }

  // ON button (no cross) visible when turbo is on; OFF button (cross) visible when turbo is off.
  void ApplyTurboButtonVisibility()
  {
    if (TurboON_Button) TurboON_Button.gameObject.SetActive(turboMode);
    if (TurboOFF_Button) TurboOFF_Button.gameObject.SetActive(!turboMode);
  }

  IEnumerator AutoSpinRoutine()
  {
    if (isSpinning)
    {
      if (spinRoutine != null)
      {
        StopCoroutine(spinRoutine);
        spinRoutine = null;
      }
      isSpinning = false;
    }

    while (isAutoSpin)
    {
      yield return SpinRoutine();

      // _autoUntilFeature cutoff is handled mid-spin in HandleAutoUntilFeatureCutoff (right after the
      // result arrives), so isAutoSpin will already be false here and the while-loop exits on its own.
      if (!_autoUntilFeature)
      {
        _autoSpinRemaining--;
        if (_autoSpinRemaining <= 0) break;
      }

      yield return new WaitForSeconds(autoSpinDelay);
    }

    // CLEAN EXIT (NO coroutine calls)
    isSpinning = false;
    isAutoSpin = false;

    // --- OLD (Age of Gods): only restored the auto-spin button
    // AutoSpin_Button.gameObject.SetActive(true);
    SetAutoSpinUI(false);
    ToggleButtonGrp(true);
  }

  private IEnumerator StopAutoSpinCoroutine()
  {
    isAutoSpin = false;

    // Lock stop button so repeat clicks no-op while the current spin finishes.
    if (AutoSpinStop_Button) AutoSpinStop_Button.interactable = false;

    yield return new WaitUntil(() => !isSpinning);

    SetAutoSpinUI(false);

    if (!uIManager.IsLowBalPopupOpen)
      ToggleButtonGrp(true);

    if (spinRoutine != null)
    {
      StopCoroutine(spinRoutine);
      spinRoutine = null;
    }

    if (autoSpinRoutine != null)
    {
      StopCoroutine(autoSpinRoutine);
      autoSpinRoutine = null;
    }
  }
  IEnumerator SpinRoutine()
  {
    ToggleButtonGrp(false);
    bool start = OnSpinStart();

    // // ===== CASE 1: Spin did not start (low balance etc.)
    if (!start)
    {
      spinRoutine = null;
      isSpinning = false;

      if (isAutoSpin)
      {
        StartCoroutine(StopAutoSpinCoroutine());
      }

      yield break;
    }

    yield return OneSpinFlow();

    if (!isAutoSpin)
    {
      ToggleButtonGrp(true);
    }
    isSpinning = false;
    spinRoutine = null;
  }

  IEnumerator OneSpinFlow()
  {
    immediateStop = false;
    yield return OnSpin();
    // yield return new WaitForSecondsRealtime(0.5f);
    yield return OnSpinEnd();

    // Auto / free spin loops must wait for a big-win celebration to fully reset before the next spin
    // kicks off. Low wins return immediately (no big-win active). A manual spin instead skips the
    // celebration via OnSpinStart -> ResetWinForNewSpin.
    if (isAutoSpin) yield return uIManager.WaitWinAnimDone();
  }

  IEnumerator StopSpin()
  {
    if (immediateStop)
      yield break;
    immediateStop = true;
    StopSpin_Button.gameObject.SetActive(false);
    StopSpin_Button.interactable = false;
    yield return new WaitUntil(() => !isSpinning);
    immediateStop = false;
    StopSpin_Button.interactable = true;
  }

  bool OnSpinStart()
  {
    if (currentBalance < currentTotalBet)
    {
      uIManager.LowBalPopup();
      return false;
    }
    // Reset win visuals for the new spin. If a big-win celebration is still showing this skips it
    // (snap to final + graceful fade-out) while the spin proceeds.
    uIManager.ResetWinForNewSpin();
    isSpinning = true;
    return true;
  }

  IEnumerator OnSpin()
  {
    uIManager.SetPlayerBalance(socketController.PlayerData.balance - currentTotalBet);

    immediateStop = false;
    if (!turboMode)
    {
      StopSpin_Button.gameObject.SetActive(true);
      StopSpin_Button.interactable = true;
    }

    yield return slotManager.StartSpin();

    socketController.AccumulateResult(betCounter);
    yield return new WaitUntil(() => socketController.isResultdone);
    
    slotManager.PopulateSlotMatrix(socketController.ResultData.matrix);

    if(turboMode)
    {
      immediateStop = true;
    }

    if (!immediateStop)
    {
      int waitFor = 10;
      for (int i = 0; i < waitFor; i++)
      {
        if (immediateStop)
        {
          break;
        }
        yield return new WaitForSecondsRealtime(0.1f);
      }
    }

    // Reels are about to settle — kill the looping spin sound so it doesn't run under the stop sfx.
    audioController.Stop("reelspin");
    yield return slotManager.StopSpin(() => audioController.Play("reelstop"), socketController.ResultData.matrix);
    immediateStop = false;

    if (StopSpin_Button.gameObject.activeSelf)
      StopSpin_Button.gameObject.SetActive(false);
  }

  IEnumerator OnSpinEnd()
  {
    currentBalance = socketController.ResultData.player.balance;
    uIManager.UpdatePlayerInfo();

    yield return null;
  }

  void SetAutoSpinUI(bool autoActive)
  {
    if (AutoSpinStop_Button)
    {
      AutoSpinStop_Button.gameObject.SetActive(autoActive);
      AutoSpinStop_Button.interactable = autoActive;
    }
  }

  internal void ToggleButtonGrp(bool toggle)
  {
    if (SlotStart_Button) SlotStart_Button.interactable = toggle;
    if (ToatlBetMinus_Button) ToatlBetMinus_Button.interactable = toggle;
    if (TotalBetPlus_Button) TotalBetPlus_Button.interactable = toggle;
    uIManager.paytable_Button.interactable = toggle;
    TurboON_Button.interactable = toggle;
    TurboOFF_Button.interactable = toggle;
  }

  private void OnBetChange(bool inc)
  {
    int lastIndex = socketController.InitLineBetData.bets.Count - 1;
    if (inc)
    {
      betCounter = (betCounter >= lastIndex) ? 0 : betCounter + 1;
    }
    else
    {
      betCounter = (betCounter <= 0) ? lastIndex : betCounter - 1;
    }
    currentTotalBet = socketController.InitLineBetData.bets[betCounter];
    TextFormatter.ApplyMoneyDigits(totalBetDigits, currentTotalBet);
    // Paytable payouts are bet-independent ("BET x {payout}") and populated only at init, so no
    // re-populate on bet change.
  }
}
