using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;
using System.Linq;
using System;
using TMPro;
using Newtonsoft.Json;

public class SlotController : MonoBehaviour
{
  [SerializeField] internal AudioController audioController;
  [SerializeField] internal GameManager gameManager;

  [Header("Sprites")]
  [SerializeField] private Sprite[] iconImages;

  [Header("Slot Images")]
  [SerializeField] internal List<SlotImage> slotMatrix;
  [SerializeField] private List<SlotImage> allMatrix;

  [Header("Slots Transforms")]
  [SerializeField] private RectTransform[] Slot_Transform;

  // Indexed by column so a tween always maps to its own Slot_Transform.
  // (Do NOT Add in completion-callback order: intro moves finish at different
  //  times depending on each reel's previous rest position, which scrambled the order.)
  private Tweener[] alltweens = new Tweener[0];

  void Awake()
  {
    ShuffleMatrix();
  }

  internal IEnumerator StartSpin()
  {
    KillAllTweens();
    FillMultiplierColumn();
    alltweens = new Tweener[Slot_Transform.Length];

    List<Tween> initTweens = new();
    audioController.Play("reelspin");
    for (int i = 0; i < Slot_Transform.Length; i++)
    {
      initTweens.Add(InitializeTweening(Slot_Transform[i], i));
    }
    yield return initTweens[0].WaitForCompletion();
    yield return initTweens[^1].WaitForCompletion();
  }

  internal void PopulateSlotMatrix(List<List<string>> resultData)
  {
    for (int i = 0; i < resultData.Count; i++)
    {
      for (int j = 0; j < resultData[i].Count; j++)
      {
        int id = int.Parse(resultData[i][j]);
        if (id == 0) //skip 0th index cuz its blank
        {
          int randId = UnityEngine.Random.Range(1, 8);
          slotMatrix[j].slotImages[i].SetIcon(ID: id, image: iconImages[randId]);
          continue;
        }
        slotMatrix[j].slotImages[i].SetIcon(ID: id, image: iconImages[id]); // matrix is [row][col]; slotMatrix is column-major
      }
    }
  }

  internal IEnumerator StopSpin(Action playFallAudio, List<List<string>> resultData)
  {
    bool playedStopAtOnce = false;

    for (int i = 0; i < Slot_Transform.Length; i++)
    {
      StopTweening(Slot_Transform[i], i, resultData[1][i] == "0");

      if (!gameManager.immediateStop)
      {
        // Staggered stop: one stop sfx per column as it lands.
        playFallAudio?.Invoke();
        yield return new WaitForSecondsRealtime(gameManager.turboMode ? 0.2f : 0.6f);
      }
      else if (!playedStopAtOnce)
      {
        // Reels settle all at once (turbo / stop button) — play the stop sfx a single time.
        playFallAudio?.Invoke();
        playedStopAtOnce = true;
      }
    }
    yield return alltweens[^2].WaitForCompletion();
    yield return alltweens[^1].WaitForCompletion();
    KillAllTweens();
  }

  internal void ShuffleMatrix(bool ignoreResultMatrix = false)
  {
    HashSet<SlotIconView>[] resultSets = null;
    if (ignoreResultMatrix)
    {
      resultSets = new HashSet<SlotIconView>[slotMatrix.Count];
      for (int i = 0; i < slotMatrix.Count; i++)
        resultSets[i] = new HashSet<SlotIconView>(slotMatrix[i].slotImages);
    }

    for (int i = 0; i < allMatrix.Count; i++)
    {
      for (int j = 0; j < allMatrix[i].slotImages.Count; j++)
      {
        if (ignoreResultMatrix && i < resultSets.Length && resultSets[i].Contains(allMatrix[i].slotImages[j]))
          continue;
        
        int randomIndex;
        if(i < 3)
          randomIndex = UnityEngine.Random.Range(1, 8);
        else
          randomIndex = UnityEngine.Random.Range(8, iconImages.Length);
        allMatrix[i].slotImages[j].SetIcon(ID: randomIndex, image: iconImages[randomIndex]);
      }
    }
  }
  
