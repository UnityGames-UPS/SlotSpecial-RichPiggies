using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Spine.Unity;

/// <summary>
/// The three persistent pig meters and the six jackpot payouts.
///
/// This lives apart from UIManager on purpose: UIManager's existing jackpot block drives
/// the PLATFORM jackpot ticker (four tiers, dollar strings pushed by the jackpot:sync socket
/// event), which is a different thing entirely from Rich Piggies' six per-bet jackpot
/// meters. It still follows UIManager's conventions to the letter — every control has a
/// landscape and a portrait reference and both are written together, because the player can
/// rotate at any moment and the off-screen set must already be correct when they do.
///
/// Everything shown here is server state. The client never accumulates a meter; it renders
/// the value the server sent and, during the coin beat, walks the display from the previous
/// value to that one.
/// </summary>
public class PigMeterController : MonoBehaviour
{
  /// <summary>One jackpot tier: its two texts, its two coin destinations, its coin colour.</summary>
  [System.Serializable]
  internal class JackpotTierUI
  {
    [Tooltip("Meter dictionary key from the server, CASE-SENSITIVE: Mega, Grand, Major, " +
             "Maxi, Minor or Mini.")]
    public string tier;

    [Tooltip("Rendered through SpriteNumberFormatter — both texts need a sprite asset from " +
             "Assets/Fonts/CustomTextFonts, or they show the literal <sprite=N> tags.")]
    public TMP_Text valueText;

    public TMP_Text valueTextPortrait;

    [Tooltip("Where a jackpot coin for this tier flies to. Landscape.")]
    public RectTransform target;

    [Tooltip("Where a jackpot coin for this tier flies to. Portrait.")]
    public RectTransform targetPortrait;

    [Tooltip("This tier's coin PNG sequence, in order. Looped for the whole flight.")]
    public List<Sprite> coinFrames = new List<Sprite>();

    [Header("Yellow free-spin collection")]
    [Tooltip("Jackpots/<Tier>Image/PanelImage, landscape. Hidden in the base game; faded in " +
             "with the Yellow intro popup and out with the congratulations panel. A CanvasGroup " +
             "is added at runtime if it has none.")]
    public GameObject panel;
    public GameObject panelPortrait;

    [Tooltip("This tier's holes, landscape, in the ORDER they fill. The count is the tier's " +
             "space count (Mega 6, Grand 5, Major 4, Maxi 3, Minor 2, Mini 2).")]
    public List<JackpotHoleUI> holes = new List<JackpotHoleUI>();

    [Tooltip("Same holes in the portrait layout, same order and count.")]
    public List<JackpotHoleUI> holesPortrait = new List<JackpotHoleUI>();

    [Header("Base-game increment effect")]
    [Tooltip("Star shine played once when a Yellow coin's jackpot coin lands and raises this " +
             "tier's multiplier. Its GameObject is switched on only while it plays.")]
    public ImageAnimation shineEffect;
    public ImageAnimation shineEffectPortrait;
  }

  /// <summary>One hole in a jackpot panel: the blast that plays on a hit, the gold fill after it.</summary>
  [System.Serializable]
  internal class JackpotHoleUI
  {
    [Tooltip("Blast sequence played once when a coin fills this hole. Frames go in its " +
             "textureArray; its GameObject is switched on only while it plays.")]
    public ImageAnimation blast;

    [Tooltip("Gold fill Image, child of the hole. Faded in after the blast, out when the tier " +
             "pays or the round ends.")]
    public Image fill;
  }

  /// <summary>One pig: its two Spine graphics, its two coin destinations, its coin colour.</summary>
  [System.Serializable]
  internal class PigUI
  {
    [Tooltip("12 = Blue, 13 = Yellow, 14 = Red (RichPiggiesSymbols).")]
    public int coinSymbolId;

    public SkeletonGraphic pig;
    public SkeletonGraphic pigPortrait;

    [Tooltip("Where a coin of this colour flies to. Landscape.")]
    public RectTransform target;

    [Tooltip("Where this pig's coin flies to. Portrait.")]
    public RectTransform targetPortrait;

    [Tooltip("This coin's PNG sequence, in order. Looped for the whole flight.")]
    public List<Sprite> coinFrames = new List<Sprite>();

    [Header("Free-spin trigger presentation")]
    [Tooltip("Glow behind this pig's counter. Alpha is yoyo'd 0 <-> glowMaxAlpha for the " +
             "whole free-spin round when this pig contributed to the trigger.")]
    public Image glow;
    public Image glowPortrait;

    [Tooltip("\"Winner\" badge blinked on and off while the trigger popup is up. Stops the " +
             "moment the player dismisses that popup — unlike the glow, which runs all round.")]
    public GameObject winnerImage;
    public GameObject winnerImagePortrait;

    [Tooltip("Cash-drop animation played ONCE on this pig as the trigger presentation starts.")]
    public ImageAnimation cashAnim;
    public ImageAnimation cashAnimPortrait;

    [Tooltip("Every Graphic that should be tinted with darkenColor when this pig did NOT " +
             "contribute to the trigger. Set the list in the Inspector.")]
    public List<Graphic> darkenTargets = new List<Graphic>();
    public List<Graphic> darkenTargetsPortrait = new List<Graphic>();

    [Header("Meter increment effect")]
    [Tooltip("StarAndRingEffect played once when a coin moves this pig's meter. Blue and Red " +
             "only — leave empty on Yellow. Its GameObject is switched on only while it plays.")]
    public ImageAnimation meterEffect;
    public ImageAnimation meterEffectPortrait;
  }

  [Header("References")]
  [SerializeField] private GameManager gameManager;

  [Tooltip("Used to pick the landscape or portrait reference. Resolved in Awake and then " +
           "polled — never FindFirstObjectByType per frame.")]
  [SerializeField] private OrientationChange orientation;

