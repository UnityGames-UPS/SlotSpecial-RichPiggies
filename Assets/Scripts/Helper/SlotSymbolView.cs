using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Spine.Unity;

/// <summary>
/// One cell of a reel strip.
///
/// A cell is in exactly ONE of two display modes at any moment, chosen by symbol id:
///
///   SPRITE  — ids 3-10. The Icon child's Image shows the symbol. ImageAnimation sits on
///             that same Image, so a sprite-sequence animation plays in place rather than
///             cross-fading to a separate overlay object.
///   SPINE   — ids 0-2 (BusinessPig / LadyPig / BeachPig). The Spine child's
///             SkeletonGraphic shows the skeleton, parked on NO animation (setup pose).
///
/// Whichever mode is active, the other child's GameObject is switched off.
///
///   SlotIcon        RectTransform + SlotSymbolView + SymbolButtonHandler   (no Graphic)
///     ├ Icon        Image + ImageAnimation
///     ├ Spine       SkeletonGraphic
///     └ WinLineText TMP_Text
///
/// Only <see cref="iconImage"/> is required. A cell with no <see cref="spineGraphic"/>
/// simply cannot show Spine symbols and falls back to its sprite.
/// </summary>
public class SlotSymbolView : MonoBehaviour
{
  [Header("Children")]
  [Tooltip("Image that shows sprite symbols. Required.")]
  [SerializeField] internal Image iconImage;

  [Tooltip("ImageAnimation driving iconImage — expected on the SAME GameObject as iconImage, " +
           "with its rendererDelegate pointing at it. Optional.")]
  [SerializeField] internal ImageAnimation imageAnimation;

  [Tooltip("SkeletonGraphic for Spine symbols. Its skeletonDataAsset is swapped at runtime " +
           "by SlotView, so leave it unassigned in the prefab. Optional.")]
  [SerializeField] internal SkeletonGraphic spineGraphic;

  [Tooltip("Optional per-line win amount label shown over this cell in win-line Phase 2. " +
           "Drawn with SpriteNumberFormatter, so it needs a sprite asset from " +
           "Assets/Fonts/CustomTextFonts — a plain TMP font shows the literal <sprite=N> tags.")]
  [SerializeField] internal TMPro.TMP_Text winLineText;

  [Header("Mystery locker")]
  [Tooltip("Image of the locker that covers this cell during a Mystery reveal. Lives on its " +
           "own child, rendered ABOVE both Icon and Spine, and is independent of the " +
           "sprite/Spine display mode. Optional.")]
  [SerializeField] internal Image lockerImage;

  [Tooltip("ImageAnimation driving lockerImage — expected on the SAME GameObject as it, with " +
           "its rendererDelegate pointing at it and StartOnAwake / StartonEnable both OFF.")]
  [SerializeField] internal ImageAnimation lockerAnimation;

  [Header("Coin overlay")]
  [Tooltip("The coin stamped on this cell by a coinOverlays entry. Its child sits BETWEEN " +
           "Icon/Spine and Locker in sibling order, so a coin on a Mystery cell stays hidden " +
           "until the locker opens. Inactive in the prefab. Optional.")]
  [SerializeField] internal RectTransform coinOverlay;

  [Tooltip("Image on the coinOverlay child. Its sprite is driven by coinAnimation.")]
  [SerializeField] internal Image coinImage;

  [Tooltip("ImageAnimation driving coinImage — expected on the SAME GameObject as it, with " +
           "its rendererDelegate pointing at it and StartOnAwake / StartonEnable both OFF. " +
           "The frames come from PigMeterController per coin type, so leave textureArray " +
           "empty in the prefab.")]
  [SerializeField] internal ImageAnimation coinAnimation;

  /// <summary>Symbol id currently displayed, or -1 before the first SetSymbol.</summary>
  internal int SymbolId { get; private set; } = -1;

  /// <summary>True while this cell is showing its Spine skeleton rather than a sprite.</summary>
  internal bool IsSpineMode { get; private set; }

