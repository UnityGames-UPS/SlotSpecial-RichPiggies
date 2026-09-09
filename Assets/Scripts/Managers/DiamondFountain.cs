using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Diamonds thrown outward from a single point, in batches, on a loop, until something tells
/// it to stop. Intended to sit BEHIND a big-win popup, so the diamonds read as spraying out
/// from behind the panel on both sides.
///
/// Same public surface as <see cref="CoinTossFountain"/> — <see cref="Play"/> /
/// <see cref="Stop"/> / <see cref="StopImmediate"/> — so a popup can drive either one
/// identically. The differences are the shape of the effect: one spawn point rather than a
/// row along the bottom, a shallow sideways arc that returns to the height it started at
/// rather than a toss that falls off the bottom of the screen, and a left/right sign that
/// ALTERNATES within a batch rather than being rolled per item, so the burst stays balanced
/// across the two sides.
/// </summary>
internal class DiamondFountain : GenericObjectPool<DiamondItem>
{
  [Header("Sprites — one is picked at random per diamond")]
  [SerializeField] private List<Sprite> sprites = new List<Sprite>();

  [Header("Spawn Points — where the burst originates")]
  [Tooltip("Empty RectTransforms behind the popup. One is picked at random per diamond, so " +
           "several points spread the burst over a region rather than a single pixel. " +
           "Anchor each with FRACTIONAL anchors rather than a pixel offset from centre — " +
           "then the same points redistribute correctly when OCController resizes this " +
           "layer from 1920 wide to 1080, and portrait needs no separate set.")]
  [SerializeField] private List<RectTransform> spawnPoints = new List<RectTransform>();

  [Tooltip("Optional. Leave EMPTY to use the list above in portrait too, which is the norm " +
           "for fractionally anchored points. Only fill this in if portrait needs a " +
           "genuinely different arrangement.")]
  [SerializeField] private List<RectTransform> spawnPointsPortraitOverride = new List<RectTransform>();

  [Header("References")]
  [Tooltip("OPTIONAL, and normally left EMPTY. The diamond layer is a child of the popup it " +
           "sits behind, so the popup's own CanvasGroup already fades the diamonds with it — " +
           "a second group here would just fade them twice. Only assign one if the layer is " +
           "moved out from under the popup.")]
  [SerializeField] private CanvasGroup canvasGroup;

  [Tooltip("Resolved from the scene in Start when left empty.")]
  [SerializeField] private OrientationChange orientation;

  [Header("Batching")]
  [Tooltip("Diamonds per batch, inclusive range. The X sign alternates across the batch, so " +
           "an even count splits evenly between the two sides.")]
  [SerializeField] private Vector2Int perBatch = new Vector2Int(2, 4);
  [SerializeField] private float batchInterval = 0.15f;

  [Header("Flight — a value is rolled per diamond from each range")]
  [Tooltip("How far out a diamond travels horizontally, as a MAGNITUDE. The sign is not " +
           "rolled — it alternates within the batch to keep the burst balanced.")]
  [SerializeField] private Vector2 offsetXRange = new Vector2(250f, 800f);

  [Tooltip("How far the diamond lifts at the HALFWAY point of its travel before settling " +
           "back to the height it started at. Rolled signed and directly, so a positive roll " +
           "arcs up and a negative one dips down. Keep it well under the X range or the " +
           "burst reads as a fountain rather than a spray.")]
  [SerializeField] private Vector2 arcHeightRange = new Vector2(-250f, 250f);

  [Tooltip("Portrait's X range. Left at (0,0) the landscape range is used for both. Portrait " +
           "is only 1080 wide against landscape's 1920, so the same throw runs off-screen.")]
  [SerializeField] private Vector2 offsetXRangePortrait = Vector2.zero;

  [SerializeField] private Vector2 durationRange = new Vector2(0.7f, 1.2f);

