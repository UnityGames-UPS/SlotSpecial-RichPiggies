using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole free-spin presentation: the trigger popup, the three per-pig intro popups, the
/// background swap, the in-round spin counter and the closing congratulations sequence.
///
/// It owns PRESENTATION only. GameManager still owns the state machine and the server still
/// owns every number — this class is handed a <see cref="FreeSpinRound"/> and animates it.
///
/// Two beats here are unusual and worth knowing before changing anything:
///
///   THE REELS SPIN THROUGH THE INTRO. Once the player dismisses the trigger popup the first
///   free spin is requested immediately and its reels loop, under the black overlay, behind
///   the per-pig popups — for however long those take. <see cref="IsIntroBlocking"/> is what
///   holds GameManager's cosmetic spin timer open, and it is also why this presenter must NOT
///   cancel itself when that spin starts (see <see cref="CancelImmediate"/>).
///
///   THE RED POPUP CLOSES DIFFERENTLY. Blue and Yellow scale down; the wild popup fades out
///   through its CanvasGroup, timed off its glow reaching zero. Its waves animation is
///   REPARENTED out of the popup in Awake so it does not scale with it.
/// </summary>
internal class FreeSpinPresenter : MonoBehaviour
{
  #region Serialized

  [Header("References")]
  [SerializeField] private GameManager gameManager;
  [SerializeField] private UIManager uiManager;
  [SerializeField] private SlotView slotView;
  [SerializeField] private PigMeterController pigMeters;

  [Header("Trigger popup — FreeSpinPopups/TriggeredPopup")]
  [SerializeField] private GameObject triggeredPopup;
  [SerializeField] private RectTransform triggeredPopupRect;

  [Tooltip("Total free spins awarded. Sprite-sheet text — needs a sprite asset from " +
           "Assets/Fonts/CustomTextFonts, or it shows the literal <sprite=N> tags.")]
  [SerializeField] private TMP_Text triggeredCountText;

  [Tooltip("The Ribbons parent's HorizontalLayoutGroup. Its spacing changes with how many " +
           "ribbons are shown, because the art overlaps by design.")]
  [SerializeField] private HorizontalLayoutGroup ribbonsLayout;

  [SerializeField] private GameObject blueRibbon;
  [SerializeField] private GameObject yellowRibbon;
  [SerializeField] private GameObject redRibbon;

  [Tooltip("Layout spacing when all three ribbons show.")]
  [SerializeField] private float ribbonSpacingThree = -33.68f;

  [Tooltip("Layout spacing when two ribbons show.")]
  [SerializeField] private float ribbonSpacingTwo = -194.4f;

  [Tooltip("Layout spacing for a single ribbon. Irrelevant to the layout — one item centres " +
           "either way — but set explicitly so the value is never inherited from the last round.")]
  [SerializeField] private float ribbonSpacingOne = 0f;

  [Tooltip("Dismisses the trigger popup and starts the round. ONE reference, not a pair — the " +
           "trigger popup itself is shared across both orientations.")]
  [SerializeField] private Button okToProceedButton;

  [Tooltip("Second dismiss button, sitting over the blocked spin button. Same effect as Ok. " +
           "Paired, because the spin button it covers is a different object per orientation.")]
  [SerializeField] private Button startButton;
  [SerializeField] private Button startButtonPortrait;

  [Header("Popup scaling — shared by the trigger, blue and yellow popups")]
  [SerializeField] private float popupScaleUpDuration = 0.35f;
  [SerializeField] private Ease popupScaleUpEase = Ease.OutBack;
  [SerializeField] private float popupScaleDownDuration = 0.25f;
  [SerializeField] private Ease popupScaleDownEase = Ease.InBack;

  [Header("Blue popup — FreeSpinPopups/FreeSpinPopup")]
  [SerializeField] private GameObject bluePopup;
  [SerializeField] private RectTransform bluePopupRect;

  [Tooltip("Free spins the Blue meter held at the trigger.")]
  [SerializeField] private TMP_Text blueCountText;
  [SerializeField] private float blueHoldDuration = 1.5f;

  [Header("Yellow popup — FreeSpinPopups/JackpoutPopup")]
  [SerializeField] private GameObject yellowPopup;
  [SerializeField] private RectTransform yellowPopupRect;
  [SerializeField] private float yellowHoldDuration = 1.5f;