  [Header("Blue Pig — free spins meter")]
  [Tooltip("Rendered through SpriteNumberFormatter, so this text needs a sprite asset from " +
           "Assets/Fonts/CustomTextFonts on it (or on its fallback list) — a plain TMP font " +
           "shows the literal <sprite=N> tags instead of digits.")]
  [SerializeField] private TMP_Text blueMeterText;
  [SerializeField] private TMP_Text blueMeterTextPortrait;

  [Header("Red Pig — wild meter")]
  [Tooltip("Sprite-number text, same wiring requirement as the blue meter.")]
  [SerializeField] private TMP_Text redMeterText;
  [SerializeField] private TMP_Text redMeterTextPortrait;

  [Header("Yellow Pig — jackpot tiers")]
  [Tooltip("Six entries: Mega, Grand, Major, Maxi, Minor, Mini.")]
  [SerializeField] private List<JackpotTierUI> jackpotTiers = new List<JackpotTierUI>();

  [Header("Pigs")]
  [Tooltip("Three entries, one per coin symbol id (12 Blue / 13 Yellow / 14 Red).")]
  [SerializeField] private List<PigUI> pigs = new List<PigUI>();

  [Header("Pig animation")]
  [Tooltip("Spine animation the pigs idle on.")]
  [SerializeField] private string idleAnimation = "Ideal";

  [Tooltip("Spine animation played each time a coin reaches a pig.")]
  [SerializeField] private string jumpAnimation = "Jump";

  [Header("Coin")]
  [Tooltip("Prefab used for the jackpot coins the Yellow Pig sends onward. Needs an Image " +
           "plus an ImageAnimation driving it. Instantiated and destroyed per coin, unlike " +
           "the symbol coins which are cached on their cell.")]
  [SerializeField] private GameObject jackpotCoinPrefab;

  [Tooltip("ImageAnimation speed shared by ALL nine coin sequences. Note ImageAnimation's " +
           "frame delay scales with frame count, so at one speed a longer sequence plays " +
           "SLOWER per frame, not just longer — keep the nine sequences the same length, or " +
           "expect them to spin at visibly different rates.")]
  [SerializeField] private float coinAnimationSpeed = 5f;

  [Header("Free-spin trigger presentation")]
  [Tooltip("Peak alpha of a contributing pig's glow. It yoyos between 0 and this.")]
  [SerializeField] private float glowMaxAlpha = 0.8f;

  [Tooltip("Seconds for ONE leg of the glow yoyo (0 -> max). A full cycle is twice this.")]
  [SerializeField] private float glowFadeDuration = 0.6f;

  [Tooltip("Seconds the winner badge stays visible in each blink.")]
  [SerializeField] private float winnerBlinkOnDuration = 0.4f;

  [Tooltip("Seconds the winner badge stays hidden in each blink.")]
  [SerializeField] private float winnerBlinkOffDuration = 0.3f;

  [Tooltip("Tint applied to a NON-contributing pig's darkenTargets for the round.")]
  [SerializeField] private Color darkenColor = new Color(0.25f, 0.25f, 0.25f, 1f);

  [Tooltip("Tint restored when the round ends. White unless the art is authored pre-tinted.")]
  [SerializeField] private Color normalColor = Color.white;

  [Tooltip("ImageAnimation speed for the per-pig cash-drop animation.")]
  [SerializeField] private float cashAnimationSpeed = 5f;

  [Header("Yellow free-spin jackpot collection")]
  [SerializeField] private float panelFadeInDuration = 0.4f;
  [SerializeField] private float panelFadeOutDuration = 0.4f;

  [Tooltip("ImageAnimation speed for the hole blast.")]
  [SerializeField] private float blastAnimationSpeed = 5f;

  [SerializeField] private float fillFadeInDuration = 0.2f;
  [SerializeField] private float fillFadeOutDuration = 0.35f;

  [Header("Meter effects")]
  [Tooltip("ImageAnimation speed for the Blue / Red StarAndRingEffect.")]
  [SerializeField] private float meterEffectSpeed = 5f;

  [Tooltip("ImageAnimation speed for the jackpot tier star shine.")]
  [SerializeField] private float jackpotShineSpeed = 5f;

  // Live one-shot effect routines, keyed by the landscape animation (or the portrait one when
  // landscape is unwired), so a second coin on the same meter restarts its effect.
  private readonly Dictionary<ImageAnimation, Coroutine> effectRoutines =
      new Dictionary<ImageAnimation, Coroutine>();

  // Holes the client has filled (or reserved for a coin still in flight), per tier.
  private readonly Dictionary<string, int> filledHoles = new Dictionary<string, int>();

  // Latched from the spin result as it arrives; consumed after that spin's win popup closes.
  private readonly List<string> pendingAwardedTiers = new List<string>();
  private Dictionary<string, int> pendingCollections;

  private readonly List<Tween> jackpotTweens = new List<Tween>();
  private readonly List<Coroutine> holeRoutines = new List<Coroutine>();
  private bool jackpotPanelsShown;

  // Live glow tweens and blink coroutines, one per pig, so a round can be torn down without
  // walking the whole pigs list guessing at what is running.
  private readonly Dictionary<int, Tween> glowTweens = new Dictionary<int, Tween>();
  private readonly Dictionary<int, Coroutine> winnerBlinks = new Dictionary<int, Coroutine>();

  // Last meter values the client rendered. Diffed against the next spin's meters to work
  // out which coin moved what. Resynced to the server value at the end of every coin beat,
  // so a dropped or interrupted animation can never leave this drifting.
  private MeterSnapshot cachedMeters = new MeterSnapshot();

  internal MeterSnapshot CachedMeters => cachedMeters;

  private bool IsPortrait =>
      orientation != null && orientation.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;

  private void Awake()
  {
    if (orientation == null) orientation = Object.FindFirstObjectByType<OrientationChange>();
    if (gameManager == null) gameManager = Object.FindFirstObjectByType<GameManager>();

    InitJackpotPanels();
    HideAllEffects();
  }

  private void OnDestroy()
  {
    KillJackpotTweens();
  }

