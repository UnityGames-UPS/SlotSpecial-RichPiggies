using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class PopupManager : MonoBehaviour
{
    [Header("Popup Parent")]
    [SerializeField] private GameObject popupParent;

    [Header("1. Disconnection Popup")]
    [SerializeField] private GameObject disconnectionPopup;
    [SerializeField] private RectTransform disconnectionPopupRect;
    [SerializeField] private TextMeshProUGUI disconnectionMessageText;
    [SerializeField] private Button disconnectionOkButton;

    [Header("2. Error Popup")]
    [SerializeField] private GameObject errorPopup;
    [SerializeField] private RectTransform errorPopupRect;
    [SerializeField] private TextMeshProUGUI errorTitleText;
    [SerializeField] private TextMeshProUGUI errorMessageText;
    [SerializeField] private Button errorOkButton;
    [SerializeField] private TextMeshProUGUI errorOkButtonText;

    [Header("3. Reconnection Popup")]
    [SerializeField] private GameObject reconnectionPopup;
    [SerializeField] private RectTransform reconnectionPopupRect;
    [SerializeField] private Image reconnectionRotatingImage;
    [SerializeField] private TextMeshProUGUI reconnectionTryText;

    [Header("4. Loading Popup")]
    [SerializeField] private GameObject loadingPopup;
    [SerializeField] private RectTransform loadingPopupRect;
    [SerializeField] private Image loadingRotatingImage;
    [SerializeField] private TextMeshProUGUI loadingAnimatedText;

    [Header("5. Exit Game Popup")]
    [SerializeField] private GameObject exitGamePopup;
    [SerializeField] private RectTransform exitGamePopupRect;
    [SerializeField] private Button exitGameYesButton;
    [SerializeField] private Button exitGameNoButton;

    [Header("Animation Settings")]
    [SerializeField] private float popupScaleInDuration = 0.3f;
    [SerializeField] private float popupScaleOutDuration = 0.2f;
    [SerializeField] private float rotationSpeed = 360f; // Degrees per second
    [SerializeField] private float loadingTextCycleSpeed = 0.5f; // Seconds per dot change
    [SerializeField] private float defaultLoadingDuration = 5f;
    [SerializeField] private float minLoadingDuration = 0.8f;

    [Header("References")]
    [SerializeField] private GameManager gameManager;

    // Current popup state
    private GameObject currentActivePopup;
    private Coroutine rotationCoroutine;
    private Coroutine loadingTextCoroutine;
    private Coroutine autoCloseCoroutine;
    private Coroutine delayedCloseCoroutine;
    private float loadingStartTime;
    private System.Action onLoadingClosed;

    // Error popup state
    private bool isErrorCritical = false;

    #region Initialization

    private void Awake()
    {
        SetupButtons();
        HideAllPopups();

        if (popupParent != null)
        {
            popupParent.SetActive(false);
        }
    }

    private void SetupButtons()
    {
        if (disconnectionOkButton != null)
        {
            disconnectionOkButton.onClick.AddListener(OnDisconnectionOkClicked);
        }

        if (errorOkButton != null)
        {
            errorOkButton.onClick.AddListener(OnErrorOkClicked);
        }

        if (exitGameYesButton != null)
        {
            exitGameYesButton.onClick.AddListener(OnExitGameYesClicked);
        }

        if (exitGameNoButton != null)
        {
            exitGameNoButton.onClick.AddListener(OnExitGameNoClicked);
        }
    }

    private void HideAllPopups()
    {
        if (disconnectionPopup != null) disconnectionPopup.SetActive(false);
        if (errorPopup != null) errorPopup.SetActive(false);
        if (reconnectionPopup != null) reconnectionPopup.SetActive(false);
        if (loadingPopup != null) loadingPopup.SetActive(false);
        if (exitGamePopup != null) exitGamePopup.SetActive(false);
    }

    #endregion

    #region 1. Disconnection Popup

    /// <summary>
    /// Show disconnection popup - triggered when max ping attempts fail
    /// </summary>
    internal void ShowDisconnectionPopup()
    {
        ShowDisconnectionPopup("Game disconnected due to network error. Please relaunch the game.");
    }

    /// <summary>
    /// Show disconnection popup with custom message
    /// </summary>
    internal void ShowDisconnectionPopup(string message)
    {
        if (disconnectionPopup == null) return;

        CloseCurrentPopup();

        if (popupParent != null) popupParent.SetActive(true);

        if (disconnectionMessageText != null)
        {
            disconnectionMessageText.text = message;
        }

        currentActivePopup = disconnectionPopup;
        disconnectionPopup.SetActive(true);

        AnimatePopupOpen(disconnectionPopupRect);
    }

    private void OnDisconnectionOkClicked()
    {
        AnimatePopupClose(disconnectionPopupRect, () =>
        {
            disconnectionPopup.SetActive(false);
            if (currentActivePopup == disconnectionPopup) currentActivePopup = null;
            UpdatePopupParentState();
            ExitGame();
        });
    }

    #endregion

    #region 2. Error Popup

    /// <summary>
    /// Show error popup for insufficient funds
    /// </summary>
    internal void ShowInsufficientFundsError()
    {
        ShowErrorPopup("Information", "Insufficient balance. Please add funds to continue.", false);
    }

    /// <summary>
    /// Show error popup for another device detected
    /// </summary>
    internal void ShowAnotherDeviceError()
    {
        ShowErrorPopup("Warning", "Your account has been logged in from another device. This session will be closed.", true);
    }

    /// <summary>
    /// Show error popup for session expired
    /// </summary>
    internal void ShowSessionExpiredError()
    {
        ShowErrorPopup("Warning", "Your session has expired. Please log in again.", true);
    }

    /// <summary>
    /// Show error popup for invalid auth token
    /// </summary>
    internal void ShowInvalidAuthError()
    {
        ShowErrorPopup("Warning", "Invalid authentication token. Please log in again.", true);
    }

    /// <summary>
    /// Show error popup for server error
    /// </summary>
    internal void ShowServerError(string message = "A server error occurred. Please try again later.")
    {
        ShowErrorPopup("Server Error", message, true);
    }

    /// <summary>
    /// Generic error popup
    /// </summary>
    /// <param name="title">Error title (Information, Warning, Server Error)</param>
    /// <param name="message">Error message</param>
    /// <param name="isCritical">If true, OK button will exit the game</param>
    internal void ShowErrorPopup(string title, string message, bool isCritical)
    {
        if (errorPopup == null) return;

        CloseCurrentPopup();

        if (popupParent != null) popupParent.SetActive(true);

        if (errorTitleText != null)
        {
            errorTitleText.text = title;
        }

        if (errorMessageText != null)
        {
            errorMessageText.text = message;
        }
        if (errorOkButtonText != null)
        {
            errorOkButtonText.text = isCritical ? "Exit Game" : "OKAY";
        }

        isErrorCritical = isCritical;

        currentActivePopup = errorPopup;
        errorPopup.SetActive(true);

        AnimatePopupOpen(errorPopupRect);
    }

    private void OnErrorOkClicked()
    {
        AnimatePopupClose(errorPopupRect, () =>
        {
            errorPopup.SetActive(false);
            if (currentActivePopup == errorPopup) currentActivePopup = null;
            UpdatePopupParentState();

            if (isErrorCritical)
            {
                ExitGame();
            }
        });
    }

    #endregion

    #region 3. Reconnection Popup

    /// <summary>
    /// Show reconnection popup with current retry count
    /// </summary>
    /// <param name="currentTry">Current retry attempt (1-based)</param>
    /// <param name="maxTries">Maximum retry attempts</param>
    internal void ShowReconnectionPopup(int currentTry, int maxTries)
    {
        if (reconnectionPopup == null) return;

        if (popupParent != null) popupParent.SetActive(true);

        // Don't close other popups for reconnection - it's an overlay
        if (currentActivePopup != reconnectionPopup)
        {
            if (reconnectionTryText != null)
            {
                reconnectionTryText.text = $"{currentTry} / {maxTries}";
            }

            currentActivePopup = reconnectionPopup;
            reconnectionPopup.SetActive(true);

            AnimatePopupOpen(reconnectionPopupRect);
            StartRotation(reconnectionRotatingImage);
        }
        else
        {
            // Just update the text if already showing
            if (reconnectionTryText != null)
            {
                reconnectionTryText.text = $"{currentTry} / {maxTries}";
            }
        }
    }

    /// <summary>
    /// Close reconnection popup (called when reconnection succeeds)
    /// </summary>
    internal void CloseReconnectionPopup()
    {
        if (reconnectionPopup == null || !reconnectionPopup.activeSelf) return;

        StopRotation();

        AnimatePopupClose(reconnectionPopupRect, () =>
        {
            reconnectionPopup.SetActive(false);
            if (currentActivePopup == reconnectionPopup)
            {
                currentActivePopup = null;
            }
            UpdatePopupParentState();
        });
    }

    #endregion

    #region 4. Loading Popup

    /// <summary>
    /// Show loading popup with default 5 second duration
    /// </summary>
    internal void ShowLoadingPopup()
    {
        ShowLoadingPopup(defaultLoadingDuration);
    }

    /// <summary>
    /// Show loading popup with custom duration
    /// </summary>
    /// <param name="duration">Duration in seconds (0 = indefinite until manually closed)</param>
    /// <summary>
    /// Show loading popup with optional auto-close duration
    /// </summary>
    /// <param name="duration">Duration in seconds (-1 = use defaultLoadingDuration, 0 = indefinite)</param>
    internal void ShowLoadingPopup(float duration = -1f)
    {
        if (loadingPopup == null) return;

        // Use default if -1
        if (duration < 0) duration = defaultLoadingDuration;

        CloseCurrentPopup();

        // Stop any pending delayed close
        if (delayedCloseCoroutine != null)
        {
            StopCoroutine(delayedCloseCoroutine);
            delayedCloseCoroutine = null;
        }

        if (popupParent != null) popupParent.SetActive(true);

        currentActivePopup = loadingPopup;
        loadingPopup.SetActive(true);
        loadingStartTime = Time.time;

        AnimatePopupOpen(loadingPopupRect);
        StartRotation(loadingRotatingImage);
        StartLoadingTextAnimation();

        // Auto-close after duration if > 0
        if (duration > 0)
        {
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
            }
            autoCloseCoroutine = StartCoroutine(AutoCloseLoadingPopup(duration));
        }
    }

    /// <summary>
    /// Close loading popup manually
    /// </summary>
    /// <summary>
    /// Close loading popup manually, respecting minimum duration
    /// </summary>
    internal void CloseLoadingPopup(System.Action onComplete = null)
    {
        if (onComplete != null) onLoadingClosed += onComplete;

        if (loadingPopup == null || !loadingPopup.activeSelf)
        {
            onLoadingClosed?.Invoke();
            onLoadingClosed = null;
            return;
        }

        float elapsed = Time.time - loadingStartTime;
        if (elapsed < minLoadingDuration)
        {
            if (delayedCloseCoroutine == null)
            {
                delayedCloseCoroutine = StartCoroutine(CloseLoadingAfterDelay(minLoadingDuration - elapsed));
            }
            return;
        }

        PerformCloseLoadingPopup();
    }

    private IEnumerator CloseLoadingAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        delayedCloseCoroutine = null;
        PerformCloseLoadingPopup();
    }

    private void PerformCloseLoadingPopup()
    {
        if (loadingPopup == null || !loadingPopup.activeSelf)
        {
            onLoadingClosed?.Invoke();
            onLoadingClosed = null;
            return;
        }

        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        StopRotation();
        StopLoadingTextAnimation();

        AnimatePopupClose(loadingPopupRect, () =>
        {
            loadingPopup.SetActive(false);
            if (currentActivePopup == loadingPopup)
            {
                currentActivePopup = null;
            }
            UpdatePopupParentState();

            // Invoke and clear callbacks
            onLoadingClosed?.Invoke();
            onLoadingClosed = null;
        });
    }

    private IEnumerator AutoCloseLoadingPopup(float duration)
    {
        yield return new WaitForSeconds(duration);
        CloseLoadingPopup();
        autoCloseCoroutine = null;
    }

    #endregion

    #region 5. Exit Game Popup

    /// <summary>
    /// Show game exit confirmation popup
    /// </summary>
    internal void ShowExitGamePopup()
    {
        if (exitGamePopup == null) return;

        CloseCurrentPopup();

        if (popupParent != null) popupParent.SetActive(true);

        currentActivePopup = exitGamePopup;
        exitGamePopup.SetActive(true);

        AnimatePopupOpen(exitGamePopupRect);
    }

    private void OnExitGameYesClicked()
    {
        AnimatePopupClose(exitGamePopupRect, () =>
        {
            exitGamePopup.SetActive(false);
            if (currentActivePopup == exitGamePopup) currentActivePopup = null;
            UpdatePopupParentState();
            ExitGame();
        });
    }

    private void OnExitGameNoClicked()
    {
        AnimatePopupClose(exitGamePopupRect, () =>
        {
            exitGamePopup.SetActive(false);
            if (currentActivePopup == exitGamePopup) currentActivePopup = null;
            UpdatePopupParentState();
        });
    }

    #endregion

    #region Animation Helpers

    /// <summary>
    /// Animate popup opening with scale effect
    /// </summary>
    private void AnimatePopupOpen(RectTransform popupRect)
    {
        AudioManager.Instance?.PlayPopupOpenClose();
        if (popupRect == null) return;

        popupRect.localScale = Vector3.zero;
        popupRect.DOScale(1f, popupScaleInDuration).SetEase(Ease.OutBack);
    }

    /// <summary>
    /// Animate popup closing with scale effect
    /// </summary>
    private void AnimatePopupClose(RectTransform popupRect, System.Action onComplete)
    {
        AudioManager.Instance?.PlayPopupOpenClose();
        if (popupRect == null)
        {
            onComplete?.Invoke();
            return;
        }

        Sequence closeSeq = DOTween.Sequence();
        closeSeq.Append(popupRect.DOScale(1.1f, 0.1f));
        closeSeq.Append(popupRect.DOScale(0f, popupScaleOutDuration).SetEase(Ease.InBack));
        closeSeq.OnComplete(() =>
        {
            popupRect.localScale = Vector3.one;
            onComplete?.Invoke();
        });
    }

    /// <summary>
    /// Start continuous clockwise rotation for loading/reconnection images
    /// </summary>
    private void StartRotation(Image targetImage)
    {
        if (targetImage == null) return;

        StopRotation();

        rotationCoroutine = StartCoroutine(RotateImageCoroutine(targetImage));
    }

    /// <summary>
    /// Stop rotation coroutine
    /// </summary>
    private void StopRotation()
    {
        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
            rotationCoroutine = null;
        }
    }

    /// <summary>
    /// Rotate image continuously clockwise
    /// </summary>
    private IEnumerator RotateImageCoroutine(Image targetImage)
    {
        if (targetImage == null) yield break;

        while (true)
        {
            targetImage.transform.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
            yield return null;
        }
    }

    /// <summary>
    /// Start loading text animation (. -> .. -> ... loop)
    /// </summary>
    private void StartLoadingTextAnimation()
    {
        if (loadingAnimatedText == null) return;

        StopLoadingTextAnimation();

        loadingTextCoroutine = StartCoroutine(LoadingTextCoroutine());
    }

    /// <summary>
    /// Stop loading text animation
    /// </summary>
    private void StopLoadingTextAnimation()
    {
        if (loadingTextCoroutine != null)
        {
            StopCoroutine(loadingTextCoroutine);
            loadingTextCoroutine = null;
        }
    }

    /// <summary>
    /// Cycle through loading dots: . -> .. -> ...
    /// </summary>
    private IEnumerator LoadingTextCoroutine()
    {
        string[] dotStates = { ".", "..", "..." };
        int currentIndex = 0;

        while (true)
        {
            if (loadingAnimatedText != null)
            {
                loadingAnimatedText.text = dotStates[currentIndex];
                currentIndex = (currentIndex + 1) % dotStates.Length;
            }

            yield return new WaitForSeconds(loadingTextCycleSpeed);
        }
    }

    #endregion

    #region Popup Management

    private void UpdatePopupParentState()
    {
        if (popupParent != null)
        {
            bool anyActive = (disconnectionPopup != null && disconnectionPopup.activeSelf) ||
                             (errorPopup != null && errorPopup.activeSelf) ||
                             (reconnectionPopup != null && reconnectionPopup.activeSelf) ||
                             (loadingPopup != null && loadingPopup.activeSelf) ||
                             (exitGamePopup != null && exitGamePopup.activeSelf);

            popupParent.SetActive(anyActive);
        }
    }

    /// <summary>
    /// Close the currently active popup (if any)
    /// </summary>
    private void CloseCurrentPopup()
    {
        if (currentActivePopup == null) return;

        StopRotation();
        StopLoadingTextAnimation();

        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        currentActivePopup.SetActive(false);
        currentActivePopup = null;
        UpdatePopupParentState();
    }

    /// <summary>
    /// Check if any popup is currently active
    /// </summary>
    internal bool IsAnyPopupActive()
    {
        return currentActivePopup != null && currentActivePopup.activeSelf;
    }

    /// <summary>
    /// Check if the loading popup is currently active
    /// </summary>
    internal bool IsLoadingPopupActive()
    {
        return loadingPopup != null && loadingPopup.activeSelf;
    }

    /// <summary>
    /// Force close all popups (emergency cleanup)
    /// </summary>
    internal void ForceCloseAllPopups()
    {
        StopRotation();
        StopLoadingTextAnimation();

        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        HideAllPopups();
        currentActivePopup = null;
        UpdatePopupParentState();
    }

    #endregion

    #region Game Control

    /// <summary>
    /// Exit the game
    /// </summary>
    private void ExitGame()
    {
        if (gameManager != null)
        {
            gameManager.ExitGame();
        }

    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        StopRotation();
        StopLoadingTextAnimation();

        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
        }

        if (disconnectionPopupRect != null) DOTween.Kill(disconnectionPopupRect);
        if (errorPopupRect != null) DOTween.Kill(errorPopupRect);
        if (reconnectionPopupRect != null) DOTween.Kill(reconnectionPopupRect);
        if (loadingPopupRect != null) DOTween.Kill(loadingPopupRect);
        if (exitGamePopupRect != null) DOTween.Kill(exitGamePopupRect);
    }

    #endregion
}