  [Header("Red popup — FreeSpinPopups/WildPopup")]
  [SerializeField] private GameObject wildPopup;
  [SerializeField] private RectTransform wildPopupRect;

  [Tooltip("Fades the wild popup out. This popup does NOT scale down like the other two.")]
  [SerializeField] private CanvasGroup wildPopupGroup;

  [Tooltip("Wilds the Red meter held at the trigger.")]
  [SerializeField] private TMP_Text wildCountText;

  [Tooltip("Glow behind the wild popup. Faded 0 -> 1 -> 0; the popup closes as it hits 0.")]
  [SerializeField] private Image wildBgGlow;

  [Tooltip("The waves background animation. REPARENTED to wavesParent on Awake so it plays " +
           "behind the popup at full size instead of scaling with it.")]
  [SerializeField] private ImageAnimation wildWavesAnim;

  [Tooltip("Where the waves animation is reparented to — FreeSpinPopups' first child, so it " +
           "draws behind every popup.")]
  [SerializeField] private Transform wavesParent;

  [Tooltip("Fraction of the wild popup's scale-up at which the waves animation starts, so " +
           "the waves are already moving as the popup arrives.")]
  [Range(0f, 1f)]
  [SerializeField] private float wavesTriggerFraction = 0.6f;

  [SerializeField] private float wavesAnimationSpeed = 5f;
  [SerializeField] private float wildGlowFadeInDuration = 0.5f;
  [SerializeField] private float wildGlowHoldDuration = 0.3f;
  [SerializeField] private float wildGlowFadeOutDuration = 0.5f;
  [SerializeField] private float wildFadeOutDuration = 0.35f;

  [Header("Intro sequencing")]
  [Tooltip("Gap between the background switch settling and the first free spin starting.")]
  [SerializeField] private float afterBgSwitchWait = 1f;

  [Tooltip("Gap between the reels starting and the first per-pig popup.")]
  [SerializeField] private float beforePopupsWait = 0.5f;

  [Tooltip("Gap between one per-pig popup closing and the next opening.")]
  [SerializeField] private float interPopupDelay = 0.5f;

  [Tooltip("Seconds the black overlay takes to fade once the intro or outro is finished.")]
  [SerializeField] private float overlayFadeDuration = 0.4f;

  [Header("Congratulations — SlotObject/WinPanel/CongratulationsPopup")]
  [Tooltip("The CanvasGroup on WinPanel — the SAME one WinPopupController fades.\n\n" +
           "Required, not optional: WinPopupController pins this group's alpha to 0 in its " +
           "Awake, so the congratulations panel sits inside a fully transparent parent and " +
           "activating it alone renders NOTHING. This raises the group for the panel and puts " +
           "it back to 0 afterwards, so the next win popup still fades in from nothing.")]
  [SerializeField] private CanvasGroup winPanelGroup;

  [SerializeField] private GameObject congratsPopup;
  [SerializeField] private RectTransform congratsPopupRect;

  [Tooltip("Looping background animation behind the congratulations panel.")]
  [SerializeField] private ImageAnimation congratsBgAnim;
  [SerializeField] private float congratsBgAnimSpeed = 5f;

  [Tooltip("The round's accumulated win. Sprite-sheet text.")]
  [SerializeField] private TMP_Text congratsTotalWinText;

  [Tooltip("MAXIMUM decimal places on the round total. 3 matches the HUD's \"0.###\".")]
  [SerializeField] private int congratsMaxDecimals = 3;

  [SerializeField] private float congratsHoldDuration = 3f;

  [Tooltip("Coin shower played under the congratulations panel.")]
  [SerializeField] private CoinTossFountain congratsFountain;

  [Header("Motorboat outro")]
  [Tooltip("The piggy-on-a-motorboat animation that crosses the screen once the " +
           "congratulations panel has closed. Drawn above all other UI.")]
  [SerializeField] private ImageAnimation motorboatAnim;
  [SerializeField] private GameObject motorboatRoot;
  [SerializeField] private float motorboatAnimationSpeed = 5f;
  [Tooltip("How far through the motorboat animation (0-1) the background switches back to " +
           "the base game.")]
  [Range(0f, 1f)]
  [SerializeField] private float motorboatBackgroundSwitchAt = 0.4f;

  #endregion