  [Tooltip("Min and max size. One value is rolled per diamond and held for its whole " +
           "flight — a diamond does not grow or shrink as it travels. Set both ends the " +
           "same for a uniform burst.")]
  [SerializeField] private Vector2 scaleRange = new Vector2(0.7f, 1.2f);

  [Header("Diamond Fade")]
  [Tooltip("A diamond is fully opaque from the moment it launches. This is how long it " +
           "spends fading to nothing at the END of its flight, so it is exactly invisible " +
           "as it arrives — keep it well under durationRange or the fade starts too early " +
           "to read as an arrival. 0 makes the diamond vanish outright.")]
  [SerializeField] private float diamondFadeOutDuration = 0.35f;

  [Header("Fade")]
  [SerializeField] private float fadeInDuration = 0.25f;
  [SerializeField] private float fadeOutDuration = 0.35f;

  private Coroutine spawnLoop;
  private Tween fadeTween;

  internal bool IsPlaying => spawnLoop != null;

  private bool IsPortrait => orientation != null &&
      orientation.CurrentMode == OrientationChange.OrientationMode.MobilePortrait;

  internal override void Start()
  {
    base.Start();

    if (orientation == null) orientation = Object.FindFirstObjectByType<OrientationChange>();
    if (canvasGroup != null) canvasGroup.alpha = 0f;
  }

  /// <summary>Fade the layer in and start throwing diamonds until <see cref="Stop"/>.</summary>
  internal void Play()
  {
    if (spawnLoop != null) return;

    if (!HasUsableSprite())
    {
      Debug.LogError("[DiamondFountain] No sprite is assigned, so there is nothing to throw. " +
                     "Populate the sprites list on this component.", this);
      return;
    }

    var points = ActiveSpawnPoints();
    if (points == null || points.Count == 0)
    {
      Debug.LogError("[DiamondFountain] No spawn points for the current orientation, so the " +
                     "burst has no origin. Populate the spawnPoints list on this component.",
                     this);
      return;
    }

    fadeTween?.Kill();
    if (canvasGroup != null)
      fadeTween = canvasGroup.DOFade(1f, fadeInDuration).SetEase(Ease.OutQuad);

    spawnLoop = StartCoroutine(SpawnLoop());
  }

  /// <summary>
  /// Stop spawning. Diamonds already in the air keep flying — cutting them would read as a
  /// glitch, where letting them run reads as the burst petering out.
  ///
  /// With no <see cref="canvasGroup"/> — the normal case, where the popup fades this layer —
  /// there is nothing left to do: every diamond fades itself out at the end of its own
  /// flight and returns itself to the pool on arrival, so nothing leaks by being left alone
  /// — the burst thins out on its own as the last diamonds land. The caller
  /// still owes a <see cref="StopImmediate"/> before the layer is DEACTIVATED, which is a
  /// different thing from being faded: deactivation kills a flight without its completion
  /// callback, and that would strand the diamond in ItemsInUse.
  /// </summary>
  internal void Stop()
  {
    if (spawnLoop != null)
    {
      StopCoroutine(spawnLoop);
      spawnLoop = null;
    }

    fadeTween?.Kill();

    if (canvasGroup == null) return;

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
    // Cancel before returning: ReturnAllItemsToPool resets each diamond's transform, and a
    // live flight tween would immediately drag it off that reset position again.
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
    if (points == null || points.Count == 0) return;

    int low = Mathf.Max(1, perBatch.x);
    int high = Mathf.Max(low, perBatch.y);
    int count = Random.Range(low, high + 1);

    // Randomise which side the batch STARTS on, then alternate. Always starting left would
    // give an odd-numbered batch a permanent bias toward that side.
    float sign = Random.value < 0.5f ? 1f : -1f;

    for (int i = 0; i < count; i++)
    {
      // A point per diamond, rolled independently — unlike CoinTossFountain, which shuffles
      // so a batch never reuses one. Two diamonds from the same point are fine here because
      // the alternating sign sends consecutive ones in opposite directions, and not capping
      // the count at the number of points means adding a point does not quietly change how
      // dense the burst is.
      var point = PickSpawnPoint(points);
      if (point != null) LaunchDiamond(point, sign);

      sign = -sign;
    }
  }

