using System.Collections.Generic;
using UnityEngine;
using TMPro;
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

    public TMP_Text valueText;
    public TMP_Text valueTextPortrait;

    [Tooltip("Where a jackpot coin for this tier flies to. Landscape.")]
    public RectTransform target;

    [Tooltip("Where a jackpot coin for this tier flies to. Portrait.")]
    public RectTransform targetPortrait;

    [Tooltip("Placeholder tint for this tier's coin until the real art lands.")]
    public Color coinColor = Color.white;
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

    [Tooltip("Where a coin of this colour flies to. Portrait.")]
    public RectTransform targetPortrait;

    [Tooltip("Placeholder tint for this coin until the real art lands.")]
    public Color coinColor = Color.white;
  }

  [Header("References")]
  [SerializeField] private GameManager gameManager;

  [Tooltip("Used to pick the landscape or portrait reference. Resolved in Awake and then " +
           "polled — never FindFirstObjectByType per frame.")]
  [SerializeField] private OrientationChange orientation;

  [Header("Blue Pig — free spins meter")]
  [SerializeField] private TMP_Text blueMeterText;
  [SerializeField] private TMP_Text blueMeterTextPortrait;

  [Header("Red Pig — wild meter")]
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
  [Tooltip("Prefab used for the jackpot coins the Yellow Pig sends onward. Instantiated and " +
           "destroyed per coin, unlike the symbol coins which are cached on their cell.")]
  [SerializeField] private GameObject jackpotCoinPrefab;

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

  internal void SetBlueText(int value)
  {
    cachedMeters.blue = value;
    string text = value.ToString();
    if (blueMeterText) blueMeterText.text = text;
    if (blueMeterTextPortrait) blueMeterTextPortrait.text = text;
  }

  internal void SetRedText(int value)
  {
    cachedMeters.red = value;
    string text = value.ToString();
    if (redMeterText) redMeterText.text = text;
    if (redMeterTextPortrait) redMeterTextPortrait.text = text;
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
    double bet = CurrentBet();
    string text = FormatAmount(multiplier * bet);

    if (ui.valueText) ui.valueText.text = text;
    if (ui.valueTextPortrait) ui.valueTextPortrait.text = text;
  }

  private double CurrentBet()
  {
    // The meters are per bet option, so the displayed payout must follow the bet the player
    // is actually on rather than the total stake.
    if (gameManager == null) return 0;
    return gameManager.currentBetAmount;
  }

  // Matches UIManager.FormatAmount so meter payouts read the same as every other figure
  // in the HUD.
  private static string FormatAmount(double amount) => amount.ToString("0.###");

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

  internal Color CoinColor(int coinSymbolId)
  {
    var ui = FindPig(coinSymbolId);
    return ui != null ? ui.coinColor : Color.white;
  }

  internal Color JackpotCoinColor(string tier)
  {
    var ui = FindTier(tier);
    return ui != null ? ui.coinColor : Color.white;
  }

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

    var image = instance.GetComponent<UnityEngine.UI.Image>();
    if (image != null) image.color = JackpotCoinColor(tier);

    return rect;
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