  /// <summary>
  /// True while the intro owns the first free spin's reels — GameManager keeps them looping
  /// for as long as this is set, however long the popups take.
  /// </summary>
  internal bool IsIntroBlocking { get; private set; }

  /// <summary>True while any part of this presentation is on screen.</summary>
  internal bool IsActive { get; private set; }

  private Coroutine sequence;
  private bool dismissRequested;
  private bool introStopRequested;

  // Where the waves animation lived before Awake reparented it, so teardown can put it back
  // rather than leaving the scene permanently rearranged.
  private Transform wavesHomeParent;
  private int wavesHomeSiblingIndex;

  #region Setup

  private void Awake()
  {
    if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
    if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();
    if (slotView == null) slotView = FindFirstObjectByType<SlotView>();
    if (pigMeters == null) pigMeters = FindFirstObjectByType<PigMeterController>();

    // Several of these ship active in the scene so the art can be authored — without this
    // they would all be visible from boot, stacked over the reels.
    SetActive(triggeredPopup, false);
    SetActive(bluePopup, false);
    SetActive(yellowPopup, false);
    SetActive(wildPopup, false);
    SetActive(congratsPopup, false);
    SetActive(motorboatRoot, false);

    DetachWaves();

    if (okToProceedButton != null) okToProceedButton.onClick.AddListener(OnDismissPressed);
    if (startButton != null) startButton.onClick.AddListener(OnDismissPressed);
    if (startButtonPortrait != null) startButtonPortrait.onClick.AddListener(OnDismissPressed);

    SetDismissButtons(false);

    ValidateWiring();
  }

  /// <summary>
  /// Lift the waves animation out of the wild popup so it does not inherit the popup's scale
  /// tween. Its home is cached first: leaving the scene rearranged would make the next
  /// Editor save persist a hierarchy nobody authored.
  /// </summary>
  private void DetachWaves()
  {
    if (wildWavesAnim == null) return;

    var wavesTransform = wildWavesAnim.transform;
    wavesHomeParent = wavesTransform.parent;
    wavesHomeSiblingIndex = wavesTransform.GetSiblingIndex();

    if (wavesParent != null)
    {
      wavesTransform.SetParent(wavesParent, worldPositionStays: false);
      wavesTransform.SetAsFirstSibling();
    }

    SetActive(wildWavesAnim.gameObject, false);
  }

  /// <summary>
  /// Report missing references at startup. Every one of these is null-guarded at runtime,
  /// which is exactly why an unwired field would otherwise show up only as a beat that
  /// silently does nothing in the middle of a round.
  /// </summary>
  private void ValidateWiring()
  {
    if (triggeredPopup == null || triggeredPopupRect == null)
      Debug.LogError("[FreeSpins] The trigger popup is not wired, so a free-spin round would " +
                     "start with no announcement and no way for the player to dismiss it. " +
                     "Assign triggeredPopup and triggeredPopupRect.", this);

    if (okToProceedButton == null && startButton == null && startButtonPortrait == null)
      Debug.LogError("[FreeSpins] No dismiss button is assigned (okToProceedButton, " +
                     "startButton, startButtonPortrait) — the trigger popup could never be " +
                     "dismissed and the round would hang there.", this);
    else if (startButton == null || startButtonPortrait == null)
      Debug.LogWarning("[FreeSpins] Only one orientation's start button is assigned, so the " +
                       "other orientation falls back to Ok alone. Assign both startButton " +
                       "and startButtonPortrait.", this);

    if (ribbonsLayout == null)
      Debug.LogWarning("[FreeSpins] ribbonsLayout is unassigned, so ribbon spacing cannot be " +
                       "set per contributor count and two ribbons will sit at the three-" +
                       "ribbon spacing.", this);

    if (wildPopup != null && wildPopupGroup == null)
      Debug.LogError("[FreeSpins] The wild popup has no CanvasGroup, so it cannot fade out — " +
                     "it would vanish in one frame instead.", this);

    if (congratsPopup == null)
      Debug.LogError("[FreeSpins] congratsPopup is unassigned; every round will end with no " +
                     "total-win panel.", this);
    else if (winPanelGroup == null && congratsPopup.GetComponentInParent<CanvasGroup>() != null)
      Debug.LogError("[FreeSpins] winPanelGroup is unassigned but congratsPopup sits under a " +
                     "CanvasGroup. WinPopupController holds WinPanel's group at alpha 0, so " +
                     "the congratulations panel will activate INVISIBLY. Assign the " +
                     "CanvasGroup on WinPanel.", this);
  }

