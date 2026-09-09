using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The win popup that fades in over the win presentation and holds for a tier-dependent beat.
///
/// SlotView keeps replaying its stage 1 — every winning symbol at once — underneath for as
/// long as this popup is up, so the popup reads as an overlay on a live grid rather than as
/// an interruption. Spin is blocked for the whole time, through
/// <see cref="UIManager.OnWinPopupOpened"/>, which raises the same isSpecialWinActive flag the
/// game loop already parks on.
///
/// Every tier is the same sequence with different data, so a new band is an entry in
/// <see cref="winTiers"/> rather than new code. Big, Huge and Mega go further and share ONE
/// popupRoot — the scene has a single BigWinsPopup — differing only in their
/// <see cref="WinTier.tierSprite"/>, holdDuration and threshold.
///
/// Three things run alongside the fade, all optional per tier: the background
/// <see cref="ImageAnimation"/> loop, a <see cref="PopupPulseGroup"/> that pops and breathes
/// the popup's children, and up to two fountains (<see cref="CoinTossFountain"/> for Normal,
/// <see cref="DiamondFountain"/> for the big tiers). All three are torn down in
/// <see cref="ResetPresentation"/>, which is the single exit both a finished hold and a
/// cancelling spin pass through.
/// </summary>
internal class WinPopupController : MonoBehaviour
{
  /// <summary>
  /// One win band. <see cref="minMultiplier"/> is win / total stake; the highest tier whose
  /// threshold the round clears is the one shown.
  /// </summary>
  [Serializable]
  internal class WinTier
  {
    public string name = "Normal";

    [Tooltip("Lowest win/stake multiplier this tier covers. 0 for Normal; the next tier up " +
             "is what ends it.")]
    public double minMultiplier;

    [Tooltip("How long the popup stays up once it has faded in. The skip button cuts it short.")]
    public float holdDuration = 2f;

    [Tooltip("The tier's popup object, e.g. WinPanel/NormalWinPopup. Deactivated on Awake.")]
    public GameObject popupRoot;

    [Header("Background animation")]
    public ImageAnimation bgAnim;

    [Tooltip("Leave empty to use whatever frames are already on bgAnim in the scene.")]
    public List<Sprite> bgFrames = new List<Sprite>();
    public float bgAnimSpeed = 5f;

    [Header("Amount")]
    [Tooltip("Sprite-sheet text. One ref, not a landscape/portrait pair — the win panel " +
             "itself is shared across both orientations.")]
    public TMP_Text amountText;

    [Tooltip("MAXIMUM decimal places; trailing zeros are dropped, so a round number shows " +
             "no decimal point at all. 3 matches the HUD's own \"0.###\" formatting.")]
    public int maxDecimals = 3;

    [Tooltip("Count the amount up from zero across the hold instead of showing it at once. " +
             "Off for Normal, whose hold is too short to read as a count; on for the big " +
             "tiers, where the climb is most of the drama.")]
    public bool countUp;

    [Tooltip("How long the finished total sits still before the popup starts fading, in " +
             "seconds. The count runs for holdDuration MINUS this, so the player always " +
             "gets a beat to read the final number rather than watching it tick up to the " +
             "last frame. A hold at or under this value counts not at all and shows the " +
             "total outright.")]
    public float countUpEndsBefore = 1f;

    [Header("Tier art")]
    [Tooltip("The Image whose sprite names this tier, e.g. BigWinsPopup/Big-Huge-MegaText" +
             "Image. Big, Huge and Mega all share ONE popupRoot and one of these — the " +
             "sprite below is the only thing that distinguishes them.")]
    public Image tierImage;

    [Tooltip("This tier's word: BIG WIN / HUGE WIN / MEGA WIN. Leave both this and " +
             "tierImage empty on a tier whose popup has no such label.")]
    public Sprite tierSprite;