  #region Seeding and bet changes

  /// <summary>
  /// Render every meter from the init payload's live state. Called once, after gameConfig
  /// exists, so the jackpot texts can already be multiplied by the opening bet.
  /// </summary>
  internal void SeedFromInit(ServerFeatures features)
  {
    if (features == null)
    {
      Debug.LogWarning("[PigMeters] Init carried no features block — meters start blank.");
      return;
    }

    cachedMeters = MeterSnapshot.From(features.meters);

    if (features.meters == null)
      Debug.LogWarning("[PigMeters] Init features carried no meters — meters start at zero.");

    ValidateHoleCounts(features.yellowPig?.jackpotLevels);

    RenderAll();
  }

  /// <summary>
  /// Repaint every text from the cached snapshot. The jackpot values depend on the current
  /// bet, so this is also the bet-change entry point (GameManager.SetBetIndex).
  /// </summary>
  internal void RenderAll()
  {
    SetBlueText(cachedMeters.blue);
    SetRedText(cachedMeters.red);
    RefreshJackpotTexts();
  }

  /// <summary>
  /// Recompute all six jackpot payouts against the current bet. A meter holds a MULTIPLIER;
  /// what the player sees is that multiplier times the bet they are on, so every bet change
  /// has to repaint all six.
  /// </summary>
  internal void RefreshJackpotTexts()
  {
    foreach (var tier in jackpotTiers)
    {
      if (tier == null || string.IsNullOrEmpty(tier.tier)) continue;
      WriteJackpotText(tier, cachedMeters.Yellow(tier.tier));
    }
  }

  #endregion

  #region Text writers

  // Both meters are whole counts that cap at 100 (100 free spins / 100 wilds), so they go
  // out with no decimals and no thousands separator — the sheets' '.' and ',' glyphs never
  // come up here.
  internal void SetBlueText(int value)
  {
    cachedMeters.blue = value;
    SpriteNumberFormatter.Apply(blueMeterText, blueMeterTextPortrait, value,
                                maxDecimals: 0, grouping: false);
  }

  internal void SetRedText(int value)
  {
    cachedMeters.red = value;
    SpriteNumberFormatter.Apply(redMeterText, redMeterTextPortrait, value,
                                maxDecimals: 0, grouping: false);
  }

  /// <summary>
  /// Set one tier's displayed payout from its meter MULTIPLIER. Called by the coin beat the
  /// moment a jackpot coin lands.
  /// </summary>
  internal void SetJackpotText(string tier, double multiplier)
  {
    var ui = FindTier(tier);
    if (ui == null)
    {
      Debug.LogError($"[PigMeters] No UI wired for jackpot tier \"{tier}\" — check the " +
                     "jackpotTiers list in the Inspector (keys are case-sensitive).", this);
      return;
    }

    cachedMeters.yellow[tier] = multiplier;
    WriteJackpotText(ui, multiplier);
  }

  private void WriteJackpotText(JackpotTierUI ui, double multiplier)
  {
    // maxDecimals 3 and no grouping is exactly UIManager.FormatAmount's "0.###", so a payout
    // reads the same here as it does when it lands in the win field.
    SpriteNumberFormatter.Apply(ui.valueText, ui.valueTextPortrait, multiplier * CurrentBet(),
                                maxDecimals: 3, grouping: false);
  }

  private double CurrentBet()
  {
    // The meters are per bet option, so the displayed payout must follow the bet the player
    // is actually on rather than the total stake.
    if (gameManager == null) return 0;
    return gameManager.currentBetAmount;
  }

  #endregion

  #region Coin destinations

  /// <summary>
  /// Where a coin of this colour should fly. Resolved AT CALL TIME against the live
  /// orientation, which is what lets CoinFlyer bend an in-flight coin to the other
  /// orientation's pig when the player rotates mid-animation.
  /// </summary>
  internal RectTransform PigTarget(int coinSymbolId)
  {
    var ui = FindPig(coinSymbolId);
    if (ui == null) return null;
    return IsPortrait ? (ui.targetPortrait ?? ui.target) : (ui.target ?? ui.targetPortrait);
  }

  /// <summary>Where a jackpot coin for this tier should fly. Orientation-live, as above.</summary>
  internal RectTransform JackpotTarget(string tier)
  {
    var ui = FindTier(tier);
    if (ui == null) return null;
    return IsPortrait ? (ui.targetPortrait ?? ui.target) : (ui.target ?? ui.targetPortrait);
  }

  /// <summary>The PNG sequence for a Blue / Yellow / Red coin, or null when none is wired.</summary>
  internal List<Sprite> CoinFrames(int coinSymbolId)
  {
    var ui = FindPig(coinSymbolId);
    return ui != null ? ui.coinFrames : null;
  }

  /// <summary>The PNG sequence for a jackpot tier's coin, or null when none is wired.</summary>
  internal List<Sprite> JackpotCoinFrames(string tier)
  {
    var ui = FindTier(tier);
    return ui != null ? ui.coinFrames : null;
  }

  /// <summary>
  /// ImageAnimation speed shared by all nine coin sequences, so every coin in the game
  /// animates at the same rate.
  /// </summary>
  internal float CoinAnimationSpeed => coinAnimationSpeed;

  #endregion

  #region Pig reaction

  /// <summary>
  /// Play the jump on whichever orientation's pig is currently on screen, then drop back to
  /// the idle loop. Calling it again while a jump is running restarts it, so two coins
  /// arriving at the same pig produce two visible jumps.
  /// </summary>
  internal void PlayPigJump(int coinSymbolId)
  {
    var ui = FindPig(coinSymbolId);
    if (ui == null)
    {
      Debug.LogError($"[PigMeters] No pig wired for coin symbol id {coinSymbolId} — check " +
                     "the pigs list in the Inspector.", this);
      return;
    }

    var graphic = IsPortrait ? (ui.pigPortrait ?? ui.pig) : (ui.pig ?? ui.pigPortrait);
    if (graphic == null || graphic.AnimationState == null) return;

    // SetAnimation (not AddAnimation) so a second coin cuts the first jump short and
    // restarts, rather than queueing behind it and arriving late.
    graphic.AnimationState.SetAnimation(0, jumpAnimation, false);
    graphic.AnimationState.AddAnimation(0, idleAnimation, true, 0f);
  }