  private void OnDestroy()
  {
    if (okToProceedButton != null) okToProceedButton.onClick.RemoveListener(OnDismissPressed);
    if (startButton != null) startButton.onClick.RemoveListener(OnDismissPressed);
    if (startButtonPortrait != null) startButtonPortrait.onClick.RemoveListener(OnDismissPressed);

    KillPopupTweens();
  }

  #endregion

  #region Trigger + intro

  /// <summary>
  /// Start the trigger presentation and return at once. The caller waits by polling
  /// <see cref="IsActive"/> — see <see cref="WaitForCompletion"/>.
  ///
  /// The sequence deliberately runs on THIS component rather than on the caller's coroutine:
  /// a cancel has to be able to stop it, and stopping a coroutine the caller is sitting on a
  /// `yield return StartCoroutine` for would leave the caller waiting forever.
  /// </summary>
  internal void BeginTrigger(FreeSpinRound round)
  {
    if (round == null) return;

    if (sequence != null) StopCoroutine(sequence);
    sequence = StartCoroutine(TriggerRoutine(round));
  }

  /// <summary>
  /// Park until the running sequence finishes or is cancelled. Both exits clear
  /// <see cref="IsActive"/>, so this cannot outlive the presentation.
  /// </summary>
  internal IEnumerator WaitForCompletion()
  {
    while (IsActive) yield return null;
  }

  private IEnumerator TriggerRoutine(FreeSpinRound round)
  {
    IsActive = true;
    uiManager?.OnWinPopupOpened();

    // Take the win presentation down but KEEP the grid dark — the popups below play over it,
    // and it stays up right through the intro until the last one has closed.
    slotView?.SettleSymbolsForFeature();

    yield return StartCoroutine(ShowTriggeredPopup(round));

    // The badges belong to the trigger popup; the glow keeps running for the whole round.
    pigMeters?.StopWinnerLoop();

    if (uiManager != null)
    {
      yield return StartCoroutine(uiManager.SwitchBackground(freeSpin: true));
      uiManager.ShowFreeSpinDisplay(round.totalSpins);
    }

    yield return new WaitForSeconds(afterBgSwitchWait);

    // From here the reels are spinning and the first result is on its way. IsIntroBlocking is
    // what stops GameManager settling them until the popups below are done.
    IsIntroBlocking = true;
    introStopRequested = false;
    gameManager?.StartFirstFreeSpin();

    yield return new WaitForSeconds(beforePopupsWait);

    yield return StartCoroutine(PlayIntroPopups(round));

    IsIntroBlocking = false;

    if (slotView != null) yield return StartCoroutine(slotView.FadeOutOverlay(overlayFadeDuration));

    sequence = null;
    IsActive = false;
    uiManager?.OnWinPopupClosed();
  }

  private IEnumerator ShowTriggeredPopup(FreeSpinRound round)
  {
    if (triggeredPopup == null || triggeredPopupRect == null) yield break;

    SpriteNumberFormatter.Apply(triggeredCountText, round.totalSpins,
                                maxDecimals: 0, grouping: false);

    ApplyRibbons(round.contributors);

    dismissRequested = false;
    SetActive(triggeredPopup, true);

    triggeredPopupRect.localScale = Vector3.zero;
    yield return triggeredPopupRect
        .DOScale(1f, popupScaleUpDuration).SetEase(popupScaleUpEase)
        .WaitForCompletion();

    // Only once the popup is fully open. A button live during the scale-up lets a stray tap
    // dismiss an announcement the player never actually saw.
    SetDismissButtons(true);

    while (!dismissRequested) yield return null;

    SetDismissButtons(false);
    AudioManager.Instance?.PlayPopupClose();

    yield return triggeredPopupRect
        .DOScale(0f, popupScaleDownDuration).SetEase(popupScaleDownEase)
        .WaitForCompletion();

    SetActive(triggeredPopup, false);
  }

