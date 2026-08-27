using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class MoneyBagController : MonoBehaviour
{
    [Header("UI Elements (Length should be 3)")]
    [SerializeField] private List<Button> moneyBagButtons;
    [SerializeField] private List<TMP_Text> rewardTexts;
    [SerializeField] private List<GameObject> coinPiles;
    [SerializeField] private List<GameObject> coinFountainAnimObjects;
    
    [Header("Titles")]
    [SerializeField] private List<GameObject> titleObjects;
    [SerializeField] private GameObject congratulationObject;
    
    [Header("Settings")]
    [SerializeField] private float popDuration = 0.5f;
    [SerializeField] private float textMoveDuration = 1.0f;
    [SerializeField] private float fountainAnimDuration = 1.0f;
    [SerializeField] private Vector3 winTextTargetPos = new Vector3(0, -100, 0);

    private MoneyBagResultData currentResultData;
    private Action onCompleteCallback;
    private bool hasPicked;
    private List<Vector3> originalTextPositions = new List<Vector3>();

    private void Awake()
    {
        // Store original positions of reward texts so they can be reset
        for (int i = 0; i < rewardTexts.Count; i++)
        {
            var text = rewardTexts[i];
            if (text != null)
            {
                originalTextPositions.Add(text.rectTransform.localPosition);
                text.gameObject.SetActive(false); // Hide initially
            }
            else
            {
                originalTextPositions.Add(Vector3.zero);
            }
            
            if (i < coinPiles.Count && coinPiles[i] != null)
            {
                coinPiles[i].SetActive(false); // Hide initially
            }
        }

        if (coinFountainAnimObjects != null)
        {
            foreach (var fountainObj in coinFountainAnimObjects)
            {
                if (fountainObj != null) fountainObj.SetActive(false);
            }
        }

        for (int i = 0; i < moneyBagButtons.Count; i++)
        {
            int index = i; // capture for closure
            if (moneyBagButtons[i] != null)
            {
                moneyBagButtons[i].onClick.AddListener(() => OnMoneyBagClicked(index));
            }
        }
        
        // Hide panel at start
        gameObject.SetActive(false);
    }

    public void StartMoneyBagBonus(MoneyBagResultData resultData, Action onComplete)
    {
        currentResultData = resultData;
        onCompleteCallback = onComplete;
        hasPicked = false;

        // Reset Titles
        if (titleObjects != null)
        {
            foreach (var title in titleObjects)
            {
                if (title != null) title.SetActive(true);
            }
        }
        if (congratulationObject != null) congratulationObject.SetActive(false);

        // Reset UI
        for (int i = 0; i < 3; i++)
        {
            if (i < moneyBagButtons.Count && moneyBagButtons[i] != null) 
            {
                moneyBagButtons[i].enabled = true;
                moneyBagButtons[i].interactable = true;
                moneyBagButtons[i].gameObject.SetActive(true);
                moneyBagButtons[i].transform.localScale = Vector3.one;
            }
            if (i < coinPiles.Count && coinPiles[i] != null)
            {
                coinPiles[i].SetActive(false);
                coinPiles[i].transform.localScale = Vector3.zero;
            }
            if (i < rewardTexts.Count && rewardTexts[i] != null)
            {
                rewardTexts[i].gameObject.SetActive(false);
                rewardTexts[i].rectTransform.localPosition = originalTextPositions[i];
                rewardTexts[i].transform.localScale = Vector3.zero;
            }
            if (coinFountainAnimObjects != null && i < coinFountainAnimObjects.Count && coinFountainAnimObjects[i] != null)
            {
                coinFountainAnimObjects[i].SetActive(false);
            }
        }
        
        gameObject.SetActive(true);
        AudioManager.Instance?.PlayFeatureOpenLoop();
    }

    private void OnMoneyBagClicked(int index)
    {
        if (hasPicked) return;
        hasPicked = true;

        // Toggle Titles
        if (titleObjects != null)
        {
            foreach (var title in titleObjects)
            {
                if (title != null) title.SetActive(false);
            }
        }
        if (congratulationObject != null) congratulationObject.SetActive(true);

        // Disable all buttons to prevent double clicking but keep normal appearance
        foreach (var btn in moneyBagButtons)
        {
            if (btn != null) btn.enabled = false;
        }

        StartCoroutine(ProcessPickSequence(index));
    }

    private IEnumerator ProcessPickSequence(int pickedUiIndex)
    {
        AudioManager.Instance?.PlayButton(); // Or a special pick sound

        // 1. Pop the picked bag
        yield return StartCoroutine(PopBag(pickedUiIndex, currentResultData.winInCash, true));

        // 2. Move win text to target pos
        if (pickedUiIndex < rewardTexts.Count && rewardTexts[pickedUiIndex] != null)
        {
            RectTransform textRect = rewardTexts[pickedUiIndex].rectTransform;
            textRect.DOLocalMove(winTextTargetPos, textMoveDuration).SetEase(Ease.InOutQuad);
            yield return new WaitForSeconds(textMoveDuration + 0.5f);
        }

        // 3. Pop the other two (dummy) bags automatically
        double ratio = currentResultData.creditsAwarded > 0 ? (currentResultData.winInCash / currentResultData.creditsAwarded) : 0.01;
        
        int revealIndex = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == pickedUiIndex) continue;

            // Always use random +- 1 or 2 credits
            int offset = UnityEngine.Random.Range(1, 3);
            if (UnityEngine.Random.value > 0.5f) offset = -offset;
            
            int dummyCredit = currentResultData.creditsAwarded + offset;
            if (dummyCredit <= 0) dummyCredit = 1;
            
            double dummyValue = dummyCredit * ratio;

            StartCoroutine(PopBag(i, dummyValue, false));
        }

        // Wait for dummy bags to pop
        yield return new WaitForSeconds(popDuration + 1.5f);

        // Notify bonus pick sequence complete (UIManager will disable panel during black film transition)
        AudioManager.Instance?.StopFeatureOpenLoop();
        onCompleteCallback?.Invoke();
    }

    private IEnumerator PopBag(int index, double cashValue, bool isPicked)
    {
        if (index >= moneyBagButtons.Count || index >= coinPiles.Count || index >= rewardTexts.Count) yield break;

        GameObject mainBag = moneyBagButtons[index] != null ? moneyBagButtons[index].gameObject : null;
        GameObject coinPile = coinPiles[index];
        TMP_Text rewardText = rewardTexts[index];

        if (coinFountainAnimObjects != null && index < coinFountainAnimObjects.Count && coinFountainAnimObjects[index] != null)
        {
            StartCoroutine(PlayCoinFountainAnimation(index));
        }

        if (mainBag != null)
        {
            mainBag.transform.DOScale(Vector3.zero, popDuration / 2f).SetEase(Ease.InBack);
        }
        
        yield return new WaitForSeconds(popDuration / 2f);
        
        if (mainBag != null) mainBag.SetActive(false);

        if (coinPile != null)
        {
            coinPile.SetActive(true);
            coinPile.transform.localScale = Vector3.zero;
            coinPile.transform.DOScale(Vector3.one, popDuration / 2f).SetEase(Ease.OutBack);
        }

        if (rewardText != null)
        {
            rewardText.gameObject.SetActive(true);
            rewardText.text = cashValue.ToString("0.###");
            rewardText.color = new Color(rewardText.color.r, rewardText.color.g, rewardText.color.b, 1f);

            rewardText.transform.localScale = Vector3.zero;
            rewardText.transform.DOScale(Vector3.one, popDuration / 2f).SetEase(Ease.OutBack);
        }
    }

    private IEnumerator PlayCoinFountainAnimation(int index)
    {
        if (coinFountainAnimObjects == null || index < 0 || index >= coinFountainAnimObjects.Count) yield break;

        GameObject fountainObj = coinFountainAnimObjects[index];
        if (fountainObj == null) yield break;

        fountainObj.SetActive(true);

        float waitTime = fountainAnimDuration;

        Animator animator = fountainObj.GetComponent<Animator>();
        if (animator == null) animator = fountainObj.GetComponentInChildren<Animator>();

        if (animator != null)
        {
            animator.Play(0, -1, 0f);
            yield return null;
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.length > 0)
            {
                waitTime = stateInfo.length;
            }
        }
        else
        {
            Animation anim = fountainObj.GetComponent<Animation>();
            if (anim == null) anim = fountainObj.GetComponentInChildren<Animation>();
            if (anim != null && anim.clip != null)
            {
                anim.Play();
                waitTime = anim.clip.length;
            }
        }

        yield return new WaitForSeconds(waitTime);

        if (fountainObj != null)
        {
            fountainObj.SetActive(false);
        }
    }
}