  #region Meter increment effects

  /// <summary>
  /// Play the StarAndRingEffect on a Blue or Red meter, both orientations. Called by the coin
  /// beat on touchdown when the coin actually moved the meter — never from the text writers,
  /// which also run on resyncs and bet changes that must not flash.
  /// </summary>
  internal void PlayMeterEffect(int coinSymbolId)
  {
    var ui = FindPig(coinSymbolId);
    if (ui == null) return;
    PlayOneShotPair(ui.meterEffect, ui.meterEffectPortrait, meterEffectSpeed);
  }

  /// <summary>Play the star shine on one jackpot tier, both orientations.</summary>
  internal void PlayJackpotShine(string tier)
  {
    var ui = FindTier(tier);
    if (ui == null) return;
    PlayOneShotPair(ui.shineEffect, ui.shineEffectPortrait, jackpotShineSpeed);
  }

  private void PlayOneShotPair(ImageAnimation landscape, ImageAnimation portrait, float speed)
  {
    var key = landscape != null ? landscape : portrait;
    if (key == null) return;

    // A second coin on the same meter restarts the effect rather than letting the first
    // routine switch it off halfway through the second.
    if (effectRoutines.TryGetValue(key, out var running) && running != null)
      StopCoroutine(running);

    effectRoutines[key] = StartCoroutine(OneShotPairRoutine(key, landscape, portrait, speed));
  }

  private IEnumerator OneShotPairRoutine(ImageAnimation key, ImageAnimation landscape,
                                         ImageAnimation portrait, float speed)
  {
    bool landscapeDone = !StartOneShot(landscape, speed);
    bool portraitDone = !StartOneShot(portrait, speed);

    if (landscape != null) landscape.onLoopComplete = _ => landscapeDone = true;
    if (portrait != null) portrait.onLoopComplete = _ => portraitDone = true;

    // Deadline for the same reason as HoleFillRoutine: the off-screen orientation's parent is
    // usually inactive, and ImageAnimation never reports completion there.
    float longest = Mathf.Max(landscape != null ? landscape.GetSequenceDuration() : 0f,
                              portrait != null ? portrait.GetSequenceDuration() : 0f);
    float deadline = Time.time + longest + 0.25f;
    while ((!landscapeDone || !portraitDone) && Time.time < deadline) yield return null;

    EndOneShot(landscape);
    EndOneShot(portrait);
    effectRoutines.Remove(key);
  }

  private static bool StartOneShot(ImageAnimation animation, float speed)
  {
    if (animation == null) return false;
    SetActiveSafe(animation.gameObject, true);
    return CoinAnimator.PlayOnce(animation, speed, null);
  }

  private static void EndOneShot(ImageAnimation animation)
  {
    if (animation == null) return;
    CoinAnimator.Stop(animation);
    SetActiveSafe(animation.gameObject, false);
  }

  /// <summary>Switch every meter / shine effect off, so none shows its first frame at load.</summary>
  private void HideAllEffects()
  {
    foreach (var ui in pigs)
    {
      if (ui == null) continue;
      EndOneShot(ui.meterEffect);
      EndOneShot(ui.meterEffectPortrait);
    }

    foreach (var ui in jackpotTiers)
    {
      if (ui == null) continue;
      EndOneShot(ui.shineEffect);
      EndOneShot(ui.shineEffectPortrait);
    }
  }

  #endregion

  #region Free-spin trigger presentation

  /// <summary>
  /// Dress the pig UI for a free-spin round.
  ///
  /// Contributors glow (an endless yoyo that runs for the whole round), blink their winner
  /// badge (only until the trigger popup is dismissed) and play their cash-drop animation
  /// once. Non-contributors are tinted with <see cref="darkenColor"/> and their Spine
  /// animation freezes, so the screen states plainly which pigs are in play.
  ///
  /// Called once the coin flights have landed, before the win presentation plays on top.
  /// </summary>
  internal void EnterTriggerState(PigFeature contributors)
  {
    foreach (var feature in PigFeatures.All)
    {
      int coinId = PigFeatures.CoinSymbolId(feature);
      var ui = FindPig(coinId);
      if (ui == null) continue;

      if (PigFeatures.Has(contributors, feature))
      {
        StartGlow(coinId, ui);
        StartWinnerBlink(coinId, ui);

        // Once, not looped — the cash drop is a reaction to the trigger, not a state.
        CoinAnimator.PlayOnce(ui.cashAnim, cashAnimationSpeed, null);
        CoinAnimator.PlayOnce(ui.cashAnimPortrait, cashAnimationSpeed, null);
      }
      else
      {
        SetTint(ui, darkenColor);
        FreezePig(ui, frozen: true);
      }
    }
  }

  /// <summary>
  /// Stop the winner badges. The glow deliberately keeps running — it marks the pigs in play
  /// for the whole round, while the badge belongs only to the trigger popup.
  /// </summary>
  internal void StopWinnerLoop()
  {
    foreach (var kv in winnerBlinks)
      if (kv.Value != null) StopCoroutine(kv.Value);

    winnerBlinks.Clear();

    foreach (var ui in pigs)
    {
      if (ui == null) continue;
      SetActiveSafe(ui.winnerImage, false);
      SetActiveSafe(ui.winnerImagePortrait, false);
    }
  }