  /// <summary>
  /// Show one ribbon per contributing pig and space them for that count. The art overlaps, so
  /// the spacing is negative and differs per count rather than being a single constant.
  /// </summary>
  private void ApplyRibbons(PigFeature contributors)
  {
    SetActive(blueRibbon, PigFeatures.Has(contributors, PigFeature.Blue));
    SetActive(yellowRibbon, PigFeatures.Has(contributors, PigFeature.Yellow));
    SetActive(redRibbon, PigFeatures.Has(contributors, PigFeature.Red));

    if (ribbonsLayout == null) return;

    int count = PigFeatures.Count(contributors);
    ribbonsLayout.spacing = count >= 3 ? ribbonSpacingThree
                          : count == 2 ? ribbonSpacingTwo
                          : ribbonSpacingOne;
  }

  /// <summary>
  /// Arm or disarm every way of dismissing the trigger popup, so pressing one immediately
  /// kills the others — the popup must not be dismissable twice while it is closing.
  ///
  /// The two kinds are handled DIFFERENTLY on purpose:
  ///
  ///   Ok lives INSIDE the popup and is part of its art, so it only loses its interactivity.
  ///   Deactivating it would make the button pop out of existence while the panel is still
  ///   scaling down around it.
  ///
  ///   The start buttons are overlays sitting over the spin button, outside the popup, so
  ///   they are switched off outright — a merely non-interactable one would go on covering
  ///   the spin button for the whole round.
  ///
  /// Both start buttons are driven, not just the on-screen orientation's, so a rotation while
  /// the popup is up finds the other one already in the right state.
  /// </summary>
  private void SetDismissButtons(bool active)
  {
    if (okToProceedButton != null) okToProceedButton.interactable = active;

    SetButton(startButton, active);
    SetButton(startButtonPortrait, active);
  }

  private static void SetButton(Button button, bool active)
  {
    if (button == null) return;
    SetActive(button.gameObject, active);
    button.interactable = active;
  }

  private void OnDismissPressed()
  {
    if (dismissRequested) return;
    dismissRequested = true;
  }

  /// <summary>
  /// Cut the intro popups short — the player pressed the normal Stop button, which
  /// GameManager.RequestStop forwards here on every press.
  ///
  /// A no-op outside the intro, so the caller does not have to know whether one is running.
  /// The reels are landed by RequestStop itself; all this adds is unwinding the popups, which
  /// would otherwise go on holding the reels open past the stop the player just asked for.
  /// </summary>
  internal void RequestIntroStop()
  {
    if (!IsIntroBlocking || introStopRequested) return;

    // The popup block sees the flag on its next check and unwinds.
    introStopRequested = true;
  }

  /// <summary>
  /// The per-pig popups, in Yellow -> Blue -> Red order. Each one is skipped if its pig did
  /// not contribute, and the whole block is abandoned the moment Stop is pressed.
  /// </summary>
  private IEnumerator PlayIntroPopups(FreeSpinRound round)
  {
    // Nothing to enable here: the first free spin is already under way, so UIManager's normal
    // OnSpinStarted has the Stop button live exactly as it would be for a base spin.
    // GameManager.RequestStop routes a press to RequestIntroStop while IsIntroBlocking holds.
    bool playedAny = false;

    foreach (var feature in IntroOrder)
    {
      if (!PigFeatures.Has(round.contributors, feature)) continue;
      if (introStopRequested) break;

      if (playedAny)
      {
        yield return StartCoroutine(WaitOrStop(interPopupDelay));
        if (introStopRequested) break;
      }

      playedAny = true;

      if (feature == PigFeature.Yellow) yield return StartCoroutine(PlayYellowPopup());
      else if (feature == PigFeature.Blue) yield return StartCoroutine(PlayBluePopup(round));
      else yield return StartCoroutine(PlayRedPopup(round));
    }

    // Whether it ran to the end or was cut, everything this block put on screen comes down
    // through the one path.
    ResetIntroPopups();

    // Stop can skip the yellow popup, but a Yellow round still needs its jackpot panels.
    if (PigFeatures.Has(round.contributors, PigFeature.Yellow))
      pigMeters?.ShowJackpotPanels();
  }

  /// <summary>Yellow first, then Blue, then Red — the order the reference game uses.</summary>
  private static readonly PigFeature[] IntroOrder =
      { PigFeature.Yellow, PigFeature.Blue, PigFeature.Red };

  private IEnumerator PlayYellowPopup()
  {
    // The six jackpot panels fade in together as the popup announcing them opens.
    pigMeters?.ShowJackpotPanels();
    yield return StartCoroutine(ScalePopup(yellowPopup, yellowPopupRect, yellowHoldDuration));
  }

