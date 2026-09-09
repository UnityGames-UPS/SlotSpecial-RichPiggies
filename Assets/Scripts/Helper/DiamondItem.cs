using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One diamond of a <see cref="DiamondFountain"/> batch: launched from the burst's single
/// spawn point out to one side along a shallow arc, at a fixed size, fading to nothing
/// exactly as it arrives.
///
/// Same contract as <see cref="CoinTossItem"/> — the item owns none of its own randomness,
/// every value is rolled by the fountain and handed in — but the arc peaks halfway along and
/// returns to the height it started at rather than falling off the bottom of the screen, the
/// sprite is static rather than an <see cref="ImageAnimation"/> sequence, and there is no
/// rotation or scale animation: the diamond holds one size for the whole flight.
/// </summary>
internal class DiamondItem : MonoBehaviour
{
  [SerializeField] private RectTransform rect;

  // Faded directly on the Image rather than through a CanvasGroup: one less component on a
  // pooled object, and the diamond has nothing else to fade.
  [SerializeField] private Image image;

  private Sequence flight;
  private Action onDone;

  /// <summary>Everything the fountain rolled for this one diamond.</summary>
  internal struct FlightConfig
  {
    internal float offsetX;     // signed travel; the sign is the side of the burst it takes
    internal float arcHeight;   // signed peak, reached HALFWAY along the X travel
    internal float duration;

    internal float scale;       // fixed for the whole flight

    internal float fadeOutDuration;  // ENDS as the diamond arrives; clamped to the flight
  }

  private void Awake() => ResolveReferences();

  private void Reset() => ResolveReferences();

  private void ResolveReferences()
  {
    if (rect == null) rect = transform as RectTransform;
    if (image == null) image = GetComponent<Image>();
  }

  /// <summary>
  /// Launch the diamond. <paramref name="onFinished"/> fires once it reaches its
  /// destination; the fountain returns it to its pool there.
  /// </summary>
  internal void Fly(Sprite sprite, FlightConfig config, Action onFinished)
  {
    ResolveReferences();

    if (rect == null)
    {
      onFinished?.Invoke();
      return;
    }

    // A diamond from the pool may still be mid-flight if something recycled it early.
    flight?.Kill();
    rect.DOKill();
    if (image != null) image.DOKill();

    onDone = onFinished;

    if (image != null && sprite != null) image.sprite = sprite;

    // Pooled diamonds come back with whatever scale and alpha they died on, and ReturnToPool
    // only resets the transform — so alpha has to be set explicitly. The scale is fixed for
    // the whole flight; the diamond does not swell or shrink as it travels.
    rect.localScale = Vector3.one * config.scale;

    // Fully opaque from the first frame — the diamond is already there when the burst
    // throws it, and only disappears at the far end.
    SetAlpha(1f);

    Vector2 start = rect.anchoredPosition;
    float endX = start.x + config.offsetX;
    float half = config.duration * 0.5f;

    // X is one continuous tween across the whole flight; Y is two halves that peak at the
    // midpoint and come back to exactly where they started. Joining them is what bends the
    // path into an arc — the diamond lifts as it travels out and settles again on arrival,
    // rather than sliding along a straight diagonal.
    //
    // X is LINEAR on purpose, and that is what puts the peak at half the DISTANCE rather
    // than merely half the time: under an easing curve the midpoint of the duration is not
    // the midpoint of the travel (OutQuad would already be three quarters of the way out).
    // The curvature all comes from Y — OutQuad decelerating into the peak, InQuad
    // accelerating out of it, so the turn is a curve and not a corner.
    flight = DOTween.Sequence();
    flight.Append(rect.DOAnchorPosY(start.y + config.arcHeight, half).SetEase(Ease.OutQuad));
    flight.Append(rect.DOAnchorPosY(start.y, half).SetEase(Ease.InQuad));
    flight.Insert(0f, rect.DOAnchorPosX(endX, config.duration).SetEase(Ease.Linear));

    // Anchored to the END of the flight rather than starting from a fixed point: that way
    // fadeOutDuration can be much shorter than the flight and the diamond still lands
    // exactly invisible, instead of vanishing early and travelling the rest of the way as
    // nothing. Clamped so a fade longer than the flight simply spans all of it.
    if (image != null && config.fadeOutDuration > 0f)
    {
      float fadeOut = Mathf.Min(config.fadeOutDuration, config.duration);
      flight.Insert(config.duration - fadeOut,
                    image.DOFade(0f, fadeOut).SetEase(Ease.InQuad));
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
  /// recycling every diamond itself, so firing the per-item callback would double-return it.
  /// </summary>
  internal void Cancel()
  {
    flight?.Kill();
    flight = null;
    onDone = null;

    if (rect != null) rect.DOKill();
    if (image != null) image.DOKill();
  }

  private void Finish()
  {
    flight = null;

    var callback = onDone;
    onDone = null;
    callback?.Invoke();
  }

  private void OnDisable()
  {
    // Pooling deactivates the diamond; a live tween on a disabled RectTransform would keep
    // running and move it again the next time it is fished out of the pool.
    flight?.Kill();
    flight = null;
    onDone = null;
    if (image != null) image.DOKill();
  }
}
