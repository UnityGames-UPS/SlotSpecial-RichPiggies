using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

public class SpinButtonHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Hold Settings")]
    [Tooltip("How long to hold the button before the hold event triggers")]
    public float holdDuration = 3.0f;
    
    [Header("Events")]
    public UnityEvent OnClick;
    public UnityEvent OnHoldThreeSeconds;
    
    private bool isPointerDown;
    private bool holdTriggered;
    private Coroutine holdCoroutine;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        
        isPointerDown = true;
        holdTriggered = false;
        holdCoroutine = StartCoroutine(HoldCheckRoutine());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPointerDown) return;
        
        isPointerDown = false;
        if (holdCoroutine != null)
        {
            StopCoroutine(holdCoroutine);
            holdCoroutine = null;
        }

        if (!holdTriggered && (button == null || button.interactable))
        {
            OnClick?.Invoke();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Cancel the hold if the player moves the mouse/finger off the button
        if (!isPointerDown) return;
        
        isPointerDown = false;
        if (holdCoroutine != null)
        {
            StopCoroutine(holdCoroutine);
            holdCoroutine = null;
        }
    }

    private IEnumerator HoldCheckRoutine()
    {
        yield return new WaitForSeconds(holdDuration);
        
        if (isPointerDown)
        {
            holdTriggered = true;
            OnHoldThreeSeconds?.Invoke();
        }
        
        holdCoroutine = null;
    }
    
    private void OnDisable()
    {
        isPointerDown = false;
        if (holdCoroutine != null)
        {
            StopCoroutine(holdCoroutine);
            holdCoroutine = null;
        }
    }
}