  private IEnumerator PlayBluePopup(FreeSpinRound round)
  {
    SpriteNumberFormatter.Apply(blueCountText, round.blueAward, maxDecimals: 0, grouping: false);
    yield return StartCoroutine(ScalePopup(bluePopup, bluePopupRect, blueHoldDuration));
  }

  /// <summary>Scale up, hold, scale down — the shape Blue and Yellow share.</summary>
  private IEnumerator ScalePopup(GameObject popup, RectTransform rect, float holdDuration)
  {
    if (popup == null || rect == null) yield break;

    SetActive(popup, true);
    rect.localScale = Vector3.zero;

    yield return rect.DOScale(1f, popupScaleUpDuration).SetEase(popupScaleUpEase)
                     .WaitForCompletion();

    yield return StartCoroutine(WaitOrStop(holdDuration));

    yield return rect.DOScale(0f, popupScaleDownDuration).SetEase(popupScaleDownEase)
                     .WaitForCompletion();

    SetActive(popup, false);
  }

  /// <summary>
  /// The red popup, which is the odd one out: its waves start partway through the scale-up
  /// and play behind it at full size, and it closes by FADING once its glow has fallen back
  /// to zero rather than by scaling down.
  /// </summary>
  private IEnumerator PlayRedPopup(FreeSpinRound round)
  {
    if (wildPopup == null || wildPopupRect == null) yield break;

    SpriteNumberFormatter.Apply(wildCountText, round.redWildCount,
                                maxDecimals: 0, grouping: false);

    SetGlowAlpha(0f);

    if (wildPopupGroup != null) wildPopupGroup.alpha = 1f;

    SetActive(wildPopup, true);
    wildPopupRect.localScale = Vector3.zero;

    var scaleUp = wildPopupRect.DOScale(1f, popupScaleUpDuration).SetEase(popupScaleUpEase);

    // The waves start mid-scale-up so they are already moving when the popup lands, rather
    // than visibly beginning from frame 0 on a popup that is already open.
    if (wildWavesAnim != null && popupScaleUpDuration > 0f)
    {
      yield return new WaitForSeconds(popupScaleUpDuration * wavesTriggerFraction);
      SetActive(wildWavesAnim.gameObject, true);
      CoinAnimator.PlayOnce(wildWavesAnim, wavesAnimationSpeed,
                            () => SetActive(wildWavesAnim.gameObject, false));
    }

    yield return scaleUp.WaitForCompletion();

    if (wildBgGlow != null)
    {
      yield return wildBgGlow.DOFade(1f, wildGlowFadeInDuration).SetEase(Ease.OutQuad)
                             .WaitForCompletion();

      yield return StartCoroutine(WaitOrStop(wildGlowHoldDuration));

      yield return wildBgGlow.DOFade(0f, wildGlowFadeOutDuration).SetEase(Ease.InQuad)
                             .WaitForCompletion();
    }

    // Closes on the glow reaching zero, not on a hold — the fade is the popup's exit.
    if (wildPopupGroup != null)
    {
      yield return wildPopupGroup.DOFade(0f, wildFadeOutDuration).SetEase(Ease.InQuad)
                                 .WaitForCompletion();
    }

    SetActive(wildPopup, false);
  }

  /// <summary>Wait, unless Stop is pressed — then return at once so the block can unwind.</summary>
  private IEnumerator WaitOrStop(float duration)
  {
    float elapsed = 0f;
    while (elapsed < duration && !introStopRequested)
    {
      elapsed += Time.deltaTime;
      yield return null;
    }
  }

  private void SetGlowAlpha(float alpha)
  {
    if (wildBgGlow == null) return;
    var color = wildBgGlow.color;
    color.a = alpha;
    wildBgGlow.color = color;
  }

  /// <summary>Take every intro popup down this frame, whatever state it was in.</summary>
  private void ResetIntroPopups()
  {
    KillPopupTweens();

    SetActive(bluePopup, false);
    SetActive(yellowPopup, false);
    SetActive(wildPopup, false);
    SetActive(triggeredPopup, false);

    if (wildPopupGroup != null) wildPopupGroup.alpha = 1f;
    SetGlowAlpha(0f);

    if (wildWavesAnim != null)
    {
      CoinAnimator.Stop(wildWavesAnim);
      SetActive(wildWavesAnim.gameObject, false);
    }
  }

