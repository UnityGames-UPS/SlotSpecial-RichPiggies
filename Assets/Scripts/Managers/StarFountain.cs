using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;

public class StarFountain : MonoBehaviour
{
    [Header("Star Fountain Settings")]
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private Transform starSpawnContainer;
    [SerializeField] private int starPoolSize = 200;

    [Header("Behavior Settings")]
    [Tooltip("If true, items maintain initial scale and alpha throughout travel (fade variation 0-10% max). Useful for coin fountains.")]
    [SerializeField] private bool disableEndFadeAndScale = false;

    private List<GameObject> starPool = new List<GameObject>();
    private Coroutine starBurstCoroutine;

    private void Start()
    {
        InitializeStarPool();
    }

    private void InitializeStarPool()
    {
        if (starPrefab == null) return;
        Transform parentContainer = starSpawnContainer != null ? starSpawnContainer : transform;

        for (int i = 0; i < starPoolSize; i++)
        {
            GameObject star = Instantiate(starPrefab, parentContainer);
            star.SetActive(false);
            starPool.Add(star);
        }
    }

    private GameObject GetPooledStar()
    {
        for (int i = 0; i < starPool.Count; i++)
        {
            if (starPool[i] != null && !starPool[i].activeSelf)
            {
                return starPool[i];
            }
        }

        Transform parentContainer = starSpawnContainer != null ? starSpawnContainer : transform;
        if (starPrefab != null)
        {
            GameObject star = Instantiate(starPrefab, parentContainer);
            star.SetActive(false);
            starPool.Add(star);
            return star;
        }

        return null;
    }

    internal void PlayStarBurst()
    {
        StopStarBurst();
        if (starPrefab == null) return;

        // Pre-warm stars mid-flight so star container is densely populated immediately on pop open
        int prewarmCount = Random.Range(25, 40);
        for (int i = 0; i < prewarmCount; i++)
        {
            float initialProgress = Random.Range(0.10f, 0.85f);
            PrewarmSingleBurstStar(initialProgress);
        }

        starBurstCoroutine = StartCoroutine(StarBurstRoutine());
    }

    internal void StopStarBurst()
    {
        if (starBurstCoroutine != null)
        {
            StopCoroutine(starBurstCoroutine);
            starBurstCoroutine = null;
        }

        for (int i = 0; i < starPool.Count; i++)
        {
            if (starPool[i] != null)
            {
                RecycleStar(starPool[i]);
            }
        }
    }

    private void RecycleStar(GameObject star)
    {
        if (star == null) return;

        DOTween.Kill(star);
        DOTween.Kill(star.transform);
        RectTransform rt = star.GetComponent<RectTransform>();
        if (rt != null) DOTween.Kill(rt);

        star.SetActive(false);

        CanvasGroup cg = star.GetComponent<CanvasGroup>();
        Image img = star.GetComponent<Image>();
        if (cg != null) cg.alpha = 0f;
        if (img != null)
        {
            Color c = img.color;
            c.a = 0f;
            img.color = c;
        }

        if (rt != null) rt.anchoredPosition = Vector2.zero;
        else star.transform.localPosition = Vector3.zero;
    }

    private IEnumerator StarBurstRoutine()
    {
        while (gameObject.activeInHierarchy)
        {
            int burstCount = Random.Range(2, 4);
            for (int i = 0; i < burstCount; i++)
            {
                SpawnSingleBurstStar();
            }
            yield return new WaitForSeconds(Random.Range(0.04f, 0.07f));
        }
    }

    private void PrewarmSingleBurstStar(float progress)
    {
        GameObject star = GetPooledStar();
        if (star == null) return;

        RecycleStar(star);

        Vector2 startOffset = Random.insideUnitCircle * 25f;

        float randomScale = Random.Range(0.3f, 1.2f);
        star.transform.localScale = Vector3.one * randomScale;

        float randomRotation = Random.Range(0f, 360f);

        float width = 800f;
        float height = 600f;
        Transform container = starSpawnContainer != null ? starSpawnContainer : transform;
        RectTransform containerRect = container.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            if (containerRect.rect.width > 0) width = containerRect.rect.width;
            if (containerRect.rect.height > 0) height = containerRect.rect.height;
        }

        float hw = width * 0.5f;
        float hh = height * 0.5f;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        float tx = Mathf.Abs(cos) > 0.0001f ? hw / Mathf.Abs(cos) : float.MaxValue;
        float ty = Mathf.Abs(sin) > 0.0001f ? hh / Mathf.Abs(sin) : float.MaxValue;
        float distanceToBorder = Mathf.Min(tx, ty);

        Vector2 targetPos = new Vector2(cos * distanceToBorder, sin * distanceToBorder);

        float totalDuration = Random.Range(1.4f, 2.2f);
        float remainingDuration = totalDuration * (1f - progress);

        Vector2 currentPos = Vector2.Lerp(startOffset, targetPos, progress);
        float currentRotation = randomRotation + (Random.Range(-90f, 90f) * progress);

        RectTransform starRect = star.GetComponent<RectTransform>();
        if (starRect != null)
        {
            starRect.anchoredPosition = currentPos;
        }
        else
        {
            star.transform.localPosition = currentPos;
        }
        star.transform.localRotation = Quaternion.Euler(0f, 0f, currentRotation);