  private static RectTransform PickSpawnPoint(List<RectTransform> points)
  {
    // Retry rather than filtering into a new list every batch: an empty slot is a wiring
    // error that Play already reported, not a state to allocate around.
    for (int attempt = 0; attempt < points.Count * 2; attempt++)
    {
      var candidate = points[Random.Range(0, points.Count)];
      if (candidate != null) return candidate;
    }

    return null;
  }

  private void LaunchDiamond(RectTransform origin, float signX)
  {
    var sprite = PickSprite();
    if (sprite == null) return;

    var diamond = GetFromPool();
    if (diamond == null) return;

    var rect = diamond.transform as RectTransform;
    if (rect == null)
    {
      ReturnToPool(diamond);
      return;
    }

    // World position, then let the layout resolve the anchored value: the spawn point lives
    // under whichever orientation container is active, which is not this pool's parent.
    rect.position = origin.position;

    Vector2 rangeX = ActiveOffsetXRange();

    var config = new DiamondItem.FlightConfig
    {
      offsetX = Random.Range(rangeX.x, rangeX.y) * signX,
      arcHeight = Random.Range(arcHeightRange.x, arcHeightRange.y),
      duration = Random.Range(durationRange.x, durationRange.y),
      scale = Random.Range(scaleRange.x, scaleRange.y),
      fadeOutDuration = diamondFadeOutDuration
    };

    diamond.Fly(sprite, config, () => ReturnToPool(diamond));
  }

  private Sprite PickSprite()
  {
    if (sprites == null || sprites.Count == 0) return null;

    // Retry rather than filtering into a new list every launch: an empty slot is a wiring
    // error that Play already reported, not a state to allocate around.
    for (int attempt = 0; attempt < sprites.Count * 2; attempt++)
    {
      var candidate = sprites[Random.Range(0, sprites.Count)];
      if (candidate != null) return candidate;
    }

    return null;
  }

  private bool HasUsableSprite()
  {
    if (sprites == null) return false;

    foreach (var sprite in sprites)
      if (sprite != null) return true;

    return false;
  }

  /// <summary>
  /// The burst origins for the orientation showing right now: the portrait override when
  /// there is one and portrait is showing, otherwise the shared list. Note the test is
  /// MobilePortrait specifically — DesktopPortrait rotates the whole UI wrapper instead of
  /// swapping panels, so it keeps using the same points landscape does.
  /// </summary>
  private List<RectTransform> ActiveSpawnPoints()
  {
    if (IsPortrait && spawnPointsPortraitOverride != null &&
        spawnPointsPortraitOverride.Count > 0)
      return spawnPointsPortraitOverride;

    if (spawnPoints != null && spawnPoints.Count > 0) return spawnPoints;

    // Nothing in the shared list: fall back to the override so a fountain wired only for
    // portrait still throws diamonds rather than erroring on every batch.
    return spawnPointsPortraitOverride;
  }

  /// <summary>
  /// Horizontal range for the orientation showing right now. An unset portrait range — both
  /// ends zero — means "same as landscape", so a burst that reads fine in both needs only
  /// the one range filled in.
  /// </summary>
  private Vector2 ActiveOffsetXRange()
  {
    if (IsPortrait && (offsetXRangePortrait.x > 0f || offsetXRangePortrait.y > 0f))
      return offsetXRangePortrait;

    return offsetXRange;
  }

  private void OnDestroy()
  {
    fadeTween?.Kill();

    if (ItemsInUse == null) return;
    for (int i = ItemsInUse.Count - 1; i >= 0; i--)
      if (ItemsInUse[i] != null) ItemsInUse[i].Cancel();
  }
}