  #endregion

  #region Outro

  /// <summary>
  /// Start the closing sequence — congratulations panel with the round's total, the pig UI
  /// reset, the background switched back and the motorboat crossing before control returns.
  /// Wait for it with <see cref="WaitForCompletion"/>.
  /// </summary>
  internal void BeginOutro(FreeSpinRound round)
  {
    if (round == null) return;

    if (sequence != null) StopCoroutine(sequence);
    sequence = StartCoroutine(OutroRoutine(round));
  }

  private IEnumerator OutroRoutine(FreeSpinRound round)
  {
    IsActive = true;
    uiManager?.OnWinPopupOpened();

    slotView?.SettleSymbolsForFeature();

    // The pigs go back to normal UNDER the congratulations panel, so the reset is not
    // something the player watches happen on an otherwise idle screen.
    pigMeters?.ExitFreeSpins();

    // The jackpot panels leave together as the congratulations panel arrives.
    pigMeters?.HideJackpotPanels(instant: false);

    yield return StartCoroutine(ShowCongratulations(round));

    // The background switches back part-way through the motorboat crossing, so the boat
    // arrives over the free-spin scene and leaves over the base game.
    Coroutine motorboat = StartCoroutine(PlayMotorboat());

    // Read after PlayMotorboat has started: PlayOnce sets the speed the duration depends on.
    float switchDelay = motorboatAnim != null
        ? motorboatAnim.GetSequenceDuration() * motorboatBackgroundSwitchAt
        : 0f;
    if (switchDelay > 0f) yield return new WaitForSeconds(switchDelay);

    Coroutine backgroundSwitch = uiManager != null
        ? StartCoroutine(uiManager.SwitchBackground(freeSpin: false))
        : null;

    yield return motorboat;

    if (backgroundSwitch != null) yield return backgroundSwitch;

    uiManager?.HideFreeSpinDisplay();

    if (slotView != null) yield return StartCoroutine(slotView.FadeOutOverlay(overlayFadeDuration));

    sequence = null;
    IsActive = false;
    uiManager?.OnWinPopupClosed();
  }

  private IEnumerator ShowCongratulations(FreeSpinRound round)
  {
    if (congratsPopup == null || congratsPopupRect == null)
    {
      Debug.LogError("[FreeSpins] The round finished but congratsPopup / congratsPopupRect is " +
                     "not assigned, so no total-win panel can be shown. Assign " +
                     "SlotObject/WinPanel/CongratulationsPopup in the Inspector.", this);
      yield break;
    }

    SpriteNumberFormatter.Apply(congratsTotalWinText, round.accumulatedWin,
                                maxDecimals: congratsMaxDecimals, grouping: false);

    // The shared WinPanel first. Its CanvasGroup is left at alpha 0 by WinPopupController, and
    // the panel itself may be switched off in the scene — a child activated under either
    // renders nothing at all, which looks exactly like the popup failing to open.
    ShowWinPanel(true);

    // Started before the scale-up so the panel arrives mid-animation.
    CoinAnimator.PlayLoop(congratsBgAnim, congratsBgAnimSpeed);
    congratsFountain?.Play();

    SetActive(congratsPopup, true);

    if (!congratsPopup.activeInHierarchy)
    {
      Transform dead = FirstInactiveAncestor(congratsPopup.transform);
      Debug.LogError($"[FreeSpins] CongratulationsPopup is not visible because " +
                     $"'{(dead != null ? dead.name : "an ancestor")}' is inactive in the " +
                     "scene. Enable it, or move the popup under a live parent.", this);
    }

    congratsPopupRect.localScale = Vector3.zero;

    yield return congratsPopupRect.DOScale(1f, popupScaleUpDuration).SetEase(popupScaleUpEase)
                                  .WaitForCompletion();

    yield return new WaitForSeconds(congratsHoldDuration);

    // Stopped before the panel closes so the shower's own fade overlaps it, rather than the
    // coins cutting out the instant the thing they decorate has gone.
    if (congratsFountain != null && congratsFountain.IsPlaying) congratsFountain.Stop();

    yield return congratsPopupRect.DOScale(0f, popupScaleDownDuration).SetEase(popupScaleDownEase)
                                  .WaitForCompletion();

    CoinAnimator.Stop(congratsBgAnim);
    SetActive(congratsPopup, false);
    ShowWinPanel(false);
  }