  // What the SkeletonGraphic is currently built from. Swapping a skeletonDataAsset forces
  // a mesh rebuild, so the filler reshuffle only pays that cost when the pig actually
  // changes — re-rolling the same id, or rolling a sprite id, is free.
  private SkeletonDataAsset appliedSkeleton;

  // The symbol's resting sprite, kept so a finished win animation can be undone.
  private Sprite restingSprite;

  // Where this cell lives in the reel strip. A winning symbol is reparented to the win
  // animation layer so it renders above the darkening overlay; these let it go back to the
  // exact slot it came from, including if a spin interrupts the animation mid-way.
  private Transform homeParent;
  private int homeSiblingIndex;

  private void Reset()
  {
    // Convenience auto-wire when the component is first added in the Editor.
    var icon = transform.Find("Icon");
    if (icon != null) iconImage = icon.GetComponent<Image>();
    if (iconImage == null) iconImage = GetComponentInChildren<Image>(true);
    if (iconImage != null) imageAnimation = iconImage.GetComponent<ImageAnimation>();

    spineGraphic = GetComponentInChildren<SkeletonGraphic>(true);

    var txt = transform.Find("WinLineText");
    if (txt != null) winLineText = txt.GetComponent<TMPro.TMP_Text>();

    var locker = transform.Find("Locker");
    if (locker != null)
    {
      lockerImage = locker.GetComponent<Image>();
      lockerAnimation = locker.GetComponent<ImageAnimation>();
    }

    var coin = transform.Find("Coin");
    if (coin != null)
    {
      coinOverlay = coin as RectTransform;
      coinImage = coin.GetComponent<Image>();
      coinAnimation = coin.GetComponent<ImageAnimation>();
    }
  }

  private void Awake()
  {
    if (imageAnimation == null && iconImage != null)
      imageAnimation = iconImage.GetComponent<ImageAnimation>();

    if (lockerAnimation == null && lockerImage != null)
      lockerAnimation = lockerImage.GetComponent<ImageAnimation>();

    // Frozen is the default state for the whole life of the cell — a resting symbol must
    // not tick, or its rigged bones drift as the column tweens. Only StartStagedAnimation
    // lifts it, and StopWinAnimation puts it back.
    if (spineGraphic != null) spineGraphic.freeze = true;

    homeParent = transform.parent;
    homeSiblingIndex = transform.GetSiblingIndex();

    if (coinOverlay != null)
    {
      if (coinImage == null) coinImage = coinOverlay.GetComponent<Image>();

      // Captured before anything can reparent the coin, so a flight always has an exact
      // slot to come back to — including its designed offset within the cell.
      coinHomeParent = coinOverlay.parent;
      coinHomeSiblingIndex = coinOverlay.GetSiblingIndex();
      coinHomeAnchoredPosition = coinOverlay.anchoredPosition;

      // The prefab should ship it inactive; make sure, so a stray enabled coin cannot sit on
      // the grid through a spin that reveals none.
      coinOverlay.gameObject.SetActive(false);
    }
  }

  #region Win animation layer

  /// <summary>
  /// Reparent this cell onto the win-animation layer so it draws above the darkening
  /// overlay. World position is preserved, so the symbol does not appear to move.
  /// </summary>
  internal void MoveToAnimationLayer(Transform layer)
  {
    if (layer == null || transform.parent == layer) return;
    transform.SetParent(layer, worldPositionStays: true);
  }

  /// <summary>
  /// Put the cell back in the exact slot it came from. Idempotent and safe to call on a
  /// cell that never left, which is what makes a spin interrupting mid-animation recover.
  /// </summary>
  internal void ReturnHome()
  {
    if (homeParent == null) return;
    if (transform.parent == homeParent && transform.GetSiblingIndex() == homeSiblingIndex) return;

    transform.SetParent(homeParent, worldPositionStays: true);
    transform.SetSiblingIndex(homeSiblingIndex);
  }

  #endregion

  #region Symbol display