  // The 4th reel is the multiplier column: it must only ever display multiplier
  // symbols (IDs 8-11). Refill EVERY cell (including result cells) so a stale/buggy
  // non-multiplier left by PopulateSlotMatrix can't scroll past while the reel spins.
  private void FillMultiplierColumn()
  {
    SlotImage column = allMatrix[^1];
    for (int j = 0; j < column.slotImages.Count; j++)
    {
      int randomIndex = UnityEngine.Random.Range(8, iconImages.Length);
      column.slotImages[j].SetIcon(ID: randomIndex, image: iconImages[randomIndex]);
    }
  }

  // Reel travel speed in local units/second. Durations are derived from this so
  // every move (intro, loop, stop) runs at a constant speed regardless of distance.
  [SerializeField] private float reelSpeed = 2857f;

  [SerializeField] private float BigSpinTopY = 1900f;
  [SerializeField] private float SmallSpinTopY = 1900f;
  [SerializeField] private float BigSpinBottomY = -1900f;
  [SerializeField] private float SmallSpinBottomY = -1900f;
  [SerializeField] private float BigSpinRestY = 150f;
  [SerializeField] private float BigSpinBlankRestY = -1900f;
  [SerializeField] private float SmallRestY = 145.402496f;

  // Duration needed to travel between two Y positions at reelSpeed.
  private float DurationFor(float fromY, float toY)
    => Mathf.Abs(toY - fromY) / Mathf.Max(reelSpeed, 0.0001f);

  #region TweeningCode
  private Tween InitializeTweening(Transform slotTransform, int index)
  {
    float SpinBottomY = index != 3 ? BigSpinBottomY : SmallSpinBottomY;
    float SpinTopY = index != 3 ? BigSpinTopY : SmallSpinTopY;

    Sequence seq = DOTween.Sequence();
    float startY = slotTransform.localPosition.y;
    seq.Append(slotTransform.DOLocalMoveY(SpinBottomY, DurationFor(startY, SpinBottomY)).SetEase(Ease.Linear));
    seq.AppendCallback(() =>
    {
      slotTransform.localPosition = new Vector2(slotTransform.localPosition.x, SpinTopY);
      // ShuffleMatrix(ignoreResultMatrix: true);
      Tweener tweener = slotTransform.DOLocalMoveY(SpinBottomY, DurationFor(SpinTopY, SpinBottomY))
        .SetLoops(-1, LoopType.Restart)
        .SetEase(Ease.Linear);
      alltweens[index] = tweener;
    });
    return seq;
  }

  private void StopTweening(Transform slotTransform, int index, bool landOnBlank)
  {
    float SpinTopY = index != 3 ? BigSpinTopY : SmallSpinTopY;
    float RestY = index != 3 ?  landOnBlank ? BigSpinBlankRestY : BigSpinRestY   : SmallRestY;

    alltweens[index]?.Kill();
    slotTransform.localPosition = new Vector2(slotTransform.localPosition.x, SpinTopY);
    alltweens[index] = slotTransform.DOLocalMoveY(RestY, DurationFor(SpinTopY, RestY)).SetEase(Ease.OutBack, 0.9f);
  }

  private void KillAllTweens()
  {
    for (int i = 0; i < alltweens.Length; i++)
    {
      alltweens[i]?.Kill();
      alltweens[i] = null;
    }
  }
  #endregion
}

[Serializable]
public class SlotImage
{
  public List<SlotIconView> slotImages = new List<SlotIconView>(10);
}

[Serializable]
public class SymbolWinAnim
{
  public int id;
  public List<Sprite> syncedSprites = new List<Sprite>(); // first synced pass frames
  public float syncedSpeed = 5f;
  public List<Sprite> loopSprites = new List<Sprite>();    // single/multiline loop pass frames
  public float loopSpeed = 5f;
  public bool loopUsesPulse = false;                       // gold bars (id 0,1): pulse instead of frames on loop
}