    [Header("Animation")]
    [Tooltip("The scale animation on the popup's children. All tiers sharing a popupRoot " +
             "point at the SAME component — it is a property of the popup, not of the tier.")]
    public PopupPulseGroup pulseGroup;

    [Header("Fountains — either, both, or neither")]
    [Tooltip("Coins tossed up from the bottom bar. The Normal tier's shower.")]
    public CoinTossFountain coinFountain;

    [Tooltip("Diamonds sprayed sideways from behind the panel. The big tiers' shower.")]
    public DiamondFountain diamondFountain;
  }

  [Header("Fade — the CanvasGroup on WinPanel")]
  [SerializeField] private CanvasGroup winPanelGroup;
  [SerializeField] private float fadeInDuration = 0.25f;
  [SerializeField] private float fadeOutDuration = 0.3f;

  [Header("Tiers — ascending by minMultiplier")]
  [SerializeField] private List<WinTier> winTiers = new List<WinTier>();

  [Tooltip("Fewest distinct values a count-up must pass through, which decides how many " +
           "decimals it runs at. A win of 2 counted in whole numbers is only 0, 1, 2 — three " +
           "states across several seconds, which reads as frozen. Requiring ~30 steps takes " +
           "that same win to two decimals instead, so it climbs smoothly. Never exceeds the " +
           "tier's maxDecimals, and never goes below what the total itself needs. 0 disables " +
           "it and counts at the total's own precision.")]
  [SerializeField] private int countUpMinSteps = 30;

  [Header("References")]
  [Tooltip("Full-screen transparent button above all other UI. Only interactable while the " +
           "popup is up; pressing it skips the remaining hold.")]
  [SerializeField] private Button skipButton;

  [SerializeField] private UIManager uiManager;

  [Header("Debug — Editor only")]
  [Tooltip("Enables the hotkeys below. Compiled out of builds entirely, but leave it off " +
           "unless you are actually testing the popup: key 1 shows it with no round behind " +
           "it, which locks the spin controls until it closes.")]
  [SerializeField] private bool debugHotkeys;

  [Tooltip("Amount the hotkeys display. Not run through the tier table — the key picks the " +
           "tier, so this can be any value without changing which popup shows.")]
  [SerializeField] private double debugWinAmount = 12.34;

  // So a tier-table misconfiguration is reported once, not on every winning spin.
  private bool warnedNoTierMatched;

  private Coroutine sequence;
  private Tween fadeTween;
  private WinTier activeTier;
  private bool skipRequested;

  internal bool IsActive { get; private set; }

  private void Awake()
  {
    if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();

    // The NormalWinPopup subtree ships active in the scene. Without this it would be visible
    // from boot, sitting over the reels.
    if (winTiers != null)
      foreach (var tier in winTiers)
        if (tier?.popupRoot != null) tier.popupRoot.SetActive(false);

    if (winPanelGroup != null)
    {
      winPanelGroup.alpha = 0f;
      winPanelGroup.interactable = false;
      winPanelGroup.blocksRaycasts = false;
    }

    if (skipButton != null)
    {
      skipButton.onClick.AddListener(OnSkipPressed);
      skipButton.gameObject.SetActive(false);
    }

    ValidateWiring();
  }