  /// <summary>
  /// Show <paramref name="id"/>. Pass a non-null <paramref name="skeleton"/> to render it
  /// in Spine (parked on no animation), otherwise the sprite is used. Exactly one of the
  /// two children ends up active.
  /// </summary>
  internal void SetSymbol(int id, Sprite sprite, SkeletonDataAsset skeleton)
  {
    SymbolId = id;

    if (skeleton != null && spineGraphic != null)
      ShowSpine(skeleton);
    else
      ShowSprite(sprite);
  }

  private void ShowSprite(Sprite sprite)
  {
    IsSpineMode = false;

    if (spineGraphic != null && spineGraphic.gameObject.activeSelf)
      spineGraphic.gameObject.SetActive(false);

    if (iconImage == null) return;

    if (!iconImage.gameObject.activeSelf) iconImage.gameObject.SetActive(true);

    // A resting symbol never animates. This also neutralises ImageAnimation's
    // StartOnAwake / StartonEnable flags, which would otherwise re-launch the win
    // animation every single time this GameObject is re-enabled — which happens on every
    // filler reshuffle, so a ticked flag makes symbols animate continuously for no reason.
    if (imageAnimation != null) imageAnimation.CancelAllPending();

    if (sprite != null)
    {
      iconImage.sprite = sprite;
      // Remembered because ImageAnimation overwrites iconImage.sprite frame by frame, and
      // its StopAnimation leaves the FIRST ANIMATION FRAME behind rather than the symbol.
      restingSprite = sprite;
    }
    iconImage.enabled = true;
  }

  private void ShowSpine(SkeletonDataAsset skeleton)
  {
    IsSpineMode = true;

    if (iconImage != null && iconImage.gameObject.activeSelf)
      iconImage.gameObject.SetActive(false);

    // SkeletonGraphic will not build on an inactive GameObject, so activate first.
    if (!spineGraphic.gameObject.activeSelf) spineGraphic.gameObject.SetActive(true);

    if (appliedSkeleton != skeleton)
    {
      spineGraphic.skeletonDataAsset = skeleton;
      spineGraphic.Initialize(true);
      appliedSkeleton = skeleton;
    }

    // Park on NO animation — the symbol rests in its setup pose until something asks for
    // a win animation.
    if (spineGraphic.AnimationState != null) spineGraphic.AnimationState.ClearTracks();
    if (spineGraphic.Skeleton != null)
    {
      spineGraphic.Skeleton.SetToSetupPose();
      spineGraphic.Skeleton.SetColor(Color.white);
    }

    FreezeSpine();
  }

  /// <summary>
  /// Build the skeleton's current pose into its mesh once, then stop it updating.
  ///
  /// A resting symbol MUST be frozen: several of the pig skeletons have rigged/physics
  /// bones that keep reacting to the transform, so an unfrozen symbol visibly wobbles
  /// while its column tweens past. <c>SkeletonGraphic.freeze</c> short-circuits Update,
  /// FixedUpdate and LateUpdate — and LateUpdate is what calls UpdateMesh — so the pose
  /// has to be flushed to the mesh BEFORE the freeze or the cell renders the previous
  /// symbol's pose. Physics.Reset snaps physics bones onto that pose instead of letting
  /// them swing into it.
  /// </summary>
  private void FreezeSpine()
  {
    if (spineGraphic == null || spineGraphic.Skeleton == null) return;

    spineGraphic.freeze = false;
    spineGraphic.Skeleton.UpdateWorldTransform(Spine.Skeleton.Physics.Reset);
    spineGraphic.UpdateMesh();
    spineGraphic.freeze = true;
  }

  /// <summary>Let the skeleton update again — only while an animation is actually playing.</summary>
  private void UnfreezeSpine()
  {
    if (spineGraphic != null) spineGraphic.freeze = false;
  }

  /// <summary>Fade whichever graphic is currently showing the symbol.</summary>
  internal void SetSymbolAlpha(float alpha, float duration = 0f)
  {
    Graphic target = IsSpineMode ? (Graphic)spineGraphic : iconImage;
    if (target == null) return;

    target.DOKill();
    if (duration > 0f) target.DOFade(alpha, duration);
    else
    {
      Color c = target.color;
      target.color = new Color(c.r, c.g, c.b, alpha);
    }
  }

  #endregion

