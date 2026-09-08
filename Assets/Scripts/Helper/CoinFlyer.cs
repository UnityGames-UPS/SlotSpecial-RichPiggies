using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Drives a coin's PNG sequence.
///
/// A coin only animates while it is MOVING. Frame 0 of every sequence is the coin's idle
/// pose, so a revealed coin sits on that frame until it launches, spins for the flight, and
/// parks back on frame 0 the instant it lands.
///
/// Shared by both kinds of coin — the cached child on a SlotIcon and the jackpot coin
/// instantiated per award — because ImageAnimation needs the same careful handling either
/// way and getting it wrong fails silently. See <see cref="Play"/>.
/// </summary>
internal static class CoinAnimator
{
  /// <summary>
  /// Park the coin on frame 0, its idle pose, with nothing playing. This is the state a coin
  /// is revealed in and the state it lands in.
  /// </summary>
  internal static bool ShowIdle(ImageAnimation animation, UnityEngine.UI.Image image,
                                List<Sprite> frames)
  {
    if (frames == null || frames.Count == 0) return false;

    Stop(animation);

    // Set the sprite directly rather than relying on StopAnimation to do it: that only
    // repaints when something was actually playing, and a freshly revealed coin never was.
    var renderer = image != null ? image : (animation != null ? animation.rendererDelegate : null);
    if (renderer == null) return false;

    renderer.sprite = frames[0];
    return true;
  }

  /// <summary>
  /// Loop <paramref name="frames"/> on <paramref name="animation"/> at
  /// <paramref name="speed"/>. Returns false when there is nothing to play, which the caller
  /// should treat as a wiring error rather than a normal state.
  /// </summary>
  internal static bool Play(ImageAnimation animation, List<Sprite> frames, float speed)
  {
    if (animation == null || frames == null || frames.Count == 0) return false;

    // CancelAllPending, not StopAnimation: the prefab may have StartOnAwake / StartonEnable
    // set, which queues a StartAnimation through Invoke that StopAnimation does not cancel.
    // Same trap ShowLocker documents on SlotSymbolView.
    animation.CancelAllPending();

    // Nothing waits on a coin finishing a pass — it spins until it lands and is put away.
    animation.onLoopComplete = null;
    animation.doLoopAnimation = true;

    animation.textureArray = frames;
    if (speed > 0f) animation.AnimationSpeed = speed;

    animation.StartAnimation();
    return true;
  }

  /// <summary>
  /// Stop a coin spinning. ImageAnimation.StopAnimation repaints the renderer with
  /// textureArray[0] on its way out, so a landing coin comes to rest on its idle pose rather
  /// than on whatever frame it happened to be mid-tumble. Safe on a coin that never started.
  /// </summary>
  internal static void Stop(ImageAnimation animation)
  {
    if (animation == null) return;

    // doLoopAnimation MUST be cleared first: AnimationProcess re-Invokes itself after raising
    // onLoopComplete while the flag is set, which would undo StopAnimation's CancelInvoke.
    animation.doLoopAnimation = false;
    animation.onLoopComplete = null;
    animation.StopAnimation();
    animation.CancelAllPending();
  }
}

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
  /// <param name="onTouchdown">
  /// Fired the instant the coin lands on the target, BEFORE it starts shrinking — the beat
  /// the receiver reacts on (the pig jumps, the meter ticks) so the reaction and the coin
  /// collapsing into it read as one event.
  /// </param>
  /// <param name="onArrive">
  /// Fired after the shrink finishes and the coin is gone. Cleanup, not presentation.
  /// </param>
  internal static IEnumerator Fly(RectTransform coin, Func<RectTransform> target,
                                  float duration, Ease ease,
                                  float shrinkDuration, Ease shrinkEase,
                                  Action onTouchdown, Action onArrive)
  {
    if (coin == null || target == null)
    {
      onTouchdown?.Invoke();
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
    }

    // Land exactly on the target, outside the duration check so a zero-duration flight still
    // ends up in the right place rather than never moving at all.
    var final = target();
    if (final != null) coin.position = final.position;

    onTouchdown?.Invoke();

    if (shrinkDuration > 0f)
    {
      coin.DOKill();
      yield return coin.DOScale(Vector3.zero, shrinkDuration).SetEase(shrinkEase).WaitForCompletion();
    }
    else
    {
      coin.localScale = Vector3.zero;
    }

    onArrive?.Invoke();
  }
}
