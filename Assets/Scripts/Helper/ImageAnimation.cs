using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ImageAnimation : MonoBehaviour
{
  public enum ImageState
  {
    NONE,
    PLAYING,
    PAUSED
  }

  public static ImageAnimation Instance;

  public List<Sprite> textureArray;
  public Image rendererDelegate;
  public bool useSharedMaterial = true;
  public bool doLoopAnimation = true;

  public System.Action<int> onLoopComplete;
  private int currentLoopCount = 0;

  [SerializeField] private bool StartOnAwake;
  [SerializeField] private bool StartonEnable;

  [HideInInspector]
  public ImageState currentAnimationState;

  private int indexOfTexture;
  private float idealFrameRate = 0.0416666679f; // ~24 fps
  private float delayBetweenAnimation;

  public float AnimationSpeed = 5f;
  public float delayBetweenLoop;
  public float animationStartDelay;

  private void Awake()
  {
    if (Instance == null)
    {
      Instance = this;
    }
    EnsureRenderer();
    if (StartOnAwake)
    {
      Invoke(nameof(StartAnimation), animationStartDelay);
    }
  }

  private void EnsureRenderer()
  {
    if (rendererDelegate == null)
    {
      rendererDelegate = GetComponent<Image>();
    }
  }

  void Start()
  {
    EnsureRenderer();
  }

  private void OnEnable()
  {
    EnsureRenderer();
    if (StartonEnable)
    {
      Invoke(nameof(StartAnimation), animationStartDelay);
    }
  }

  private void OnDisable()
  {
    StopAnimation();
  }

  private void AnimationProcess()
  {
    if (textureArray == null || textureArray.Count == 0) return;

    SetTextureOfIndex();
    indexOfTexture++;

    if (indexOfTexture >= textureArray.Count)
    {
      indexOfTexture = 0;
      currentLoopCount++;
      onLoopComplete?.Invoke(currentLoopCount);

      // A listener is allowed to stop the animation from inside onLoopComplete — that is
      // how callers wait for "N full passes". StopAnimation's CancelInvoke cannot help it
      // here, because we are already inside the invoked call and would re-Invoke below.
      // Honour the stop instead of overriding it.
      if (currentAnimationState != ImageState.PLAYING) return;

      if (doLoopAnimation)
      {
        Invoke(nameof(AnimationProcess), delayBetweenAnimation + delayBetweenLoop);
      }
      else
      {
        currentAnimationState = ImageState.NONE;
      }
    }
    else
    {
      Invoke(nameof(AnimationProcess), delayBetweenAnimation);
    }
  }

  /// <summary>
  /// Wall-clock length of one full pass at the current AnimationSpeed, using the same
  /// formula StartAnimation does. Callers that need to know when a one-shot sequence ends
  /// can use this as a deadline instead of waiting on onLoopComplete forever.
  /// Note the delay scales with frame count, so duration grows with the SQUARE of it.
  /// </summary>
  public float GetSequenceDuration()
  {
    if (textureArray == null || textureArray.Count == 0) return 0f;

    float delay = idealFrameRate * (float)textureArray.Count / AnimationSpeed;
    if (delay <= 0) delay = 0.05f;
    return delay * textureArray.Count;
  }

  public void StartAnimation()
  {
    if (textureArray == null || textureArray.Count == 0) return;

    EnsureRenderer();
    if (rendererDelegate == null) return;

    CancelInvoke(nameof(AnimationProcess));
    indexOfTexture = 0;
    currentLoopCount = 0;
    currentAnimationState = ImageState.PLAYING;

    RevertToInitialState();

    delayBetweenAnimation = idealFrameRate * (float)textureArray.Count / AnimationSpeed;
    if (delayBetweenAnimation <= 0) delayBetweenAnimation = 0.05f;

    Invoke(nameof(AnimationProcess), delayBetweenAnimation);
  }

  public void PlayAnimation()
  {
    StartAnimation();
  }

  public void Play()
  {
    StartAnimation();
  }

  public void PauseAnimation()
  {
    if (currentAnimationState == ImageState.PLAYING)
    {
      CancelInvoke(nameof(AnimationProcess));
      currentAnimationState = ImageState.PAUSED;
    }
  }

  public void ResumeAnimation()
  {
    if (currentAnimationState == ImageState.PAUSED && !IsInvoking(nameof(AnimationProcess)))
    {
      Invoke(nameof(AnimationProcess), delayBetweenAnimation);
      currentAnimationState = ImageState.PLAYING;
    }
  }

  /// <summary>
  /// Hard reset: cancels the frame timer AND any pending auto-start queued by
  /// StartOnAwake / StartonEnable. StopAnimation cannot do this — it early-returns when the
  /// state is already NONE, and it only cancels AnimationProcess, so a queued StartAnimation
  /// would still fire and leave the symbol animating on its own.
  /// </summary>
  public void CancelAllPending()
  {
    CancelInvoke();
    currentAnimationState = ImageState.NONE;
    currentLoopCount = 0;
    indexOfTexture = 0;
  }

  public void StopAnimation()
  {
    if (currentAnimationState != ImageState.NONE)
    {
      EnsureRenderer();
      if (rendererDelegate != null && textureArray != null && textureArray.Count > 0)
      {
        rendererDelegate.sprite = textureArray[0];
      }
      CancelInvoke(nameof(AnimationProcess));
      currentAnimationState = ImageState.NONE;
      currentLoopCount = 0;
    }
  }

  public void RevertToInitialState()
  {
    indexOfTexture = 0;
    SetTextureOfIndex();
  }

  private void SetTextureOfIndex()
  {
    if (textureArray == null || textureArray.Count == 0 || indexOfTexture < 0 || indexOfTexture >= textureArray.Count) return;

    EnsureRenderer();
    if (rendererDelegate != null)
    {
      rendererDelegate.sprite = textureArray[indexOfTexture];
    }
  }
}