  #region Win animation — stage / start

  // A cell stages its animation first and starts it later, so every winning symbol in a
  // stage begins on the SAME frame and stays in step with the others. Both symbol kinds
  // play their sequence EXACTLY ONCE and hold the final frame until the caller cleans up.

  private string stagedSpineAnimation;
  private System.Action stagedOnFinished;
  private bool frameAnimationStaged;


  /// <summary>
  /// Stage a sprite-sequence animation on this cell's icon. ImageAnimation drives the icon
  /// Image directly, so the frames replace the resting sprite in place — no cross-fade.
  /// <paramref name="onFinished"/> fires once, when the sequence ends.
  /// Returns false when the cell is in Spine mode, has no ImageAnimation, or no frames.
  /// </summary>
  internal bool StageFrameAnimation(List<Sprite> frames, float animationSpeed, System.Action onFinished)
  {
    if (IsSpineMode) return false;
    if (imageAnimation == null || iconImage == null) return false;
    if (frames == null || frames.Count == 0) return false;

    imageAnimation.CancelAllPending();
    imageAnimation.textureArray = frames;

    // Play the sequence ONCE. With this false, ImageAnimation raises onLoopComplete at the
    // end of the run and then parks itself instead of re-scheduling.
    imageAnimation.doLoopAnimation = false;

    if (animationSpeed > 0f) imageAnimation.AnimationSpeed = animationSpeed;
    imageAnimation.onLoopComplete = _ => onFinished?.Invoke();

    if (!iconImage.gameObject.activeSelf) iconImage.gameObject.SetActive(true);
    SetSymbolAlpha(1f);

    frameAnimationStaged = true;
    stagedSpineAnimation = null;
    stagedOnFinished = onFinished;
    return true;
  }

  /// <summary>
  /// Stage a Spine animation, switching skin first if one is named. Nothing plays until
  /// <see cref="StartStagedAnimation"/>. Returns false when the cell is not in Spine mode.
  /// </summary>
  internal bool StageSpineAnimation(string animationName, string skinName, System.Action onFinished)
  {
    if (!IsSpineMode || spineGraphic == null || string.IsNullOrEmpty(animationName)) return false;
    if (spineGraphic.Skeleton == null) return false;

    // Refuse a name the skeleton does not have. SetAnimation would throw on it, and a throw
    // inside the start loop leaves every cell after this one un-started with its pending
    // count already raised — the stage then waits out its whole safety timeout.
    var animation = spineGraphic.Skeleton.Data.FindAnimation(animationName);
    if (animation == null)
    {
      Debug.LogError($"[SlotSymbolView] Symbol {SymbolId}: skeleton has no animation " +
                     $"'{animationName}'. Check the symbolWinAnims entry on SlotView.", this);
      return false;
    }

    if (!string.IsNullOrEmpty(skinName))
    {
      spineGraphic.Skeleton.SetSkin(skinName);
      spineGraphic.Skeleton.SetSlotsToSetupPose();
    }

    stagedSpineAnimation = animationName;
    stagedOnFinished = onFinished;
    frameAnimationStaged = false;
    return true;
  }

  /// <summary>Start whatever was staged. Called on every cell in the same frame.</summary>
  internal void StartStagedAnimation()
  {
    if (!string.IsNullOrEmpty(stagedSpineAnimation))
    {
      // Resting skeletons are frozen (see FreezeSpine) — it has to tick again to advance.
      UnfreezeSpine();

      // loop: false — one pass, then TrackEntry.Complete tells us it is done.
      var entry = spineGraphic.AnimationState.SetAnimation(0, stagedSpineAnimation, false);
      var callback = stagedOnFinished;
      if (entry != null && callback != null) entry.Complete += _ => callback();

      stagedSpineAnimation = null;
      return;
    }

    if (frameAnimationStaged && imageAnimation != null)
    {
      imageAnimation.StartAnimation();
      frameAnimationStaged = false;
    }
  }

  #endregion

  #region Mystery locker

