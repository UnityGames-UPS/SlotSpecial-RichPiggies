using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One coin of a <see cref="CoinTossFountain"/> batch: launched upward from a spawn point,
/// spinning, then falling back past the bottom of the screen.
///
/// The coin owns none of its own randomness. Rise, drift, durations and scale are rolled by
/// the fountain and handed in, so a batch can be tuned in one place and every coin in it
/// stays consistent with the fountain's ranges.
/// </summary>
internal class CoinTossItem : MonoBehaviour
{
  [SerializeField] private RectTransform rect;
  [SerializeField] private ImageAnimation imageAnimation;

  // Faded directly on the Image rather than through a CanvasGroup: one less component on a
  // pooled object, and the coin has nothing else to fade.
  [SerializeField] private Image image;

  private Sequence flight;
  private Action onDone;

  /// <summary>Everything the fountain rolled for this one coin.</summary>
  internal struct TossConfig
  {
    internal float rise;          // local units above the spawn point at the apex
    internal float driftX;        // signed horizontal travel across the whole flight
    internal float fallDistance;  // how far BELOW the spawn point the coin drops
    internal float upDuration;
    internal float downDuration;

    internal float scale;          // scale at the apex, the coin's largest
    internal float scaleFrom;      // fraction of `scale` it launches at
    internal float scaleTo;        // fraction of `scale` it lands at

    internal float fadeInDuration;   // from launch
    internal float fadeOutDuration;  // ending as the coin reaches fallDistance
  }

  private void Awake() => ResolveReferences();

  private void Reset() => ResolveReferences();

  private void ResolveReferences()
  {
    if (rect == null) rect = transform as RectTransform;
    if (imageAnimation == null) imageAnimation = GetComponent<ImageAnimation>();
    if (image == null)
      image = imageAnimation != null && imageAnimation.rendererDelegate != null
          ? imageAnimation.rendererDelegate
          : GetComponent<Image>();
  }

  /// <summary>
  /// Launch the coin. <paramref name="onFinished"/> fires once the fall completes; the
  /// fountain returns the coin to its pool there.
  /// </summary>
  internal void Toss(List<Sprite> frames, float animationSpeed, TossConfig config,
                     Action onFinished)
  {
    ResolveReferences();

    if (rect == null)
    {
      onFinished?.Invoke();
      return;
    }

    // A coin from the pool may still be mid-flight if something recycled it early.
    flight?.Kill();
    rect.DOKill();
    if (image != null) image.DOKill();

    onDone = onFinished;

    // Pooled coins come back with whatever scale and alpha they died on, and ReturnToPool
    // only resets the transform — so both ends of the fade have to be set explicitly here.
    float scaleFrom = config.scale * config.scaleFrom;
    float scaleTo = config.scale * config.scaleTo;
    rect.localScale = Vector3.one * scaleFrom;
    SetAlpha(config.fadeInDuration > 0f ? 0f : 1f);

    // CoinAnimator, not ImageAnimation directly: it clears the pending Invoke that a prefab
    // with StartOnAwake / StartonEnable queues, which StopAnimation on its own does not.
    CoinAnimator.Play(imageAnimation, frames, animationSpeed);

    // Both ends are measured from where the coin actually spawned, so nothing here depends
    // on the layer's own height — which changes with the orientation.
    Vector2 start = rect.anchoredPosition;
    float apexY = start.y + config.rise;
    float endY = start.y - config.fallDistance;
    float total = config.upDuration + config.downDuration;

    // Vertical is two halves so the coin decelerates into the apex and accelerates out of
    // it — a toss, not a linear round trip. The horizontal drift is one continuous linear
    // tween joined across both, which is what bends the path into an arc.
    flight = DOTween.Sequence();
    flight.Append(rect.DOAnchorPosY(apexY, config.upDuration).SetEase(Ease.OutQuad));
    flight.Append(rect.DOAnchorPosY(endY, config.downDuration).SetEase(Ease.InQuad));
    flight.Insert(0f, rect.DOAnchorPosX(start.x + config.driftX, total).SetEase(Ease.Linear));

    // Scale swells to its peak on the way up and shrinks on the way down, so the coin reads
    // as coming toward the viewer and receding rather than sliding on a flat plane.
    flight.Insert(0f, rect.DOScale(config.scale, config.upDuration).SetEase(Ease.OutQuad));
    flight.Insert(config.upDuration,
                  rect.DOScale(scaleTo, config.downDuration).SetEase(Ease.InQuad));

    if (image != null)
    {
      if (config.fadeInDuration > 0f)
        flight.Insert(0f, image.DOFade(1f, Mathf.Min(config.fadeInDuration, config.upDuration))
                               .SetEase(Ease.OutQuad));

      // Anchored to the END of the flight, not the start of the fall: that way fadeOut can be
      // shorter than downDuration and the coin still lands fully transparent, instead of
      // vanishing early and leaving the rest of the fall invisible.
      if (config.fadeOutDuration > 0f)
      {
        float fadeOut = Mathf.Min(config.fadeOutDuration, config.downDuration);
        flight.Insert(total - fadeOut, image.DOFade(0f, fadeOut).SetEase(Ease.InQuad));
      }
    }

    flight.OnComplete(Finish);
  }

  private void SetAlpha(float alpha)
  {
    if (image == null) return;
    Color c = image.color;
    c.a = alpha;
    image.color = c;
  }

  /// <summary>
  /// Stop dead without reporting completion. For a fountain being torn down — the caller is
  /// recycling every coin itself, so firing the per-coin callback would double-return it.
  /// </summary>
  internal void Cancel()
  {
    flight?.Kill();
    flight = null;
    onDone = null;

    if (rect != null) rect.DOKill();
    if (image != null) image.DOKill();
    CoinAnimator.Stop(imageAnimation);
  }

  private void Finish()
  {
    flight = null;
    CoinAnimator.Stop(imageAnimation);

    var callback = onDone;
    onDone = null;
    callback?.Invoke();
  }

  private void OnDisable()
  {
    // Pooling deactivates the coin; a live tween on a disabled RectTransform would keep
    // running and move it again the next time it is fished out of the pool.
    flight?.Kill();
    flight = null;
    onDone = null;
    if (image != null) image.DOKill();
  }
}