  /// <summary>
  /// Report missing references at startup rather than as a popup that quietly does nothing.
  /// Every one of these is optional at runtime — the sequence null-checks its way past all of
  /// them — which is precisely why an unassigned field is otherwise invisible.
  /// </summary>
  private void ValidateWiring()
  {
    if (winPanelGroup == null)
      Debug.LogError("[WinPopupController] winPanelGroup is unassigned, so the panel can " +
                     "never fade in. Assign the CanvasGroup on WinPanel.", this);

    if (winTiers == null || winTiers.Count == 0)
    {
      Debug.LogError("[WinPopupController] winTiers is empty — no win will ever show a " +
                     "popup. Add a 'Normal' entry with minMultiplier 0.", this);
      return;
    }

    for (int i = 0; i < winTiers.Count; i++)
    {
      var tier = winTiers[i];
      if (tier == null)
      {
        Debug.LogError($"[WinPopupController] winTiers[{i}] is empty.", this);
        continue;
      }

      if (tier.popupRoot == null)
        Debug.LogError($"[WinPopupController] Tier '{tier.name}' has no popupRoot, so it " +
                       "will play its coins and its timer but show no panel. Assign the " +
                       "tier's popup GameObject (e.g. WinPanel/NormalWinPopup).", this);

      if (tier.amountText == null)
        Debug.LogError($"[WinPopupController] Tier '{tier.name}' has no amountText, so the " +
                       "win amount will not be displayed.", this);

      // Half-wired tier art is the nastiest case here: the popup still opens, but showing
      // whatever word the PREVIOUS tier left on the shared Image. A Mega win labelled BIG
      // looks like a payout bug rather than a wiring one.
      if (tier.tierSprite != null && tier.tierImage == null)
        Debug.LogError($"[WinPopupController] Tier '{tier.name}' has a tierSprite but no " +
                       "tierImage, so its sprite can never be applied and the popup will " +
                       "show whichever tier opened last. Assign the shared " +
                       "Big/Huge/MegaTextImage.", this);

      // Counting is silently skipped when there is no room for it, which looks exactly like
      // the countUp flag not working.
      if (tier.countUp && tier.holdDuration <= tier.countUpEndsBefore)
        Debug.LogWarning($"[WinPopupController] Tier '{tier.name}' has countUp on but a " +
                         $"holdDuration ({tier.holdDuration}s) at or under its " +
                         $"countUpEndsBefore ({tier.countUpEndsBefore}s), leaving no time " +
                         "to count in — the total will appear at once. Raise the hold or " +
                         "lower countUpEndsBefore.", this);

      if (tier.tierImage != null && tier.tierSprite == null)
        Debug.LogError($"[WinPopupController] Tier '{tier.name}' has a tierImage but no " +
                       "tierSprite, so it will show whichever tier opened last. Assign this " +
                       "tier's word sprite.", this);
    }
  }

  /// <summary>
  /// Walk up from <paramref name="target"/> and return the first ancestor that is switched
  /// off, or null when the whole chain is live. Activating a child of a disabled parent
  /// renders nothing, which looks exactly like the popup failing to open.
  /// </summary>
  private static Transform FirstInactiveAncestor(Transform target)
  {
    for (Transform t = target; t != null; t = t.parent)
      if (!t.gameObject.activeSelf) return t;

    return null;
  }

#if UNITY_EDITOR
  private void Update()
  {
    if (!debugHotkeys) return;

    // 1-4 — play winTiers[0..3] straight away, with no spin behind them. With the tier list
    // authored ascending that is Normal / Big / Huge / Mega.
    if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
      DebugPlayTier(0);
    if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
      DebugPlayTier(1);
    if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
      DebugPlayTier(2);
    if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
      DebugPlayTier(3);

    // 5 — skip whatever is up, exactly as the on-screen skip button does.
    if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
      DebugSkip();
  }

  private void DebugPlayTier(int index)
  {
    if (IsActive)
    {
      Debug.Log("[WinPopupController] Popup already up — press 5 to skip it first.", this);
      return;
    }

    if (winTiers == null || index < 0 || index >= winTiers.Count)
    {
      Debug.LogError($"[WinPopupController] No tier at index {index} — winTiers holds " +
                     $"{winTiers?.Count ?? 0}. Keys 1-4 map to the first four tiers in " +
                     "order.", this);
      return;
    }

    var tier = winTiers[index];
    if (tier == null)
    {
      Debug.LogError($"[WinPopupController] winTiers[{index}] is empty.", this);
      return;
    }

    Debug.Log($"[WinPopupController] debug: playing tier '{tier.name}' at {debugWinAmount}.",
              this);

    // Straight to the sequence, bypassing SelectTier — the point is to preview one specific
    // tier, not to re-derive which tier debugWinAmount would land in.
    IsActive = true;
    uiManager?.OnWinPopupOpened();
    sequence = StartCoroutine(RunSequence(tier, debugWinAmount, null));
  }