  // The locker is a THIRD display child, independent of the sprite/Spine mode split above:
  // it simply covers whatever the cell is showing. Because the server's matrix already
  // carries the revealed symbol, the cell underneath is correct before the reveal ever
  // plays — the locker only has to hide it, open, and switch itself off.
  //
  // It uses the same stage-then-start idiom as the win animations so every locker in the
  // grid begins on the SAME frame.

  private bool lockerStaged;

  /// <summary>True while this cell is covered by its locker.</summary>
  internal bool IsLockerVisible => lockerImage != null && lockerImage.gameObject.activeSelf;

  /// <summary>
  /// Cover the cell with its closed locker. Returns false when the cell has no locker child.
  /// </summary>
  internal bool ShowLocker(Sprite closedSprite)
  {
    if (lockerImage == null) return false;

    if (!lockerImage.gameObject.activeSelf) lockerImage.gameObject.SetActive(true);

    // Activating the object may have queued a StartAnimation through ImageAnimation's
    // StartonEnable flag. Cancel it here rather than trusting the Inspector — an unnoticed
    // tick would make the locker play its reveal the moment it appears, mid-spin.
    if (lockerAnimation != null)
    {
      lockerAnimation.CancelAllPending();
      lockerAnimation.onLoopComplete = null;
      lockerAnimation.doLoopAnimation = false;
    }

    if (closedSprite != null) lockerImage.sprite = closedSprite;

    Color c = lockerImage.color;
    lockerImage.color = new Color(c.r, c.g, c.b, 1f);
    lockerImage.enabled = true;

    lockerStaged = false;
    return true;
  }

  /// <summary>
  /// Stage the locker's open-and-exit sequence. Nothing plays until
  /// <see cref="StartStagedLocker"/>. <paramref name="onFinished"/> fires once, at the end
  /// of the single pass. Returns false when there is no locker or no frames.
  /// </summary>
  internal bool StageLockerReveal(List<Sprite> frames, float animationSpeed, System.Action onFinished)
  {
    if (lockerAnimation == null || lockerImage == null) return false;
    if (frames == null || frames.Count == 0) return false;

    lockerAnimation.CancelAllPending();
    lockerAnimation.textureArray = frames;

    // One pass only: ImageAnimation raises onLoopComplete at the end of the run and parks
    // itself instead of re-scheduling.
    lockerAnimation.doLoopAnimation = false;

    if (animationSpeed > 0f) lockerAnimation.AnimationSpeed = animationSpeed;
    lockerAnimation.onLoopComplete = _ => onFinished?.Invoke();

    if (!lockerImage.gameObject.activeSelf) lockerImage.gameObject.SetActive(true);

    lockerStaged = true;
    return true;
  }

  /// <summary>Start the staged locker reveal. Called on every locker in the same frame.</summary>
  internal void StartStagedLocker()
  {
    if (!lockerStaged || lockerAnimation == null) return;

    lockerStaged = false;
    lockerAnimation.StartAnimation();
  }

  /// <summary>
  /// Take the locker down. Safe on a cell that never had one, and safe to call from inside
  /// the reveal's own completion callback.
  /// </summary>
  internal void HideLocker()
  {
    lockerStaged = false;

    if (lockerAnimation != null)
    {
      lockerAnimation.onLoopComplete = null;

      // Same trap as StopWinAnimation: AnimationProcess re-Invokes itself after raising
      // onLoopComplete when doLoopAnimation is set, so StopAnimation's CancelInvoke would be
      // undone if we stop from inside that callback. Clear the flag first.
      lockerAnimation.doLoopAnimation = false;
      lockerAnimation.StopAnimation();
      lockerAnimation.CancelAllPending();
    }

    if (lockerImage != null && lockerImage.gameObject.activeSelf)
      lockerImage.gameObject.SetActive(false);
  }

  #endregion

  #region Coin overlay

  // A coin stamped on this cell by payload.coinOverlays. Unlike the win animation, which
  // reparents the WHOLE cell, only the coin child leaves — the symbol underneath stays put
  // and keeps playing its win animation while the coin flies off to its pig.
  //
  // The child is cached rather than instantiated: a cell can only ever reveal one coin per
  // spin, so there is nothing to pool. It goes back home disabled when the flight ends.
  //
  // The coin's look is a looping PNG sequence, one per coin type, supplied by
  // PigMeterController rather than baked into the prefab — nine sequences share this one
  // child, so the cell cannot know which it will show until the result arrives.