  /// <summary>
  /// Put the pig UI back the way the base game leaves it: glows off, tints restored, every
  /// pig unfrozen and idling. Idempotent, so the outro and an interrupted round can both
  /// call it.
  ///
  /// The METERS need nothing here — the last free spin's ResyncTo has already taken the
  /// server's post-reset values.
  /// </summary>
  internal void ExitFreeSpins()
  {
    StopWinnerLoop();

    foreach (var kv in glowTweens)
      kv.Value?.Kill();

    glowTweens.Clear();

    foreach (var ui in pigs)
    {
      if (ui == null) continue;

      SetGlowAlpha(ui.glow, 0f);
      SetGlowAlpha(ui.glowPortrait, 0f);
      SetActiveSafe(ui.glow != null ? ui.glow.gameObject : null, false);
      SetActiveSafe(ui.glowPortrait != null ? ui.glowPortrait.gameObject : null, false);

      CoinAnimator.Stop(ui.cashAnim);
      CoinAnimator.Stop(ui.cashAnimPortrait);

      SetTint(ui, normalColor);
      FreezePig(ui, frozen: false);
    }

    ResetPigsToIdle();
  }

  private void StartGlow(int coinId, PigUI ui)
  {
    if (glowTweens.TryGetValue(coinId, out var existing)) existing?.Kill();

    // Both orientations are driven, not just the visible one: the player can rotate at any
    // point in a round that lasts dozens of spins, and the off-screen glow has to already be
    // mid-cycle when they do.
    SetActiveSafe(ui.glow != null ? ui.glow.gameObject : null, true);
    SetActiveSafe(ui.glowPortrait != null ? ui.glowPortrait.gameObject : null, true);

    SetGlowAlpha(ui.glow, 0f);
    SetGlowAlpha(ui.glowPortrait, 0f);

    var sequence = DOTween.Sequence().SetLoops(-1, LoopType.Yoyo);
    bool anyTarget = false;

    if (ui.glow != null)
    {
      sequence.Join(ui.glow.DOFade(glowMaxAlpha, glowFadeDuration).SetEase(Ease.InOutSine));
      anyTarget = true;
    }

    if (ui.glowPortrait != null)
    {
      sequence.Join(ui.glowPortrait.DOFade(glowMaxAlpha, glowFadeDuration).SetEase(Ease.InOutSine));
      anyTarget = true;
    }

    if (!anyTarget)
    {
      sequence.Kill();
      Debug.LogError($"[PigMeters] Pig {coinId} contributed to a free-spin trigger but has no " +
                     "glow wired in either orientation, so nothing will mark it as in play. " +
                     "Assign glow / glowPortrait in the Inspector.", this);
      return;
    }

    glowTweens[coinId] = sequence;
  }

  private void StartWinnerBlink(int coinId, PigUI ui)
  {
    if (winnerBlinks.TryGetValue(coinId, out var existing) && existing != null)
      StopCoroutine(existing);

    winnerBlinks[coinId] = StartCoroutine(BlinkWinner(ui));
  }

  private IEnumerator BlinkWinner(PigUI ui)
  {
    // Runs until StopWinnerLoop stops the coroutine. Both orientations toggle together for
    // the same reason the glow does.
    while (true)
    {
      SetActiveSafe(ui.winnerImage, true);
      SetActiveSafe(ui.winnerImagePortrait, true);
      yield return new WaitForSeconds(winnerBlinkOnDuration);

      SetActiveSafe(ui.winnerImage, false);
      SetActiveSafe(ui.winnerImagePortrait, false);
      yield return new WaitForSeconds(winnerBlinkOffDuration);
    }
  }

  private static void SetGlowAlpha(Image glow, float alpha)
  {
    if (glow == null) return;
    var color = glow.color;
    color.a = alpha;
    glow.color = color;
  }

  private static void SetActiveSafe(GameObject target, bool active)
  {
    if (target != null && target.activeSelf != active) target.SetActive(active);
  }

  /// <summary>Tint both orientations' darken lists. Alpha is left alone — only the RGB dims.</summary>
  private static void SetTint(PigUI ui, Color color)
  {
    TintAll(ui.darkenTargets, color);
    TintAll(ui.darkenTargetsPortrait, color);
  }

  private static void TintAll(List<Graphic> targets, Color color)
  {
    if (targets == null) return;

    foreach (var graphic in targets)
    {
      if (graphic == null) continue;
      graphic.color = new Color(color.r, color.g, color.b, graphic.color.a);
    }
  }

  /// <summary>
  /// Freeze or resume a pig's Spine animation. TimeScale rather than stopping the track, so
  /// the pig holds the pose it was in rather than snapping to the setup pose.
  /// </summary>
  private static void FreezePig(PigUI ui, bool frozen)
  {
    SetTimeScale(ui.pig, frozen ? 0f : 1f);
    SetTimeScale(ui.pigPortrait, frozen ? 0f : 1f);
  }

  private static void SetTimeScale(SkeletonGraphic graphic, float scale)
  {
    if (graphic == null || graphic.AnimationState == null) return;
    graphic.AnimationState.TimeScale = scale;
  }

  #endregion

  /// <summary>Put every pig back on its idle loop. Used when seeding.</summary>
  internal void ResetPigsToIdle()
  {
    foreach (var ui in pigs)
    {
      if (ui == null) continue;
      SetIdle(ui.pig);
      SetIdle(ui.pigPortrait);
    }
  }

  private void SetIdle(SkeletonGraphic graphic)
  {
    if (graphic == null || graphic.AnimationState == null) return;
    graphic.AnimationState.SetAnimation(0, idleAnimation, true);
  }

  #endregion

  #region Jackpot coin spawning