  private void DebugSkip()
  {
    if (!IsActive)
    {
      Debug.Log("[WinPopupController] Nothing to skip — press 1-4 to play a tier.", this);
      return;
    }

    OnSkipPressed();
  }
#endif

  /// <summary>
  /// Whether this round earns a popup. False for a losing spin, and false when no tier
  /// covers the multiplier — which is how a tier table with a gap at the bottom would
  /// silently suppress popups, so keep the lowest tier at minMultiplier 0.
  /// </summary>
  internal bool ShouldShow(double winAmount, double totalPay)
  {
    if (winAmount <= 0) return false;

    if (SelectTier(winAmount, totalPay) != null) return true;

    // Reaching here means a real win matched no tier. Report it once — silently showing
    // nothing looks identical to the popup being broken.
    if (!warnedNoTierMatched)
    {
      warnedNoTierMatched = true;
      double multiplier = totalPay > 0 ? winAmount / totalPay : 0;

      if (winTiers == null || winTiers.Count == 0)
        Debug.LogError("[WinPopupController] A win of " + winAmount + " matched no tier " +
                       "because the winTiers list is empty. Add a 'Normal' entry with " +
                       "minMultiplier 0.", this);
      else
        Debug.LogError($"[WinPopupController] A win of {winAmount} at {multiplier:0.##}x " +
                       "stake matched no tier — every entry's minMultiplier is above it. " +
                       "The lowest tier must be minMultiplier 0 to catch every win.", this);
    }

    return false;
  }

  /// <summary>
  /// Show the popup for this win and run it to completion. <paramref name="onClosed"/> fires
  /// after the fade-out, once spin has been handed back.
  /// </summary>
  internal void Show(double winAmount, double totalPay, Action onClosed)
  {
    var tier = SelectTier(winAmount, totalPay);
    if (tier == null)
    {
      onClosed?.Invoke();
      return;
    }

    if (sequence != null) StopCoroutine(sequence);

    // Raise the gate BEFORE returning. SlotView calls onComplete immediately after this, and
    // that runs GameManager.ProcessSpecialFeaturesAfterWin, whose first statement checks
    // isSpecialWinActive. Set it a frame later and the round resolves under the popup.
    IsActive = true;
    uiManager?.OnWinPopupOpened();

    sequence = StartCoroutine(RunSequence(tier, winAmount, onClosed));
  }

  /// <summary>
  /// Tear the popup down this frame with no fade and no callback — the next spin has killed
  /// the presentation out from under it.
  /// </summary>
  internal void CancelImmediate()
  {
    if (!IsActive && sequence == null) return;

    if (sequence != null)
    {
      StopCoroutine(sequence);
      sequence = null;
    }

    fadeTween?.Kill();
    fadeTween = null;

    // ResetPresentation stops the active tier's fountains and pulse — nothing extra to do
    // here beyond passing instant, which is what turns the fades into cuts.
    ResetPresentation(instant: true, restoreControls: false);
  }