  private Transform coinHomeParent;
  private int coinHomeSiblingIndex;
  private Vector2 coinHomeAnchoredPosition;

  /// <summary>The coin's RectTransform, or null when this cell has no coin child wired.</summary>
  internal RectTransform CoinRect => coinOverlay;

  /// <summary>
  /// Reveal the coin on this cell, sitting STILL on frame 0 of <paramref name="frames"/> —
  /// its idle pose. It does not animate until <see cref="StartCoinAnimation"/> launches it.
  /// Returns false when the cell has no coin child, which is a prefab wiring bug rather than
  /// a normal state.
  /// </summary>
  internal bool ShowCoin(List<Sprite> frames)
  {
    if (coinOverlay == null) return false;

    // Any previous flight must be undone before the coin is shown again, or it appears
    // already shrunk or still parented to the flight layer.
    ReturnCoinHome();

    coinOverlay.localScale = Vector3.one;

    // Activate BEFORE touching the animation: ImageAnimation's OnDisable stops it, so work
    // done on a still-inactive object would be undone the moment it ran.
    coinOverlay.gameObject.SetActive(true);

    // Not fatal if this fails — the coin is on screen either way. Worth saying so, because a
    // coin stuck on the wrong sprite looks like a missing asset.
    if (!CoinAnimator.ShowIdle(coinAnimation, coinImage, frames))
    {
      Debug.LogError("[SlotSymbolView] Coin revealed with no frames — check the coin's Image " +
                     "and ImageAnimation wiring on the SlotIcon prefab, and the coinFrames " +
                     "list for this coin type on PigMeterController.", this);
    }

    return true;
  }

  /// <summary>
  /// Set the coin spinning for its flight. Called as it leaves the cell, so a coin sitting on
  /// the grid waiting its turn in the stagger stays on its idle pose.
  /// </summary>
  internal void StartCoinAnimation(List<Sprite> frames, float animationSpeed)
  {
    CoinAnimator.Play(coinAnimation, frames, animationSpeed);
  }

  /// <summary>Stop the coin spinning and rest it on its idle pose. Used the moment it lands.</summary>
  internal void StopCoinAnimation()
  {
    CoinAnimator.Stop(coinAnimation);
  }

  /// <summary>
  /// Lift the coin onto <paramref name="layer"/> so it draws above the reels for its flight.
  /// World position is preserved, so it does not appear to move.
  /// </summary>
  internal void DetachCoin(Transform layer)
  {
    if (coinOverlay == null || layer == null || coinOverlay.parent == layer) return;
    coinOverlay.SetParent(layer, worldPositionStays: true);
  }

  /// <summary>
  /// Put the coin back where it came from, hidden and at full scale. Idempotent and safe on
  /// a coin that never flew, which is what makes a spin interrupting mid-flight recover.
  /// </summary>
  internal void ReturnCoinHome()
  {
    if (coinOverlay == null) return;

    coinOverlay.DOKill();
    CoinAnimator.Stop(coinAnimation);

    if (coinHomeParent != null && coinOverlay.parent != coinHomeParent)
    {
      coinOverlay.SetParent(coinHomeParent, worldPositionStays: false);
      coinOverlay.SetSiblingIndex(coinHomeSiblingIndex);
    }

    coinOverlay.anchoredPosition = coinHomeAnchoredPosition;
    coinOverlay.localScale = Vector3.one;

    if (coinOverlay.gameObject.activeSelf) coinOverlay.gameObject.SetActive(false);
  }

  #endregion