  /// <summary>
  /// Spawn a jackpot coin for <paramref name="tier"/> at <paramref name="origin"/> (the
  /// yellow pig), parented to the flight layer. Unlike a symbol coin this one is created and
  /// destroyed per award, since a single spin can send several to the same tier.
  /// </summary>
  internal RectTransform SpawnJackpotCoin(string tier, RectTransform origin, Transform flightLayer)
  {
    if (jackpotCoinPrefab == null)
    {
      Debug.LogError("[PigMeters] jackpotCoinPrefab is not assigned — the Yellow Pig cannot " +
                     "send a coin on to its jackpot tier.", this);
      return null;
    }

    if (flightLayer == null) return null;

    var instance = Instantiate(jackpotCoinPrefab, flightLayer);
    var rect = instance.transform as RectTransform;
    if (rect == null)
    {
      Debug.LogError("[PigMeters] jackpotCoinPrefab has no RectTransform.", this);
      Destroy(instance);
      return null;
    }

    if (origin != null) rect.position = origin.position;
    rect.localScale = Vector3.one;

    // Spawned on its idle frame. StartJackpotCoinAnimation sets it spinning when it launches.
    var animation = instance.GetComponent<ImageAnimation>();
    var image = instance.GetComponent<UnityEngine.UI.Image>();
    if (!CoinAnimator.ShowIdle(animation, image, JackpotCoinFrames(tier)))
    {
      Debug.LogError($"[PigMeters] Jackpot tier \"{tier}\" has no coin frames, or the prefab " +
                     "has no Image/ImageAnimation — its coin will fly as a blank image.", this);
    }

    return rect;
  }

  /// <summary>Set a spawned jackpot coin spinning for its flight.</summary>
  internal void StartJackpotCoinAnimation(RectTransform coin, string tier)
  {
    if (coin == null) return;
    CoinAnimator.Play(coin.GetComponent<ImageAnimation>(), JackpotCoinFrames(tier), coinAnimationSpeed);
  }

  /// <summary>Stop a jackpot coin spinning and rest it on its idle frame as it lands.</summary>
  internal void StopJackpotCoinAnimation(RectTransform coin)
  {
    if (coin == null) return;
    CoinAnimator.Stop(coin.GetComponent<ImageAnimation>());
  }

  #endregion

  #region Yellow jackpot collection

  /// <summary>
  /// Boot state: every panel hidden, every hole empty. The panels ship inactive in the scene;
  /// a CanvasGroup is added where missing so a panel fades as a whole — its static text and
  /// holes included — rather than just its own Image.
  /// </summary>
  private void InitJackpotPanels()
  {
    foreach (var ui in jackpotTiers)
    {
      if (ui == null) continue;

      EnsureCanvasGroup(ui.panel);
      EnsureCanvasGroup(ui.panelPortrait);
      SetPanelVisible(ui.panel, false);
      SetPanelVisible(ui.panelPortrait, false);

      int landscape = ui.holes != null ? ui.holes.Count : 0;
      int portrait = ui.holesPortrait != null ? ui.holesPortrait.Count : 0;
      if (landscape != portrait)
        Debug.LogError($"[PigMeters] Jackpot tier \"{ui.tier}\" has {landscape} landscape hole(s) " +
                       $"but {portrait} portrait hole(s). Both lists must match, in the same order.", this);
    }

    ResetAllHoles();
    jackpotPanelsShown = false;
  }

  /// <summary>
  /// The server decides when a tier pays; the UI can only show as many hits as it has holes.
  /// A mismatch means a tier either pays before it looks full or looks full and keeps going.
  /// </summary>
  private void ValidateHoleCounts(Dictionary<string, int> jackpotLevels)
  {
    if (jackpotLevels == null) return;

    foreach (var kv in jackpotLevels)
    {
      var ui = FindTier(kv.Key);
      if (ui == null) continue;

      int holes = ui.holes != null ? ui.holes.Count : 0;
      if (holes != kv.Value)
        Debug.LogWarning($"[PigMeters] Server says jackpot \"{kv.Key}\" fills at {kv.Value} " +
                         $"space(s) but the UI has {holes} hole(s). Hits past the last hole " +
                         "will not be shown; check the backend's yellowPig.jackpotLevels.", this);
    }
  }

  /// <summary>
  /// Fade all six panels in, both orientations together, with every hole empty. Called as the
  /// Yellow intro popup opens. Idempotent.
  /// </summary>
  internal void ShowJackpotPanels()
  {
    if (jackpotPanelsShown) return;
    jackpotPanelsShown = true;

    KillJackpotTweens();
    ResetAllHoles();

    foreach (var ui in jackpotTiers)
    {
      if (ui == null) continue;
      FadePanelIn(ui.panel);
      FadePanelIn(ui.panelPortrait);
    }
  }

  internal bool JackpotPanelsShown => jackpotPanelsShown;

  /// <summary>
  /// Fade every panel out together, then switch them off and empty their holes. Called as the
  /// congratulations panel opens; <paramref name="instant"/> for a cancelled round.
  /// </summary>
  internal void HideJackpotPanels(bool instant)
  {
    pendingAwardedTiers.Clear();
    pendingCollections = null;

    if (!jackpotPanelsShown && !instant) return;
    jackpotPanelsShown = false;

    KillJackpotTweens();

    foreach (var ui in jackpotTiers)
    {
      if (ui == null) continue;

      if (instant)
      {
        SetPanelVisible(ui.panel, false);
        SetPanelVisible(ui.panelPortrait, false);
      }
      else
      {
        FadePanelOut(ui.panel);
        FadePanelOut(ui.panelPortrait);
      }
    }

    // Holes are reset at once when instant; otherwise they go with the panel, after its fade,
    // so the gold does not visibly drop out of a panel that is still on screen.
    if (instant) ResetAllHoles();
    else jackpotTweens.Add(DOVirtual.DelayedCall(panelFadeOutDuration, ResetAllHoles));
  }

  /// <summary>
  /// Claim the next hole of <paramref name="tier"/> for a coin about to fly. Reserved at launch
  /// rather than on landing, so coins still in the air fill their holes in launch order.
  /// Returns -1 when the tier has no free hole.
  /// </summary>
  internal int ReserveHole(string tier)
  {
    var ui = FindTier(tier);
    if (ui == null)
    {
      Debug.LogError($"[PigMeters] No UI wired for jackpot tier \"{tier}\" — its coin has " +
                     "nowhere to go. Check the jackpotTiers list (keys are case-sensitive).", this);
      return -1;
    }

    int filled = FilledHoles(tier);
    int capacity = ui.holes != null ? ui.holes.Count : 0;
    if (filled >= capacity)
    {
      Debug.LogWarning($"[PigMeters] Jackpot \"{tier}\" is already showing all {capacity} " +
                       "hole(s) filled; this coin will fly but fill nothing.", this);
      return -1;
    }

    filledHoles[tier] = filled + 1;
    return filled;
  }