  private IEnumerator RunSequence(WinTier tier, double winAmount, Action onClosed)
  {
    activeTier = tier;
    skipRequested = false;

    // The panel itself, before the tier's popup inside it: Awake hides the popup by
    // deactivating it, but the shared WinPanel may also be switched off in the scene, and a
    // child activated under a dead parent renders nothing.
    if (winPanelGroup != null && !winPanelGroup.gameObject.activeSelf)
      winPanelGroup.gameObject.SetActive(true);

    if (tier.popupRoot != null)
    {
      tier.popupRoot.SetActive(true);

      // Now that both are on, anything still dark is an ancestor further up that this
      // component does not own. Name it rather than showing nothing.
      if (!tier.popupRoot.activeInHierarchy)
      {
        Transform dead = FirstInactiveAncestor(tier.popupRoot.transform);
        string deadName = dead != null ? dead.name : "an ancestor";
        Debug.LogError($"[WinPopupController] Tier '{tier.name}' popupRoot " +
                       $"'{tier.popupRoot.name}' is not visible because '{deadName}' is " +
                       "inactive in the scene. Enable it, or move the popup under a live " +
                       "parent.", this);
      }
    }

    // Which word this tier shows. Big / Huge / Mega share one popupRoot and one Image, so
    // without this the popup would keep whichever sprite the last tier left on it.
    if (tier.tierImage != null && tier.tierSprite != null)
      tier.tierImage.sprite = tier.tierSprite;

    // A counting tier opens on zero and climbs during the hold; every other tier shows the
    // total from the first frame.
    float countUpDuration = tier.countUp
        ? Mathf.Max(0f, tier.holdDuration - tier.countUpEndsBefore)
        : 0f;

    // Decided ONCE, from the final total, and held for every frame of the count. Formatting
    // each frame with maxDecimals instead would let the width change as the value climbs —
    // 0 -> "0", 12.5 -> "12.5", 12.75 -> "12.75" — so the digits would visibly shuffle and
    // the decimal point slide about. Because the width comes from the total, the count still
    // ends on exactly the string Format would have produced.
    int decimals = countUpDuration > 0f
        ? CountUpDecimals(winAmount, tier.maxDecimals)
        : SpriteNumberFormatter.DecimalsUsed(winAmount, tier.maxDecimals);

    SpriteNumberFormatter.ApplyFixed(tier.amountText, countUpDuration > 0f ? 0d : winAmount,
                                     decimals);

    // Start the background loop BEFORE the fade, so the popup arrives mid-animation rather
    // than visibly starting from frame 0 once it is already on screen.
    StartBackgroundAnimation(tier);

    // Likewise the pop: it runs concurrently with the fade in, so the panel scales up as it
    // appears rather than materialising and only then coming to life.
    tier.pulseGroup?.Play();

    PlayFountains(tier);

    fadeTween?.Kill();
    if (winPanelGroup != null)
    {
      winPanelGroup.blocksRaycasts = true;
      fadeTween = winPanelGroup.DOFade(1f, fadeInDuration).SetEase(Ease.OutQuad);
      yield return fadeTween.WaitForCompletion();
    }

    // Only skippable once it is actually readable — a button live during the fade would let a
    // stray tap dismiss a popup the player never saw.
    if (skipButton != null) skipButton.gameObject.SetActive(true);

    // Whether the exact total has been written yet. The counted value never quite reaches
    // it: interpolation is sampled at elapsed < countUpDuration, so the closest it gets is
    // one frame short — 499.99 of a 500.005 win. Landing on the real number has to be its
    // own explicit step.
    bool settled = countUpDuration <= 0f;

    float elapsed = 0f;
    while (elapsed < tier.holdDuration && !skipRequested)
    {
      elapsed += Time.deltaTime;

      // Driven from the hold loop rather than a parallel tween, so the count cannot outlive
      // the hold and there is no second thing for skip and cancel to have to kill.
      if (!settled)
      {
        if (elapsed < countUpDuration)
        {
          SpriteNumberFormatter.ApplyFixed(tier.amountText,
                                           winAmount * (elapsed / countUpDuration),
                                           decimals);
        }
        else
        {
          // The instant the count is up, not when the hold is. Deferring this to after the
          // loop is what left the almost-right number on screen for the whole of
          // countUpEndsBefore, snapping to the total only as the popup began to fade — the
          // exact opposite of the intent, which is that the finished total is what the
          // player gets to sit and read.
          SpriteNumberFormatter.ApplyFixed(tier.amountText, winAmount, decimals);
          settled = true;
        }
      }

      yield return null;
    }

    // Only reachable via skip, which cuts the count partway and leaves a part-total on
    // screen. A tier that counted to the end, or never counted at all, is already settled.
    if (!settled) SpriteNumberFormatter.ApplyFixed(tier.amountText, winAmount, decimals);

    if (skipButton != null) skipButton.gameObject.SetActive(false);

    // Stopped here rather than in ResetPresentation so the fountain's own fade overlaps the
    // panel's, instead of the diamonds cutting out the instant the panel has gone.
    StopFountains(tier, instant: false);

    fadeTween?.Kill();
    if (winPanelGroup != null)
    {
      fadeTween = winPanelGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.InQuad);
      yield return fadeTween.WaitForCompletion();
    }

