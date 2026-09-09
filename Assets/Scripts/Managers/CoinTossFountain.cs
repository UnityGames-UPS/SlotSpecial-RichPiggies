using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Coins tossed up from points along the bottom of the screen, in batches, on a loop, until
/// something tells it to stop.
///
/// Deliberately knows nothing about wins or popups — it is driven purely by
/// <see cref="Play"/> / <see cref="Stop"/>, so any popup that wants a coin shower can own one
/// of these. The fade lives on this component's own CanvasGroup rather than on the caller's,
/// so the coins can be layered above whatever they decorate and still come and go with it.
/// </summary>
internal class CoinTossFountain : GenericObjectPool<CoinTossItem>
{
  /// <summary>One coin colour. Frames are the turnaround sequence, played on a loop.</summary>
  [System.Serializable]
  internal class CoinVariant
  {
    public string name = "Coin";
    public List<Sprite> frames = new List<Sprite>();
    public float animationSpeed = 5f;
  }

  [Header("Coin Variants — one per colour")]
  [SerializeField] private List<CoinVariant> variants = new List<CoinVariant>();

  [Header("Spawn Points — empty RectTransforms along the bottom bar")]
  [Tooltip("The main set. Anchor each point to the BOTTOM with a FRACTIONAL x anchor " +
           "(anchorMin.x = anchorMax.x = 0.15, 0.35, ...) rather than a pixel offset from " +
           "centre — then the same points redistribute correctly when OCController resizes " +
           "this layer from 1920 wide to 1080, and portrait needs no separate set.")]
  [SerializeField] private List<RectTransform> spawnPoints = new List<RectTransform>();

  [Tooltip("Optional. Leave EMPTY to use the list above in portrait too, which is the norm " +
           "for fractionally anchored points. Only fill this in if portrait needs a " +
           "genuinely different arrangement.")]
  [SerializeField] private List<RectTransform> spawnPointsPortraitOverride = new List<RectTransform>();

  [Header("References")]
  [Tooltip("This layer's own group. Every coin fades with it. Left null, the fountain still " +
           "runs but pops in and out.")]
  [SerializeField] private CanvasGroup canvasGroup;

  [Tooltip("Resolved from the scene in Start when left empty.")]
  [SerializeField] private OrientationChange orientation;

  [Header("Batching")]
  [Tooltip("Coins per batch, inclusive range. Capped at the number of spawn points so a " +
           "batch never launches two coins from the same place.")]
  [SerializeField] private Vector2Int coinsPerBatch = new Vector2Int(2, 4);
  [SerializeField] private float batchInterval = 0.2f;

  [Header("Flight — a value is rolled per coin from each range")]
  [Tooltip("How high a coin rises above its spawn point, in local units. Split per " +
           "orientation because the layer is 1080 tall in landscape and 1920 in portrait — " +
           "one range cannot read the same in both.")]
  [SerializeField] private Vector2 riseRangeLandscape = new Vector2(400f, 700f);

  [Tooltip("Portrait's rise range. Left at (0,0) the landscape range is used for both.")]
  [SerializeField] private Vector2 riseRangePortrait = new Vector2(700f, 1200f);
  [Tooltip("Horizontal drift magnitude. The sign is randomised, so a coin may arc either way.")]
  [SerializeField] private Vector2 driftXRange = new Vector2(50f, 200f);
  [SerializeField] private Vector2 upDurationRange = new Vector2(0.45f, 0.7f);
  [SerializeField] private Vector2 downDurationRange = new Vector2(0.5f, 0.8f);
  [Tooltip("Scale at the apex — a coin's largest. The two factors below are fractions of it.")]
  [SerializeField] private Vector2 scaleRange = new Vector2(0.7f, 1.2f);

  [Tooltip("Fraction of the apex scale a coin launches at. Below 1 it swells on the way up.")]
  [SerializeField] private float scaleFromFactor = 0.6f;

  [Tooltip("Fraction of the apex scale a coin lands at. Below 1 it shrinks on the way down.")]
  [SerializeField] private float scaleToFactor = 0.6f;

