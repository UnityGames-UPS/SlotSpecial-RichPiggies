using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Flies a coin from wherever it currently is to a destination that is allowed to MOVE
/// while it is in the air.
///
/// This is deliberately not a DOTween DOMove. Two things relocate a destination mid-flight:
///   - the player rotating the device, which swaps to the other orientation's meter UI
///     (see PigMeterController.PigTarget / JackpotTarget, resolved per frame), and
///   - OCController's 0.2s position/scale tween on the slot area, which drags the coin's
///     own start point along with it.
/// A tween baked against a fixed world point would fly to a stale location in both cases.
///
/// The step is expressed as "the fraction of the REMAINING distance to consume this frame
/// so that eased progress advances from e(t) to e(t + dt)". Because it is always relative
/// to where the coin is right now, the destination can teleport across the screen and the
/// coin simply curves toward the new one — no snap, and it still lands on time.
/// </summary>
internal static class CoinFlyer
{
  /// <summary>
  /// Move <paramref name="coin"/> onto <paramref name="target"/> over
  /// <paramref name="duration"/>, then shrink it to nothing.
  /// </summary>
  /// <param name="target">
  /// Re-invoked every frame. Returning null holds the coin at its current position rather
  /// than throwing, so a destination being toggled off mid-flight degrades quietly.
  /// </param>
  internal static IEnumerator Fly(RectTransform coin, Func<RectTransform> target,
                                  float duration, Ease ease,
                                  float shrinkDuration, Action onArrive)
  {
    if (coin == null || target == null)
    {
      onArrive?.Invoke();
      yield break;
    }

    if (duration > 0f)
    {
      float elapsed = 0f;

      while (elapsed < duration)
      {
        float dt = Time.deltaTime;
        float t = elapsed / duration;
        float tNext = Mathf.Min((elapsed + dt) / duration, 1f);

        float eased = DOVirtual.EasedValue(0f, 1f, t, ease);
        float easedNext = DOVirtual.EasedValue(0f, 1f, tNext, ease);

        // Guard the denominator: eased hits 1 on the final frame, and an overshooting ease
        // (OutBack and friends) can push it past 1 mid-flight.
        float remainingFraction = Mathf.Max(1f - eased, 0.0001f);
        float step = Mathf.Clamp01((easedNext - eased) / remainingFraction);

        var destination = target();
        if (destination != null)
          coin.position = Vector3.Lerp(coin.position, destination.position, step);

        elapsed += dt;
        yield return null;
      }

      var final = target();
      if (final != null) coin.position = final.position;
    }

    if (shrinkDuration > 0f)
    {
      coin.DOKill();
      yield return coin.DOScale(Vector3.zero, shrinkDuration).SetEase(Ease.InBack).WaitForCompletion();
    }
    else
    {
      coin.localScale = Vector3.zero;
    }

    onArrive?.Invoke();
  }
}
