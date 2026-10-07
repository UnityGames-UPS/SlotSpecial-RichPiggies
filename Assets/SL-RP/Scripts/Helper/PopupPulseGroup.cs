using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// The scale animation on a popup's children: each target pops in from nothing, overshoots,
/// settles, then breathes on an endless yoyo until something stops it.
///
/// Deliberately knows nothing about wins or tiers — it is driven purely by
/// <see cref="Play"/> / <see cref="Stop"/>, so any popup that wants its contents alive can
/// own one of these. The targets are a serialized list rather than "every child", because a
/// popup usually has a piece or two that must stay put.
///
/// The point of the per-target <see cref="PulseTarget.startDelay"/> and
/// <see cref="PulseTarget.startAtMax"/> is that a group pulsing in lockstep reads as the
/// whole panel zooming. Staggered and phase-inverted, it reads as the panel being alive.
/// </summary>
internal class PopupPulseGroup : MonoBehaviour
{
  /// <summary>
  /// One animated object. Every scale below is a FACTOR of the target's own scene scale, not
  /// an absolute — see <see cref="baseScales"/>.
  /// </summary>
  [Serializable]
  internal class PulseTarget
  {
    public RectTransform target;

    [Tooltip("Held at zero scale for this long before popping in. Give each target in the " +
             "group a different value (0, 0.02, 0.04, ...) so they do not arrive as one lump.")]
    public float startDelay;

    [Header("Pop in")]
    [Tooltip("Peak of the entry pop, as a factor of the target's scene scale.")]
    public float overshootScale = 1.2f;
    public float overshootTime = 0.18f;

    [Tooltip("How long it takes to fall from the overshoot into the pulse range.")]
    public float settleTime = 0.12f;

    [Header("Pulse")]
    public float pulseMin = 0.95f;
    public float pulseMax = 1.05f;

    [Tooltip("One leg of the pulse — min to max. A full cycle is twice this.")]
    public float pulseDuration = 0.6f;

    [Tooltip("Settle to pulseMax and breathe DOWN first, instead of settling to pulseMin and " +
             "breathing up. Tick it on alternate targets and they pulse against each other.")]
    public bool startAtMax;
  }

  [Tooltip("The objects to animate. Order does not matter; the stagger comes from each " +
           "entry's own startDelay.")]
  [SerializeField] private List<PulseTarget> targets = new List<PulseTarget>();

  // Captured once, before anything has been scaled. Every factor above is multiplied by
  // this, so a target that sits at 1.4 in the scene pulses around 1.4 rather than being
  // silently resized to 1 the first time the popup opens.
  private Vector3[] baseScales;

  // Two entries per target once it is up and running: the intro sequence, and the endless
  // pulse the intro hands off to. Held as Tween rather than Sequence because the pulse is a
  // plain tween — see BuildFor for why it cannot be part of the sequence.
  private readonly List<Tween> tweens = new List<Tween>();

  internal bool IsPlaying => tweens.Count > 0;

  private void Awake() => CaptureBaseScales();

  private void CaptureBaseScales()
  {
    if (targets == null)
    {
      baseScales = Array.Empty<Vector3>();
      return;
    }

    baseScales = new Vector3[targets.Count];
    for (int i = 0; i < targets.Count; i++)
    {
      var entry = targets[i];
      baseScales[i] = entry?.target != null ? entry.target.localScale : Vector3.one;

      // A target authored at zero scale would make every factor below zero too, and the
      // popup would animate nothing at all while looking correctly wired.
      if (baseScales[i] == Vector3.zero)
      {
        Debug.LogError($"[PopupPulseGroup] Target '{entry?.target?.name}' has a scene scale " +
                       "of zero, so it can never become visible. Set its scale to 1.", this);
        baseScales[i] = Vector3.one;
      }
    }
  }