  [Header("Coin Fade")]
  [Tooltip("Fade from transparent over this long as the coin launches. Clamped to the rise " +
           "duration. 0 makes the coin appear fully opaque.")]
  [SerializeField] private float coinFadeInDuration = 0.25f;

  [Tooltip("Fade to transparent over this long, ending exactly as the coin reaches its " +
           "fall distance. Clamped to the fall duration. 0 keeps it opaque all the way down.")]
  [SerializeField] private float coinFadeOutDuration = 0.4f;

  [Tooltip("How far BELOW its spawn point a coin falls before it is recycled. Relative, not " +
           "an absolute Y: this layer's height flips between 1080 and 1920 with the " +
           "orientation, so a fixed Y that cleared the bottom edge in one would sit well " +
           "inside the screen in the other. Only needs to exceed the spawn points' height " +
           "above the bottom edge.")]
  [SerializeField] private float fallDistance = 500f;

  [Header("Fade")]
  [SerializeField] private float fadeInDuration = 0.25f;
  [SerializeField] private float fadeOutDuration = 0.35f;

  private Coroutine spawnLoop;
  private Tween fadeTween;
  private readonly List<RectTransform> batchPoints = new List<RectTransform>();

  internal bool IsPlaying => spawnLoop != null;

  private bool IsPortrait => orientation != null &&
      orientation.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;

  internal override void Start()
  {
    base.Start();

    if (orientation == null) orientation = Object.FindFirstObjectByType<OrientationChange>();
    if (canvasGroup != null) canvasGroup.alpha = 0f;
  }

  /// <summary>Fade the layer in and start throwing coins until <see cref="Stop"/>.</summary>
  internal void Play()
  {
    if (spawnLoop != null) return;

    if (!HasUsableVariant())
    {
      Debug.LogError("[CoinTossFountain] No variant has any frames, so there is nothing to " +
                     "throw. Populate the variants list on this component.", this);
      return;
    }

    fadeTween?.Kill();
    if (canvasGroup != null)
      fadeTween = canvasGroup.DOFade(1f, fadeInDuration).SetEase(Ease.OutQuad);

    spawnLoop = StartCoroutine(SpawnLoop());
  }

  /// <summary>
  /// Stop spawning and fade what is left out. Coins already in the air keep flying through
  /// the fade — cutting them would read as a glitch, where fading reads as them receding.
  /// </summary>
  internal void Stop()
  {
    if (spawnLoop != null)
    {
      StopCoroutine(spawnLoop);
      spawnLoop = null;
    }

    fadeTween?.Kill();

    if (canvasGroup == null)
    {
      RecycleEverything();
      return;
    }

    fadeTween = canvasGroup.DOFade(0f, fadeOutDuration)
                           .SetEase(Ease.InQuad)
                           .OnComplete(RecycleEverything);
  }

  /// <summary>Everything gone this frame, no fade. For a spin cutting the presentation short.</summary>
  internal void StopImmediate()
  {
    if (spawnLoop != null)
    {
      StopCoroutine(spawnLoop);
      spawnLoop = null;
    }

    fadeTween?.Kill();
    fadeTween = null;
    if (canvasGroup != null) canvasGroup.alpha = 0f;

    RecycleEverything();
  }

  private void RecycleEverything()
  {
    // Cancel before returning: ReturnAllItemsToPool resets each coin's transform, and a live
    // flight tween would immediately drag it off that reset position again.
    for (int i = ItemsInUse.Count - 1; i >= 0; i--)
      if (ItemsInUse[i] != null) ItemsInUse[i].Cancel();

    ReturnAllItemsToPool();
  }

  private IEnumerator SpawnLoop()
  {
    while (true)
    {
      SpawnBatch();
      yield return new WaitForSeconds(batchInterval);
    }
  }