  /// <summary>
  /// Stop any win animation and return the cell to its resting look, without changing
  /// which display mode it is in.
  /// </summary>
  internal void StopWinAnimation(float fadeDuration = 0.2f)
  {
    if (imageAnimation != null)
    {
      imageAnimation.onLoopComplete = null;

      // doLoopAnimation MUST be cleared before StopAnimation, and this is not optional.
      // ImageAnimation.AnimationProcess raises onLoopComplete and then, still inside the
      // same call, re-schedules itself if doLoopAnimation is set. So when we stop from
      // inside that callback, StopAnimation's CancelInvoke is immediately undone by the
      // re-Invoke a few lines later and the animation loops forever.
      imageAnimation.doLoopAnimation = false;
      imageAnimation.StopAnimation();
      imageAnimation.CancelAllPending();

      // StopAnimation parks the renderer on textureArray[0] — the first frame of the WIN
      // animation, not the symbol. Put the resting sprite back.
      if (!IsSpineMode && iconImage != null && restingSprite != null)
        iconImage.sprite = restingSprite;
    }

    if (IsSpineMode && spineGraphic != null)
    {
      if (spineGraphic.AnimationState != null) spineGraphic.AnimationState.ClearTracks();
      if (spineGraphic.Skeleton != null) spineGraphic.Skeleton.SetToSetupPose();

      // Back to a resting symbol: flush the setup pose to the mesh, then stop updating so
      // the rigged bones do not drift while the column tweens.
      FreezeSpine();
    }

    SetSymbolAlpha(1f, fadeDuration);
  }

  /// <summary>Hard reset with no tweens — used when a new spin starts.</summary>
  internal void ResetVisualState()
  {
    StopWinAnimation(0f);
    // A spin starting mid-reveal must not leave a locker covering the cell, or a coin
    // stranded on the flight layer, for the whole next round.
    HideLocker();
    ReturnCoinHome();
    ReturnHome();
    transform.DOKill();
    transform.localScale = Vector3.one;
    HideWinText(0f);
  }

  #region Win line text

  [Header("Win line text")]
  [Tooltip("Fade time for the per-line payout label appearing and disappearing.")]
  [SerializeField] private float winTextFadeDuration = 0.2f;

  internal void ShowWinText(double amount)
  {
    if (winLineText == null) return;

    // tint: true is required here and nowhere else in the meters — this label is FADED in and
    // out below, and DOFade only writes the text colour. An untinted sprite glyph carries its
    // own colour and would sit at full opacity through the whole fade.
    SpriteNumberFormatter.Apply(winLineText, amount, maxDecimals: 3, grouping: false, tint: true);
    winLineText.DOKill();

    // Fade up from wherever the label currently is, not from zero. With a single winning
    // line the same label is hidden and re-shown every pass, and the fade-out is still
    // running when the next pass asks for it — resetting to 0 there snaps it dark before
    // fading in again. Only a label that is genuinely off starts from transparent.
    if (!winLineText.gameObject.activeSelf)
    {
      winLineText.gameObject.SetActive(true);
      SetWinTextAlpha(0f);
    }
    else if (Mathf.Approximately(winLineText.color.a, 1f))
    {
      return; // already fully up — leave it alone rather than re-running the tween
    }

    winLineText.DOFade(1f, winTextFadeDuration);
  }

  /// <summary>
  /// Fade the label out and switch it off when it lands. Pass 0 to drop it immediately —
  /// used by the hard reset, where a tween that outlives the spin would re-hide a label the
  /// next round has already shown.
  /// </summary>
  internal void HideWinText(float fadeDuration = -1f)
  {
    if (winLineText == null) return;

    float duration = fadeDuration >= 0f ? fadeDuration : winTextFadeDuration;

    winLineText.DOKill();
    if (duration <= 0f || !winLineText.gameObject.activeSelf)
    {
      winLineText.gameObject.SetActive(false);
      SetWinTextAlpha(1f);
      return;
    }

    winLineText.DOFade(0f, duration).OnComplete(() =>
    {
      winLineText.gameObject.SetActive(false);
      // Left opaque so anything that switches the object on without going through
      // ShowWinText still gets a visible label.
      SetWinTextAlpha(1f);
    });
  }

  private void SetWinTextAlpha(float alpha)
  {
    Color c = winLineText.color;
    winLineText.color = new Color(c.r, c.g, c.b, alpha);
  }

  #endregion
}