  /// <summary>Blast then gold fill on one hole, both orientations together. Fire-and-forget.</summary>
  internal void PlayHoleFill(string tier, int index)
  {
    var ui = FindTier(tier);
    if (ui == null || index < 0) return;

    holeRoutines.Add(StartCoroutine(HoleFillRoutine(HoleAt(ui.holes, index),
                                                     HoleAt(ui.holesPortrait, index))));
  }

  private IEnumerator HoleFillRoutine(JackpotHoleUI landscape, JackpotHoleUI portrait)
  {
    bool landscapeDone = !StartBlast(landscape);
    bool portraitDone = !StartBlast(portrait);

    if (landscape?.blast != null) landscape.blast.onLoopComplete = _ => landscapeDone = true;
    if (portrait?.blast != null) portrait.blast.onLoopComplete = _ => portraitDone = true;

    // Backed by the sequence's own length: the off-screen orientation is usually inactive, and
    // ImageAnimation silently drops its completion callback there.
    float longest = Mathf.Max(BlastDuration(landscape), BlastDuration(portrait));
    float deadline = Time.time + longest + 0.25f;
    while ((!landscapeDone || !portraitDone) && Time.time < deadline) yield return null;

    EndBlast(landscape);
    EndBlast(portrait);

    FadeFill(landscape, 1f, fillFadeInDuration);
    FadeFill(portrait, 1f, fillFadeInDuration);
  }

  private bool StartBlast(JackpotHoleUI hole) => StartOneShot(hole?.blast, blastAnimationSpeed);

  private static void EndBlast(JackpotHoleUI hole) => EndOneShot(hole?.blast);

  private float BlastDuration(JackpotHoleUI hole)
  {
    if (hole?.blast == null) return 0f;
    return hole.blast.GetSequenceDuration();
  }

  /// <summary>
  /// Latch this spin's jackpot awards and the server's collection counts, the moment the
  /// result arrives. Consumed by <see cref="ClearAwardedJackpots"/> after the win popup.
  /// </summary>
  internal void SetPendingJackpotAwards(List<ServerJackpotWin> jackpotWins,
                                        Dictionary<string, int> collections)
  {
    pendingAwardedTiers.Clear();
    pendingCollections = collections;

    if (jackpotWins == null) return;

    foreach (var win in jackpotWins)
    {
      if (win == null) continue;

      string tier = RichPiggiesSymbols.JackpotTierName(win.symbolId) ?? win.symbolName;
      if (string.IsNullOrEmpty(tier) || FindTier(tier) == null)
      {
        Debug.LogError($"[PigMeters] jackpotWin names an unknown tier (id {win.symbolId}, " +
                       $"name \"{win.symbolName}\"). Its holes cannot be cleared.", this);
        continue;
      }

      if (!pendingAwardedTiers.Contains(tier)) pendingAwardedTiers.Add(tier);
    }
  }

  internal bool HasAwardedJackpotsToClear => pendingAwardedTiers.Count > 0 && jackpotPanelsShown;

  /// <summary>
  /// The no-award path of <see cref="ClearAwardedJackpots"/>: nothing to clear, just check the
  /// holes against the server's counts.
  /// </summary>
  internal void SyncPendingJackpotCollections()
  {
    var collections = pendingCollections;
    pendingCollections = null;
    pendingAwardedTiers.Clear();

    if (jackpotPanelsShown) CheckCollections(collections, null);
  }

  /// <summary>Tiers this spin paid, for SlotView to top up before the popup.</summary>
  internal IReadOnlyList<string> PendingAwardedTiers => pendingAwardedTiers;

  /// <summary>
  /// Fill every remaining hole of a tier that paid this spin. Only needed when the client's
  /// count had drifted — normally the coins have already filled it.
  /// </summary>
  internal void ForceFillTier(string tier)
  {
    var ui = FindTier(tier);
    if (ui == null || ui.holes == null) return;

    int filled = FilledHoles(tier);
    if (filled >= ui.holes.Count) return;

    Debug.LogWarning($"[PigMeters] Jackpot \"{tier}\" paid with only {filled}/{ui.holes.Count} " +
                     "hole(s) shown filled. Filling the rest so the award reads correctly.", this);

    for (int i = filled; i < ui.holes.Count; i++)
    {
      FadeFill(HoleAt(ui.holes, i), 1f, fillFadeInDuration);
      FadeFill(HoleAt(ui.holesPortrait, i), 1f, fillFadeInDuration);
    }

    filledHoles[tier] = ui.holes.Count;
  }

  /// <summary>
  /// After the win popup: fade out every gold fill of each tier that paid, together, and
  /// start that tier again from empty. A no-op on a normal spin.
  /// </summary>
  internal IEnumerator ClearAwardedJackpots()
  {
    var collections = pendingCollections;
    pendingCollections = null;

    var awarded = new List<string>(pendingAwardedTiers);
    pendingAwardedTiers.Clear();

    if (awarded.Count > 0 && jackpotPanelsShown)
    {
      foreach (string tier in awarded)
      {
        var ui = FindTier(tier);
        if (ui == null) continue;

        FadeAllFills(ui.holes, 0f, fillFadeOutDuration);
        FadeAllFills(ui.holesPortrait, 0f, fillFadeOutDuration);
        filledHoles[tier] = 0;
      }

      yield return new WaitForSeconds(fillFadeOutDuration);
    }

    if (jackpotPanelsShown) CheckCollections(collections, awarded);
  }