    sequence = null;
    ResetPresentation(instant: false, restoreControls: true);

    onClosed?.Invoke();
  }

  /// <summary>
  /// The highest tier the round clears. The list is authored ascending, but this does not
  /// rely on that — it takes the best match either way, so a tier appended out of order in
  /// the Inspector still behaves.
  /// </summary>
  private WinTier SelectTier(double winAmount, double totalPay)
  {
    if (winTiers == null || winAmount <= 0) return null;

    // A zero stake would make every win infinitely large. Treat it as "no multiplier known"
    // and fall through to the lowest tier rather than picking the top one.
    double multiplier = totalPay > 0 ? winAmount / totalPay : 0;

    WinTier best = null;
    foreach (var tier in winTiers)
    {
      if (tier == null || multiplier < tier.minMultiplier) continue;
      if (best == null || tier.minMultiplier >= best.minMultiplier) best = tier;
    }

    return best;
  }

  private void StartBackgroundAnimation(WinTier tier)
  {
    if (tier.bgAnim == null) return;

    // CoinAnimator is the shared "loop this ImageAnimation safely" helper — it clears the
    // pending Invoke a StartOnAwake / StartonEnable prefab queues, which StopAnimation alone
    // does not cancel. Nothing coin-specific about it.
    var frames = (tier.bgFrames != null && tier.bgFrames.Count > 0)
        ? tier.bgFrames
        : tier.bgAnim.textureArray;

    if (frames == null || frames.Count == 0)
    {
      Debug.LogError($"[WinPopupController] Tier '{tier.name}' has a bgAnim with no frames, " +
                     "so its background will not animate. Assign bgFrames, or frames on the " +
                     "ImageAnimation itself.", this);
      return;
    }

    CoinAnimator.Play(tier.bgAnim, frames, tier.bgAnimSpeed);
  }

  /// <summary>
  /// How many decimals a count-up should run at: enough that it passes through at least
  /// <see cref="countUpMinSteps"/> distinct values, but never fewer than the total itself
  /// needs and never more than the tier allows.
  ///
  /// Precision here is really a proxy for granularity. Counting to a 500 win in whole
  /// numbers is 500 steps and looks perfectly smooth; counting to a 2 win the same way is
  /// three, and a big-win popup that sits on "1" for a second and a half looks broken. Small
  /// wins reach big tiers whenever the stake is small — the tier is chosen by win/stake, not
  /// by the cash amount — so this is a normal case, not an edge one.
  ///
  /// The trade-off is that such a win displays as "2.00" rather than the HUD's "2", for the
  /// whole popup including its final frame. That is deliberate: switching to the shorter form
  /// at the end would shift the digits at the exact moment the player is reading them, which
  /// is the jitter the fixed width exists to prevent.
  /// </summary>
  private int CountUpDecimals(double winAmount, int maxDecimals)
  {
    int decimals = SpriteNumberFormatter.DecimalsUsed(winAmount, maxDecimals);
    if (countUpMinSteps <= 0 || winAmount <= 0) return decimals;

    // Each extra decimal multiplies the number of steps by ten. Bounded by maxDecimals, so
    // this runs at most a couple of times.
    while (decimals < maxDecimals && winAmount * Math.Pow(10, decimals) < countUpMinSteps)
      decimals++;

    return decimals;
  }

  /// <summary>
  /// Start whichever showers this tier owns. Both slots are optional and independent — a tier
  /// may have coins, diamonds, both, or neither.
  /// </summary>
  private static void PlayFountains(WinTier tier)
  {
    if (tier == null) return;

    tier.coinFountain?.Play();
    tier.diamondFountain?.Play();
  }

  /// <summary>
  /// Stop this tier's showers. <paramref name="instant"/> cuts them dead this frame; without
  /// it they stop spawning and fade, letting whatever is already in the air finish its
  /// flight.
  ///
  /// The IsPlaying guard on the fading path matters because a normal close calls this twice
  /// — once as the panel starts fading, again from ResetPresentation. Without it the second
  /// call would kill the first fade partway and restart a full-length one, leaving the
  /// shower visible after the panel it decorates has gone. The instant path is unguarded on
  /// purpose: it is what has to kill that fade.
  /// </summary>
  private static void StopFountains(WinTier tier, bool instant)
  {
    if (tier == null) return;

    if (instant)
    {
      tier.coinFountain?.StopImmediate();
      tier.diamondFountain?.StopImmediate();
      return;
    }

    if (tier.coinFountain != null && tier.coinFountain.IsPlaying) tier.coinFountain.Stop();
    if (tier.diamondFountain != null && tier.diamondFountain.IsPlaying)
      tier.diamondFountain.Stop();
  }

  private void ResetPresentation(bool instant, bool restoreControls)
  {
    if (activeTier != null)
    {
      CoinAnimator.Stop(activeTier.bgAnim);

      // Must happen on EVERY exit, not only on cancel: the pulse leaves the popup's children
      // at whatever scale the yoyo was passing through, and that scale would still be there
      // the next time the popup opens.
      activeTier.pulseGroup?.Stop(instant);

      // A normal close has already stopped these at the top of the fade out. This catches
      // the cancel path, and is a no-op when they are stopped twice.
      StopFountains(activeTier, instant);

      // The diamond layer is a CHILD of the popup root, so the SetActive(false) two lines
      // below would strand every diamond still in the air: deactivation kills the flight
      // without firing the pool callback, and the item never leaves ItemsInUse. Recycle
      // unconditionally — by this point the panel has finished fading, so nothing visible is
      // being cut. The coin fountain needs no equivalent; its layer is a sibling of the
      // popup, not a child, and it is left to finish its own fade.
      activeTier.diamondFountain?.StopImmediate();

      SpriteNumberFormatter.Clear(activeTier.amountText);
      if (activeTier.popupRoot != null) activeTier.popupRoot.SetActive(false);
      activeTier = null;
    }

    if (winPanelGroup != null)
    {
      if (instant) winPanelGroup.alpha = 0f;
      winPanelGroup.blocksRaycasts = false;
      winPanelGroup.interactable = false;
    }

    if (skipButton != null) skipButton.gameObject.SetActive(false);

    skipRequested = false;
    IsActive = false;

    // Last, and always: drop the gate that is holding the round. Clearing IsActive first
    // matters because EnableControlsAfterWinAnimation early-returns while a special win is
    // still flagged. A cancel skips the control restore — the spin that cancelled us has
    // already set the buttons up for spinning.
    if (restoreControls) uiManager?.OnWinPopupClosed();
    else uiManager?.OnWinPopupCancelled();
  }

  private void OnSkipPressed()
  {
    if (!IsActive) return;
    AudioManager.Instance?.PlayPopupClose();
    skipRequested = true;
  }

  private void OnDestroy()
  {
    fadeTween?.Kill();
    if (skipButton != null) skipButton.onClick.RemoveListener(OnSkipPressed);
  }
}