  /// <summary>
  /// Raise or drop the shared WinPanel around the congratulations popup.
  ///
  /// Put back to alpha 0 on the way out, because WinPopupController fades this same group in
  /// from 0 for every win popup — leaving it at 1 would make the next win's panel appear
  /// instantly instead of fading.
  /// </summary>
  private void ShowWinPanel(bool visible)
  {
    if (winPanelGroup == null) return;

    if (visible && !winPanelGroup.gameObject.activeSelf)
      winPanelGroup.gameObject.SetActive(true);

    winPanelGroup.alpha = visible ? 1f : 0f;
    winPanelGroup.blocksRaycasts = visible;
  }

  /// <summary>
  /// First switched-off ancestor of <paramref name="target"/>, or null when the whole chain is
  /// live. Activating a child of a dead parent renders nothing, which is indistinguishable
  /// from the popup being broken.
  /// </summary>
  private static Transform FirstInactiveAncestor(Transform target)
  {
    for (Transform t = target; t != null; t = t.parent)
      if (!t.gameObject.activeSelf) return t;

    return null;
  }

  private IEnumerator PlayMotorboat()
  {
    if (motorboatAnim == null)
    {
      SetActive(motorboatRoot, false);
      yield break;
    }

    SetActive(motorboatRoot, true);

    bool finished = false;
    CoinAnimator.PlayOnce(motorboatAnim, motorboatAnimationSpeed, () => finished = true);

    // Backed by the sequence's own computed length. ImageAnimation raises onLoopComplete from
    // an Invoke chain that a disabled object silently drops, and the round must not be able
    // to hang on a decoration.
    float deadline = Time.time + motorboatAnim.GetSequenceDuration() + 1f;
    while (!finished && Time.time < deadline) yield return null;

    CoinAnimator.Stop(motorboatAnim);
    SetActive(motorboatRoot, false);
  }

  #endregion

  #region Teardown

  /// <summary>
  /// Tear the whole presentation down this frame — a new spin has killed it out from under us.
  ///
  /// A NO-OP during the intro. The intro deliberately starts a spin of its own, and that spin
  /// calls SlotView.StartSpin -> KillWinTweens -> here; cancelling then would tear down the
  /// very popups the intro exists to show.
  /// </summary>
  internal void CancelImmediate()
  {
    if (IsIntroBlocking) return;
    if (!IsActive && sequence == null) return;

    // StopAllCoroutines, not just the sequence: stopping an outer coroutine does NOT stop the
    // nested ones it started, and every popup beat is a nested StartCoroutine. Safe only
    // because the sequence lives on THIS component — nobody outside is yielding on it.
    sequence = null;
    StopAllCoroutines();
    ResetIntroPopups();

    congratsFountain?.StopImmediate();
    CoinAnimator.Stop(congratsBgAnim);
    CoinAnimator.Stop(motorboatAnim);

    SetActive(congratsPopup, false);
    ShowWinPanel(false);
    SetActive(motorboatRoot, false);
    SetDismissButtons(false);

    // KillWinTweens released the WIN hold on the darkening panel before calling us, but the
    // feature hold is deliberately immune to that — without this the grid would stay dark for
    // the rest of the session.
    slotView?.ReleaseFeatureOverlay();

    // A cancelled round leaves the pigs mid-presentation: glowing, darkened, Spine frozen.
    pigMeters?.ExitFreeSpins();
    pigMeters?.HideJackpotPanels(instant: true);

    IsActive = false;
    IsIntroBlocking = false;

    // Drop the gate without touching the controls — the spin that cancelled us has already
    // set the buttons up for spinning.
    uiManager?.OnWinPopupCancelled();
  }

  private void KillPopupTweens()
  {
    triggeredPopupRect?.DOKill();
    bluePopupRect?.DOKill();
    yellowPopupRect?.DOKill();
    wildPopupRect?.DOKill();
    congratsPopupRect?.DOKill();

    wildPopupGroup?.DOKill();
    if (wildBgGlow != null) wildBgGlow.DOKill();
  }

  private static void SetActive(GameObject target, bool active)
  {
    if (target != null && target.activeSelf != active) target.SetActive(active);
  }

  #endregion
}