  /// <summary>
  /// Compare the holes on screen with the server's yellowFSCollections and report any
  /// disagreement. Deliberately does NOT move the fills: the holes follow the coins the player
  /// watched land (one coin, one hole; an award empties the tier), and snapping them to a
  /// server count that disagrees would refill a tier that just paid, or show a hit that never
  /// flew. A tier paid this spin is expected to read 0.
  /// </summary>
  private void CheckCollections(Dictionary<string, int> collections, List<string> awarded)
  {
    if (collections == null) return;

    foreach (var ui in jackpotTiers)
    {
      if (ui == null || string.IsNullOrEmpty(ui.tier)) continue;
      if (!collections.TryGetValue(ui.tier, out int server)) continue;

      int shown = FilledHoles(ui.tier);
      if (shown == server) continue;

      bool justPaid = awarded != null && awarded.Contains(ui.tier);
      Debug.LogWarning($"[PigMeters] Jackpot \"{ui.tier}\" shows {shown} hole(s) filled but " +
                       $"yellowFSCollections says {server}" +
                       (justPaid ? " on the spin it paid — the server should have reset it to 0." : ".") +
                       " Keeping the client count; check the backend.", this);
    }
  }

  private int FilledHoles(string tier) =>
      filledHoles.TryGetValue(tier, out int filled) ? filled : 0;

  private void ResetAllHoles()
  {
    filledHoles.Clear();

    foreach (var ui in jackpotTiers)
    {
      if (ui == null) continue;
      ResetHoles(ui.holes);
      ResetHoles(ui.holesPortrait);
    }
  }

  private static void ResetHoles(List<JackpotHoleUI> holes)
  {
    if (holes == null) return;

    foreach (var hole in holes)
    {
      if (hole == null) continue;

      EndBlast(hole);

      if (hole.fill != null)
      {
        hole.fill.DOKill();
        SetGraphicAlpha(hole.fill, 0f);
        SetActiveSafe(hole.fill.gameObject, false);
      }
    }
  }

  private void FadeAllFills(List<JackpotHoleUI> holes, float alpha, float duration)
  {
    if (holes == null) return;
    foreach (var hole in holes) FadeFill(hole, alpha, duration);
  }

  /// <summary>Fade a fill to <paramref name="alpha"/>; switched off once it reaches 0.</summary>
  private void FadeFill(JackpotHoleUI hole, float alpha, float duration)
  {
    var fill = hole?.fill;
    if (fill == null) return;

    fill.DOKill();

    if (alpha > 0f)
    {
      if (!fill.gameObject.activeSelf)
      {
        SetGraphicAlpha(fill, 0f);
        fill.gameObject.SetActive(true);
      }

      jackpotTweens.Add(fill.DOFade(alpha, duration).SetEase(Ease.OutQuad));
    }
    else
    {
      if (!fill.gameObject.activeSelf) return;

      jackpotTweens.Add(fill.DOFade(0f, duration).SetEase(Ease.InQuad)
                            .OnComplete(() => SetActiveSafe(fill.gameObject, false)));
    }
  }

  private static JackpotHoleUI HoleAt(List<JackpotHoleUI> holes, int index) =>
      holes != null && index >= 0 && index < holes.Count ? holes[index] : null;

  private static void EnsureCanvasGroup(GameObject panel)
  {
    if (panel != null && panel.GetComponent<CanvasGroup>() == null)
      panel.AddComponent<CanvasGroup>();
  }

  private static void SetPanelVisible(GameObject panel, bool visible)
  {
    if (panel == null) return;

    var group = panel.GetComponent<CanvasGroup>();
    if (group != null)
    {
      group.DOKill();
      group.alpha = visible ? 1f : 0f;
    }

    SetActiveSafe(panel, visible);
  }

  private void FadePanelIn(GameObject panel)
  {
    if (panel == null) return;

    var group = panel.GetComponent<CanvasGroup>();
    SetActiveSafe(panel, true);
    if (group == null) return;

    group.DOKill();
    group.alpha = 0f;
    jackpotTweens.Add(group.DOFade(1f, panelFadeInDuration).SetEase(Ease.OutQuad));
  }

  private void FadePanelOut(GameObject panel)
  {
    if (panel == null || !panel.activeSelf) return;

    var group = panel.GetComponent<CanvasGroup>();
    if (group == null)
    {
      SetActiveSafe(panel, false);
      return;
    }

    group.DOKill();
    jackpotTweens.Add(group.DOFade(0f, panelFadeOutDuration).SetEase(Ease.InQuad)
                           .OnComplete(() => SetActiveSafe(panel, false)));
  }

  private static void SetGraphicAlpha(Graphic graphic, float alpha)
  {
    var color = graphic.color;
    color.a = alpha;
    graphic.color = color;
  }

  private void KillJackpotTweens()
  {
    // A blast still playing would otherwise fade its fill back in over a panel just reset.
    foreach (var routine in holeRoutines)
      if (routine != null) StopCoroutine(routine);
    holeRoutines.Clear();

    foreach (var tween in jackpotTweens) tween?.Kill();
    jackpotTweens.Clear();
  }

  #endregion

  #region Lookup

  private JackpotTierUI FindTier(string tier)
  {
    if (string.IsNullOrEmpty(tier)) return null;
    foreach (var ui in jackpotTiers)
      if (ui != null && ui.tier == tier) return ui;
    return null;
  }

  private PigUI FindPig(int coinSymbolId)
  {
    foreach (var ui in pigs)
      if (ui != null && ui.coinSymbolId == coinSymbolId) return ui;
    return null;
  }

  #endregion

  /// <summary>
  /// Pin the cache to the server's own values. Called at the end of every coin beat so a
  /// coin that never arrived — an interrupted spin, a missing destination — cannot leave the
  /// client's idea of the meters drifting from the server's.
  /// </summary>
  internal void ResyncTo(ServerMeters meters)
  {
    if (meters == null) return;
    cachedMeters = MeterSnapshot.From(meters);
    RenderAll();
  }
}