  /// <summary>
  /// Restart the whole group from zero scale. Safe to call while already playing — the
  /// previous tweens are killed and the targets reset first.
  /// </summary>
  internal void Play()
  {
    // Reset rather than resume: a popup reopening mid-pulse must start from the pop, not
    // from wherever the last one happened to be in its breathe.
    Stop(instant: true);

    if (targets == null) return;
    if (baseScales == null || baseScales.Length != targets.Count) CaptureBaseScales();

    for (int i = 0; i < targets.Count; i++)
    {
      var entry = targets[i];
      if (entry?.target == null) continue;

      BuildFor(entry, baseScales[i]);
    }
  }

  private void BuildFor(PulseTarget entry, Vector3 baseScale)
  {
    float settleTo = entry.startAtMax ? entry.pulseMax : entry.pulseMin;
    float pulseTo = entry.startAtMax ? entry.pulseMin : entry.pulseMax;

    entry.target.DOKill();
    entry.target.localScale = Vector3.zero;

    var intro = DOTween.Sequence();

    // The entry pop. OutBack on top of an overshoot target is deliberate — the ease adds a
    // little more travel past 1.2 before it comes back, which is what gives it the snap.
    intro.Append(entry.target.DOScale(baseScale * entry.overshootScale, entry.overshootTime)
                             .SetEase(Ease.OutBack));

    intro.Append(entry.target.DOScale(baseScale * settleTo, entry.settleTime)
                             .SetEase(Ease.InOutSine));

    if (entry.startDelay > 0f) intro.SetDelay(entry.startDelay);

    // The endless breathe CANNOT be appended to the sequence above. DOTween allows infinite
    // loops only on a Sequence itself, never on a tween nested inside one — it rewrites the
    // -1 to int.MaxValue and warns. Looping the whole sequence instead is not an option
    // either, since that would replay the entry pop forever. So the intro hands off on
    // completion to a standalone tween, where -1 is legal.
    //
    // Yoyo rather than two chained legs: one tween, and the ease mirrors at both ends so the
    // breathe has no flat spot where it turns around.
    intro.OnComplete(() =>
        tweens.Add(entry.target.DOScale(baseScale * pulseTo, entry.pulseDuration)
                               .SetEase(Ease.InOutSine)
                               .SetLoops(-1, LoopType.Yoyo)));

    tweens.Add(intro);
  }

  /// <summary>
  /// Kill every pulse and put the targets back to the scale they had in the scene.
  /// </summary>
  /// <param name="instant">
  /// Unused today — both paths snap back, because the popup's own CanvasGroup fade is what
  /// covers the exit. Kept as a parameter so a future eased retract has somewhere to live
  /// without changing every caller.
  /// </param>
  internal void Stop(bool instant)
  {
    // Killing the intro also prevents its OnComplete from ever adding a pulse tween, so
    // nothing can be appended to the list behind this loop's back.
    for (int i = tweens.Count - 1; i >= 0; i--) tweens[i]?.Kill();
    tweens.Clear();

    if (targets == null) return;

    // Capture rather than fall back to Vector3.one when this runs before Awake — which it
    // does when WinPopupController.Awake deactivates the popup root at scene load, firing
    // OnDisable here. Nothing has scaled anything yet at that point, so the targets are
    // still at their authored scale and capturing now is exactly right; defaulting to one
    // would quietly resize whatever was authored at something else.
    if (baseScales == null || baseScales.Length != targets.Count) CaptureBaseScales();

    // Restore unconditionally, not just for targets that had a sequence: Play sets
    // localScale to zero directly, so a target whose sequence never got built would
    // otherwise stay invisible.
    for (int i = 0; i < targets.Count; i++)
    {
      var entry = targets[i];
      if (entry?.target == null) continue;

      entry.target.DOKill();
      entry.target.localScale = baseScales[i];
    }
  }

  private void OnDisable()
  {
    // The popup root is deactivated between showings; a live tween on a disabled transform
    // keeps running and would leak a mid-pulse scale into the next open.
    Stop(instant: true);
  }

  private void OnDestroy()
  {
    for (int i = tweens.Count - 1; i >= 0; i--) tweens[i]?.Kill();
    tweens.Clear();
  }
}