        CanvasGroup cg = star.GetComponent<CanvasGroup>();
        Image img = star.GetComponent<Image>();

        // For coins (disableEndFadeAndScale), alpha variation is 0-10% max (0.9 to 1.0) and stays constant
        float startAlpha = disableEndFadeAndScale ? Random.Range(0.9f, 1.0f) : (1f - progress);
        if (cg != null) cg.alpha = startAlpha;
        if (img != null)
        {
            Color c = img.color;
            c.a = startAlpha;
            img.color = c;
        }

        star.SetActive(true);

        if (!disableEndFadeAndScale)
        {
            if (cg != null) cg.DOFade(0f, remainingDuration).SetEase(Ease.Linear);
            else if (img != null) img.DOFade(0f, remainingDuration).SetEase(Ease.Linear);
        }

        Sequence starSeq = DOTween.Sequence();
        if (starRect != null)
        {
            starSeq.Join(starRect.DOAnchorPos(targetPos, remainingDuration).From(currentPos).SetEase(Ease.Linear));
        }
        else
        {
            starSeq.Join(star.transform.DOLocalMove(targetPos, remainingDuration).From(currentPos).SetEase(Ease.Linear));
        }

        float extraRot = Random.Range(-90f, 90f) * (1f - progress);
        starSeq.Join(star.transform.DORotate(new Vector3(0, 0, currentRotation + extraRot), remainingDuration, RotateMode.FastBeyond360));

        if (!disableEndFadeAndScale)
        {
            starSeq.Join(star.transform.DOScale(randomScale * 0.4f, remainingDuration * 0.4f).SetDelay(remainingDuration * 0.6f));
        }

        starSeq.OnComplete(() =>
        {
            RecycleStar(star);
        });
    }

    private void SpawnSingleBurstStar()
    {
        GameObject star = GetPooledStar();
        if (star == null) return;

        RecycleStar(star);

        Vector2 startOffset = Random.insideUnitCircle * 25f;
        RectTransform starRect = star.GetComponent<RectTransform>();
        if (starRect != null)
        {
            starRect.anchoredPosition = startOffset;
        }
        else
        {
            star.transform.localPosition = startOffset;
        }

        float randomScale = Random.Range(0.3f, 1.2f);
        star.transform.localScale = Vector3.one * randomScale;

        float randomRotation = Random.Range(0f, 360f);
        star.transform.localRotation = Quaternion.Euler(0f, 0f, randomRotation);

        CanvasGroup cg = star.GetComponent<CanvasGroup>();
        Image img = star.GetComponent<Image>();

        // For coins (disableEndFadeAndScale), alpha variation is 0-10% max (0.9 to 1.0) and stays constant
        float startAlpha = disableEndFadeAndScale ? Random.Range(0.9f, 1.0f) : 1f;

        if (cg != null) cg.alpha = startAlpha;
        if (img != null)
        {
            Color c = img.color;
            c.a = startAlpha;
            img.color = c;
        }

        float width = 800f;
        float height = 600f;
        Transform container = starSpawnContainer != null ? starSpawnContainer : transform;
        RectTransform containerRect = container.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            if (containerRect.rect.width > 0) width = containerRect.rect.width;
            if (containerRect.rect.height > 0) height = containerRect.rect.height;
        }

        float hw = width * 0.5f;
        float hh = height * 0.5f;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        float tx = Mathf.Abs(cos) > 0.0001f ? hw / Mathf.Abs(cos) : float.MaxValue;
        float ty = Mathf.Abs(sin) > 0.0001f ? hh / Mathf.Abs(sin) : float.MaxValue;
        float distanceToBorder = Mathf.Min(tx, ty);

        Vector2 targetPos = new Vector2(cos * distanceToBorder, sin * distanceToBorder);

        float animDuration = Random.Range(1.4f, 2.2f);

        star.SetActive(true);

        if (!disableEndFadeAndScale)
        {
            if (cg != null) cg.DOFade(0f, animDuration).SetEase(Ease.Linear);
            else if (img != null) img.DOFade(0f, animDuration).SetEase(Ease.Linear);
        }

        Sequence starSeq = DOTween.Sequence();
        if (starRect != null)
        {
            starSeq.Join(starRect.DOAnchorPos(targetPos, animDuration).From(startOffset).SetEase(Ease.Linear));
        }
        else
        {
            starSeq.Join(star.transform.DOLocalMove(targetPos, animDuration).From(startOffset).SetEase(Ease.Linear));
        }

        float extraRot = Random.Range(-90f, 90f);
        starSeq.Join(star.transform.DORotate(new Vector3(0, 0, randomRotation + extraRot), animDuration, RotateMode.FastBeyond360));

        if (!disableEndFadeAndScale)
        {
            starSeq.Join(star.transform.DOScale(randomScale * 0.4f, animDuration * 0.4f).SetDelay(animDuration * 0.6f));
        }

        starSeq.OnComplete(() =>
        {
            RecycleStar(star);
        });
    }

    private void OnDisable()
    {
        StopStarBurst();
    }
}