  private void SpawnBatch()
  {
    var points = ActiveSpawnPoints();
    if (points == null || points.Count == 0)
    {
      Debug.LogError("[CoinTossFountain] No spawn points for the current orientation. " +
                     "Populate the spawnPoints list on this component.", this);
      return;
    }

    // Shuffle a working copy and take from the front, so one batch never doubles up on a
    // spawn point — two coins launching from the same pixel reads as a rendering bug.
    batchPoints.Clear();
    batchPoints.AddRange(points);
    for (int i = batchPoints.Count - 1; i > 0; i--)
    {
      int j = Random.Range(0, i + 1);
      (batchPoints[i], batchPoints[j]) = (batchPoints[j], batchPoints[i]);
    }

    int low = Mathf.Max(1, coinsPerBatch.x);
    int high = Mathf.Max(low, coinsPerBatch.y);
    int count = Mathf.Min(Random.Range(low, high + 1), batchPoints.Count);

    for (int i = 0; i < count; i++)
    {
      var point = batchPoints[i];
      if (point != null) LaunchCoin(point);
    }
  }

  private void LaunchCoin(RectTransform spawnPoint)
  {
    var variant = PickVariant();
    if (variant == null) return;

    var coin = GetFromPool();
    if (coin == null) return;

    var rect = coin.transform as RectTransform;
    if (rect == null)
    {
      ReturnToPool(coin);
      return;
    }

    // World position, then let the layout resolve the anchored value: the spawn points live
    // under whichever orientation container is active, which is not this pool's parent.
    rect.position = spawnPoint.position;

    Vector2 rise = ActiveRiseRange();

    var config = new CoinTossItem.TossConfig
    {
      rise = Random.Range(rise.x, rise.y),
      driftX = Random.Range(driftXRange.x, driftXRange.y) * (Random.value < 0.5f ? -1f : 1f),
      fallDistance = fallDistance,
      upDuration = Random.Range(upDurationRange.x, upDurationRange.y),
      downDuration = Random.Range(downDurationRange.x, downDurationRange.y),
      scale = Random.Range(scaleRange.x, scaleRange.y),
      scaleFrom = scaleFromFactor,
      scaleTo = scaleToFactor,
      fadeInDuration = coinFadeInDuration,
      fadeOutDuration = coinFadeOutDuration
    };

    coin.Toss(variant.frames, variant.animationSpeed, config, () => ReturnToPool(coin));
  }

  private CoinVariant PickVariant()
  {
    if (variants == null || variants.Count == 0) return null;

    // Retry rather than filtering into a new list every launch: a variant with no frames is
    // a wiring error that Play already reported, not a state to allocate around.
    for (int attempt = 0; attempt < variants.Count * 2; attempt++)
    {
      var candidate = variants[Random.Range(0, variants.Count)];
      if (candidate != null && candidate.frames != null && candidate.frames.Count > 0)
        return candidate;
    }

    return null;
  }

  private bool HasUsableVariant()
  {
    if (variants == null) return false;

    foreach (var variant in variants)
      if (variant != null && variant.frames != null && variant.frames.Count > 0) return true;

    return false;
  }

  /// <summary>
  /// The spawn list to use right now: the portrait override when there is one and portrait is
  /// showing, otherwise the shared list. Note the test is MobilePortrait specifically —
  /// DesktopPortrait rotates the whole UI wrapper instead of swapping panels, so it keeps
  /// using the same points landscape does.
  /// </summary>
  /// <summary>
  /// Rise range for the orientation showing right now. An unset portrait range — both ends
  /// zero — means "same as landscape", so a fountain that reads fine in both needs only the
  /// one range filled in.
  /// </summary>
  private Vector2 ActiveRiseRange()
  {
    if (IsPortrait && (riseRangePortrait.x > 0f || riseRangePortrait.y > 0f))
      return riseRangePortrait;

    return riseRangeLandscape;
  }

  private List<RectTransform> ActiveSpawnPoints()
  {
    if (IsPortrait && spawnPointsPortraitOverride != null &&
        spawnPointsPortraitOverride.Count > 0)
      return spawnPointsPortraitOverride;

    if (spawnPoints != null && spawnPoints.Count > 0) return spawnPoints;

    // Nothing in the shared list: fall back to the override so a fountain wired only for
    // portrait still throws coins rather than erroring on every batch.
    return spawnPointsPortraitOverride;
  }

  private void OnDestroy()
  {
    fadeTween?.Kill();

    if (ItemsInUse == null) return;
    for (int i = ItemsInUse.Count - 1; i >= 0; i--)
      if (ItemsInUse[i] != null) ItemsInUse[i].Cancel();
  }
}
