using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PopupManager popupManager;
    [SerializeField] private JSFunctCalls jsFunctCalls;

    [Header("Loading & Intro")]
    [SerializeField] private GameObject gameScreen;
    [SerializeField] private GameObject gameLogoObject;



    [Header("Bet Controls")]
    [SerializeField] private TMP_Text betAmountText;
    [SerializeField] private Button betPlusButton;
    [SerializeField] private Button betMinusButton;
    [Header("Bet Controls - Portrait")]
    [SerializeField] private TMP_Text betAmountTextPortrait;
    [SerializeField] private Button betPlusButtonPortrait;
    [SerializeField] private Button betMinusButtonPortrait;

    [Header("Balance & Win")]
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private TMP_Text winAmountText;
    [SerializeField] private GameObject winTextObject;
    [SerializeField] private GameObject goodLuckObject;
    [Header("Balance & Win - Portrait")]
    [SerializeField] private TMP_Text balanceTextPortrait;
    [SerializeField] private TMP_Text winAmountTextPortrait;
    [SerializeField] private GameObject winTextObjectPortrait;
    [SerializeField] private GameObject goodLuckObjectPortrait;

    [Header("Free Spins Transition")]
    [Tooltip("Black film faded over the screen when a free-spin round ends.")]
    [SerializeField] private CanvasGroup transitionBackFilm;

    [Header("Spin Button")]
    [SerializeField] private Button spinButton;
    [SerializeField] private Button stopButton;
    [Header("Spin Button - Portrait")]
    [SerializeField] private Button spinButtonPortrait;
    [SerializeField] private Button stopButtonPortrait;

    [Header("Auto Play Stop Control")]
    [SerializeField] private Button autoSpinStopButton;
    [SerializeField] private TMP_Text autoSpinRemainingText;
    [Header("Auto Play Stop Control - Portrait")]
    [SerializeField] private Button autoSpinStopButtonPortrait;
    [SerializeField] private TMP_Text autoSpinRemainingTextPortrait;

    [Header("Auto Play Panel")]
    [SerializeField] private GameObject autoPlayPanel;
    [SerializeField] private RectTransform autoPlayPanelRect;
    [SerializeField] private Button autoPlayCloseButton;
    [Header("Auto Play Selection Buttons")]
    [SerializeField] private Button autoPlay10Button;
    [SerializeField] private Button autoPlay50Button;
    [SerializeField] private Button autoPlay100Button;
    [SerializeField] private Button autoPlay200Button;
    [SerializeField] private Button autoPlay500Button;
    [SerializeField] private Button autoPlayInfiniteButton;

    [Header("Auto Play Panel - Portrait")]
    [SerializeField] private GameObject autoPlayPanelPortrait;
    [SerializeField] private RectTransform autoPlayPanelRectPortrait;
    [SerializeField] private Button autoPlayCloseButtonPortrait;
    [SerializeField] private Button autoPlay10ButtonPortrait;
    [SerializeField] private Button autoPlay50ButtonPortrait;
    [SerializeField] private Button autoPlay100ButtonPortrait;
    [SerializeField] private Button autoPlay200ButtonPortrait;
    [SerializeField] private Button autoPlay500ButtonPortrait;
    [SerializeField] private Button autoPlayInfiniteButtonPortrait;

    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private RectTransform settingsPanelRect;
    [SerializeField] private Button settingsOpenButton;
    [SerializeField] private Button settingsCloseButton;
    [SerializeField] private Button settingsBgCloseButton;
    [SerializeField] private Button gameQuitButton;
    [Header("Settings Panel - Portrait")]
    [SerializeField] private GameObject settingsPanelPortrait;
    [SerializeField] private RectTransform settingsPanelRectPortrait;
    [SerializeField] private Button settingsOpenButtonPortrait;
    [SerializeField] private Button settingsCloseButtonPortrait;
    [SerializeField] private Button settingsBgCloseButtonPortrait;
    [SerializeField] private Button gameQuitButtonPortrait;

    [Header("Speed Buttons (Three-Layer Toggle)")]
    [SerializeField] private Button normalSpeedButton;
    [SerializeField] private Button turboSpeedButton;
    [SerializeField] private Button quickSpeedButton;
    [Header("Speed Buttons - Portrait")]
    [SerializeField] private Button normalSpeedButtonPortrait;
    [SerializeField] private Button turboSpeedButtonPortrait;
    [SerializeField] private Button quickSpeedButtonPortrait;

    [Header("Sound Panel")]
    [SerializeField] private GameObject soundPanel;
    [SerializeField] private RectTransform soundPanelRect;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button soundPanelCloseButton;
    [SerializeField] private Button soundPanelOpenButton;
    [SerializeField] private Button soundPanelOpenButtonPortrait;

    [Header("Game Rules Panel")]
    [SerializeField] private GameObject gameRulesPanel;
    [SerializeField] private RectTransform gameRulesPanelRect;
    [SerializeField] private Button gameRulesOpenButton;
    [SerializeField] private Button gameRulesBackButton;
    [Header("Game Rules Panel - Portrait")]
    [SerializeField] private Button gameRulesOpenButtonPortrait;

    [Header("Guide Panel")]
    [SerializeField] private GameObject guidePanel;
    [SerializeField] private RectTransform guidePanelRect;
    [SerializeField] private Button guideOpenButton;
    [SerializeField] private Button guideBackButton;
    [Header("Guide Panel - Portrait")]
    [SerializeField] private Button guideOpenButtonPortrait;

    [Header("Game Rules Dynamic Texts")]
    [SerializeField] private TMP_Text totalLineCountText;
    [SerializeField] private TMP_Text ruleSymbol0Text;
    [SerializeField] private TMP_Text ruleSymbol1Text;
    [SerializeField] private TMP_Text ruleSymbol2Text;
    [SerializeField] private TMP_Text ruleSymbol3Text;
    [SerializeField] private TMP_Text ruleSymbol4Text;
    [SerializeField] private TMP_Text ruleSymbol5Text;
    [SerializeField] private TMP_Text ruleSymbol6Text;
    [SerializeField] private TMP_Text ruleSymbol7Text;
    [SerializeField] private TMP_Text ruleSymbol8Text;
    [SerializeField] private TMP_Text ruleSymbol9Text;

    [Header("Free Spin Count Display - Game Screen")]
    [SerializeField] private GameObject freeSpinCountContainer;
    [SerializeField] private TMP_Text remainingFreeSpinsText;

    [Header("Ping Display")]
    [SerializeField] private TMP_Text pingText;
    [SerializeField] private TMP_Text pingTextPortrait;

    [Header("Platform Jackpot")]
    [SerializeField] private TMP_Text grandJackpotText;
    [SerializeField] private TMP_Text majorJackpotText;
    [SerializeField] private TMP_Text minorJackpotText;
    [SerializeField] private TMP_Text miniJackpotText;

    [Header("Platform Jackpot - Portrait")]
    [SerializeField] private TMP_Text grandJackpotTextPortrait;
    [SerializeField] private TMP_Text majorJackpotTextPortrait;
    [SerializeField] private TMP_Text minorJackpotTextPortrait;
    [SerializeField] private TMP_Text miniJackpotTextPortrait;

    [Header("Expand-Shrink Controls")]
    [SerializeField] private Button expandButton;
    [SerializeField] private Button shrinkButton;
    [Header("Expand-Shrink Controls - Portrait")]
    [SerializeField] private Button expandButtonPortrait;
    [SerializeField] private Button shrinkButtonPortrait;

    private bool isExpanded = false;
    private bool isSettingsPanelOpen = false;

    private Tween balanceTween;
    private Tween winTween;
    private double totalFreeSpinWin = 0;
    private int totalFreeSpinsAwarded = 0;

    private int initialFreeSpins = 0;

    // Optimistic balance: the locally-deducted balance shown while the spin is in flight
    private double optimisticBalance = 0;
    private bool hasOptimisticBalance = false;

    [Header("Rapid Stop Cooldown")]
    [Tooltip("Seconds the player must wait before pressing Stop again after an immediate stop.")]
    [SerializeField] private float rapidStopCooldown = 1f;
    private float lastRapidStopTime = -99f;

    private int currentRulesPage = 0;
    private bool isPageAnimating;
    [Header("UI State")]
    private double currentWinDisplayValue = 0;
    // Raised while a win popup owns the screen. WinPopupController drives it through
    // OnWinPopupOpened / OnWinPopupClosed, and GameManager's round loop parks on it.
    private bool isSpecialWinActive = false;
    public bool IsSpecialWinActive => isSpecialWinActive;

    private void Awake()
    {
        if (jsFunctCalls != null)
        {
            jsFunctCalls.RegisterVisibilityListener(gameObject.name);
        }
    }



    public void OnFocusChanged(string value)
    {
        bool focused = value == "1";
        Debug.Log("UNITY FOCUS CHANGED: " + value + " (focused: " + focused + ")");
        AudioManager.Instance?.SetMuteAll(!focused);
        if (gameManager != null && gameManager.socketManager != null)
        {
            gameManager.socketManager.HandleFocusChange(focused);
        }
    }

    private void Start()
    {
        SetupButtons();
        SetupAutoPlayPanel();
        SetupSettingsPanel();
        SetupGameRulesPanel();
        SetupGuidePanel();

        InitializeExpandShrink();

        if (gameScreen) gameScreen.SetActive(true);
        InitializeUI();
        StartCoroutine(WaitForInitialization());
        RegisterFullscreenListener();
    }


    private void InitializeUI()
    {
        if (soundPanel) soundPanel.SetActive(false);
        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, false);
        if (autoPlayPanelRect) autoPlayPanelRect.anchoredPosition = new Vector2(autoPlayPanelRect.anchoredPosition.x, -600f);
        if (autoPlayPanelRectPortrait) autoPlayPanelRectPortrait.anchoredPosition = new Vector2(autoPlayPanelRectPortrait.anchoredPosition.x, -600f);
        SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, false);

        SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
        UpdateSpeedButtonsVisibility(gameManager.currentSpinSpeed);

        isSettingsPanelOpen = false;
        SetGameObjectActive(settingsPanel, settingsPanelPortrait, false);
        if (gameRulesPanel) gameRulesPanel.SetActive(false);
        if (guidePanel) guidePanel.SetActive(false);
        if (freeSpinCountContainer) freeSpinCountContainer.SetActive(false);
        if (transitionBackFilm) transitionBackFilm.gameObject.SetActive(false);
        UpdatePingDisplay("-- ms");
    }

    #region Loading & Intro Sequence

    private IEnumerator WaitForInitialization()
    {
        float initializationTimeout = 20f;
        float timer = 0f;
        while (!gameManager.isInitialized && !gameManager.initializationFailed && timer < initializationTimeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (gameManager.initializationFailed || !gameManager.isInitialized)
        {
            if (gameManager.socketManager != null)
            {
                gameManager.socketManager.SetRaycastBlocker(false);
            }

            if (popupManager != null)
            {
                string errorMsg = gameManager.initializationFailed ? "Game failed to initialize." : "Initialization timed out. Please check your connection.";
                popupManager.ShowErrorPopup("Connection Error", errorMsg, true);
            }
        }
        else
        {
            AudioManager.Instance?.PlayBgMusic();
        }
    }

    #endregion

    #region UI Synchronization Helpers

    private void SetTMPText(TMP_Text text1, TMP_Text text2, string content)
    {
        if (text1) text1.text = content;
        if (text2) text2.text = content;
    }

    private void SetGameObjectActive(GameObject obj1, GameObject obj2, bool active)
    {
        if (obj1) obj1.SetActive(active);
        if (obj2) obj2.SetActive(active);
    }

    private void SetButtonInteractable(Button btn1, Button btn2, bool interactable)
    {
        if (btn1) btn1.interactable = interactable;
        if (btn2) btn2.interactable = interactable;
    }

    private void SetButtonActive(Button btn1, Button btn2, bool active)
    {
        if (btn1) btn1.gameObject.SetActive(active);
        if (btn2) btn2.gameObject.SetActive(active);
    }

    #endregion

    #region Button Setup

    private void SetupButtons()
    {
        // Bet buttons
        if (betPlusButton)  betPlusButton.onClick.AddListener(() => gameManager.IncreaseBet());
        if (betMinusButton) betMinusButton.onClick.AddListener(() => gameManager.DecreaseBet());
        if (betPlusButtonPortrait)  betPlusButtonPortrait.onClick.AddListener(() => gameManager.IncreaseBet());
        if (betMinusButtonPortrait) betMinusButtonPortrait.onClick.AddListener(() => gameManager.DecreaseBet());

        // Spin button
        if (spinButton)
        {
            var holdHandler = spinButton.GetComponent<SpinButtonHoldHandler>();
            if (holdHandler != null)
            {
                holdHandler.OnClick.AddListener(OnSpinButtonPressed);
                holdHandler.OnHoldThreeSeconds.AddListener(OnSpinButtonHeld);
            }
            else
            {
                spinButton.onClick.AddListener(OnSpinButtonPressed);
            }
        }
        if (spinButtonPortrait)
        {
            var holdHandler = spinButtonPortrait.GetComponent<SpinButtonHoldHandler>();
            if (holdHandler != null)
            {
                holdHandler.OnClick.AddListener(OnSpinButtonPressed);
                holdHandler.OnHoldThreeSeconds.AddListener(OnSpinButtonHeld);
            }
            else
            {
                spinButtonPortrait.onClick.AddListener(OnSpinButtonPressed);
            }
        }

        // Stop button
        if (stopButton) stopButton.onClick.AddListener(OnStopButtonPressed);
        if (stopButtonPortrait) stopButtonPortrait.onClick.AddListener(OnStopButtonPressed);

        // Auto spin stop button
        if (autoSpinStopButton)
        {
            autoSpinStopButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayAutoplayStop();
                gameManager.StopAutoPlay();
            });
        }
        if (autoSpinStopButtonPortrait)
        {
            autoSpinStopButtonPortrait.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayAutoplayStop();
                gameManager.StopAutoPlay();
            });
        }

        if (autoPlayCloseButton) autoPlayCloseButton.onClick.AddListener(CloseAutoPlayPanel);
        if (autoPlayCloseButtonPortrait) autoPlayCloseButtonPortrait.onClick.AddListener(CloseAutoPlayPanel);

        if (gameQuitButton) gameQuitButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExitButtonPressed(); });
        if (gameQuitButtonPortrait) gameQuitButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExitButtonPressed(); });

        if (expandButton) expandButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExpand(); });
        if (shrinkButton) shrinkButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnShrink(); });
        if (expandButtonPortrait) expandButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExpand(); });
        if (shrinkButtonPortrait) shrinkButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnShrink(); });

        // Speed buttons setup (Three-layer Toggle)
        if (normalSpeedButton) normalSpeedButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.Turbo); });
        if (turboSpeedButton)  turboSpeedButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.QuickSpin); });
        if (quickSpeedButton)  quickSpeedButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.Normal); });
        if (normalSpeedButtonPortrait) normalSpeedButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.Turbo); });
        if (turboSpeedButtonPortrait)  turboSpeedButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.QuickSpin); });
        if (quickSpeedButtonPortrait)  quickSpeedButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); SetSpeedMode(SpinSpeed.Normal); });
    }

    private void SetupAutoPlayPanel()
    {
        if (autoPlay10Button)       autoPlay10Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(10); });
        if (autoPlay50Button)       autoPlay50Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(50); });
        if (autoPlay100Button)      autoPlay100Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(100); });
        if (autoPlay200Button)      autoPlay200Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(200); });
        if (autoPlay500Button)      autoPlay500Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(500); });
        if (autoPlayInfiniteButton) autoPlayInfiniteButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(-1); });

        if (autoPlay10ButtonPortrait)       autoPlay10ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(10); });
        if (autoPlay50ButtonPortrait)       autoPlay50ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(50); });
        if (autoPlay100ButtonPortrait)      autoPlay100ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(100); });
        if (autoPlay200ButtonPortrait)      autoPlay200ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(200); });
        if (autoPlay500ButtonPortrait)      autoPlay500ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(500); });
        if (autoPlayInfiniteButtonPortrait) autoPlayInfiniteButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(-1); });
    }

    private void SetupSettingsPanel()
    {
        if (settingsOpenButton) settingsOpenButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            if (isSettingsPanelOpen)
                CloseSettingsPanel();
            else
                OpenSettingsPanel();
        });
        if (settingsOpenButtonPortrait) settingsOpenButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            if (isSettingsPanelOpen)
                CloseSettingsPanel();
            else
                OpenSettingsPanel();
        });

        if (settingsCloseButton) settingsCloseButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsCloseButtonPortrait) settingsCloseButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsBgCloseButton) settingsBgCloseButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsBgCloseButtonPortrait) settingsBgCloseButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        // Sound panel buttons & sliders
        if (soundPanelOpenButton) soundPanelOpenButton.onClick.AddListener(OpenSoundPanel);
        if (soundPanelOpenButtonPortrait) soundPanelOpenButtonPortrait.onClick.AddListener(OpenSoundPanel);
        if (soundPanelCloseButton) soundPanelCloseButton.onClick.AddListener(CloseSoundPanel);

        if (musicSlider)
        {
            if (AudioManager.Instance != null) musicSlider.value = AudioManager.Instance.MusicVolume;
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }
        if (sfxSlider)
        {
            if (AudioManager.Instance != null) sfxSlider.value = AudioManager.Instance.SfxVolume;
            sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
        }
    }

    private void SetupGameRulesPanel()
    {
        if (gameRulesOpenButton) gameRulesOpenButton.onClick.AddListener(OpenGameRulesPanel);
        if (gameRulesOpenButtonPortrait) gameRulesOpenButtonPortrait.onClick.AddListener(OpenGameRulesPanel);

        if (gameRulesBackButton) gameRulesBackButton.onClick.AddListener(() => { AudioManager.Instance?.PlayPopupClose(); CloseGameRulesPanel(); });
    }

    private void SetupGuidePanel()
    {
        if (guideOpenButton) guideOpenButton.onClick.AddListener(OpenGuidePanel);
        if (guideOpenButtonPortrait) guideOpenButtonPortrait.onClick.AddListener(OpenGuidePanel);

        if (guideBackButton) guideBackButton.onClick.AddListener(() => { AudioManager.Instance?.PlayPopupClose(); CloseGuidePanel(); });
    }

    #endregion

    #region Game Events

    internal void OnGameInitialized()
    {
        currentWinDisplayValue = 0;
        UpdateBetDisplay();
        UpdateBalanceDisplay();
        UpdateWinDisplay(0);
    }

    internal void OnSpinStarted()
    {
        AudioManager.Instance?.PlaySpinStart();

        if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
            SetBetControlsEnabled(false);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
            UpdateWinDisplay(0);
        }

        UpdateBalanceDisplay();

        CloseAutoPlayPanelImmediate();
    }

    internal void OnSpinResultReceived()
    {
        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
    }

    internal void OnSpinStopping(SpinResult result = null)
    {
        UpdateBalanceDisplay();
        if (result != null)
        {
            double displayWin = (gameManager != null && gameManager.isInFreeSpins) ? result.serverTotalRoundWin : result.winAmount;
            UpdateWinDisplay(displayWin);
        }
    }

    internal void OnSpinCompleted(SpinResult result = null)
    {
        if (result != null)
        {
            double displayWin = (gameManager != null && gameManager.isInFreeSpins) ? result.serverTotalRoundWin : result.winAmount;
            UpdateWinDisplay(displayWin);
        }
        UpdateBalanceDisplay();

        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);

            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        }
    }

    /// <summary>
    /// Called by WinPopupController the instant its popup starts. Raises the same
    /// isSpecialWinActive flag the CNY universal popup uses, which is what parks
    /// GameManager's round loop and keeps EnableControlsAfterWinAnimation from handing spin
    /// back behind the popup's back.
    /// </summary>
    internal void OnWinPopupOpened()
    {
        isSpecialWinActive = true;
        DisableControlsDuringWinAnimation();
    }

    /// <summary>Paired with <see cref="OnWinPopupOpened"/>: drops the gate and returns spin.</summary>
    internal void OnWinPopupClosed()
    {
        isSpecialWinActive = false;
        EnableControlsAfterWinAnimation();
    }

    /// <summary>
    /// Drop the gate WITHOUT touching the controls. For a popup torn down by the next spin:
    /// that spin has already set the buttons up for spinning, and restoring "win presentation
    /// finished" state on top of it would put the idle spin button back mid-spin.
    /// </summary>
    internal void OnWinPopupCancelled()
    {
        isSpecialWinActive = false;
    }

    internal void DisableControlsDuringWinAnimation()
    {
        SetBetControlsEnabled(false);
        SetSpinStopButtonStates(isSpinningState: false, isInteractable: false);
    }

    internal void EnableControlsAfterWinAnimation()
    {
        if (isSpecialWinActive) return;

        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
        }
    }

    #endregion

    #region Spin Button

    public void OnSpinButtonPressed()
    {
        if (gameManager.isAutoPlaying)
        {
            AudioManager.Instance?.PlayAutoplayStop();
            gameManager.StopAutoPlay();
            return;
        }

        if (!gameManager.IsSpinning())
        {
            gameManager.RequestSpin();
        }
    }

    private void OnStopButtonPressed()
    {
        if (gameManager.isAutoPlaying)
        {
            AudioManager.Instance?.PlayAutoplayStop();
            gameManager.StopAutoPlay();
            return;
        }

        if (gameManager.IsSpinning())
        {
            // Rapid-stop cooldown: prevent the player from spamming the stop button
            if (Time.unscaledTime - lastRapidStopTime < rapidStopCooldown)
                return;

            lastRapidStopTime = Time.unscaledTime;
            AudioManager.Instance?.PlaySpinStop();
            gameManager.RequestStop();
        }
    }

    internal void DisableSpinButtonDuringStop()
    {
        if (gameManager.isAutoPlaying)
        {
            SetButtonActive(spinButton, spinButtonPortrait, false);
            SetButtonActive(stopButton, stopButtonPortrait, false);
            SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, true);
            SetButtonInteractable(autoSpinStopButton, autoSpinStopButtonPortrait, false);
        }
        else
        {
            SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, false);
            SetButtonActive(spinButton, spinButtonPortrait, false);
            SetButtonActive(stopButton, stopButtonPortrait, true);
            SetButtonInteractable(stopButton, stopButtonPortrait, false);
        }
    }

    internal void SetSpinStopButtonStates(bool isSpinningState, bool isInteractable)
    {
        if (gameManager.isAutoPlaying)
        {
            SetButtonActive(spinButton, spinButtonPortrait, false);
            SetButtonActive(stopButton, stopButtonPortrait, false);
            SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, true);
            SetButtonInteractable(autoSpinStopButton, autoSpinStopButtonPortrait, isInteractable);
        }
        else
        {
            SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, false);
            
            if (isSpinningState)
            {
                SetButtonActive(spinButton, spinButtonPortrait, false);
                SetButtonActive(stopButton, stopButtonPortrait, true);
                SetButtonInteractable(stopButton, stopButtonPortrait, isInteractable);
            }
            else
            {
                SetButtonActive(stopButton, stopButtonPortrait, false);
                SetButtonActive(spinButton, spinButtonPortrait, true);
                SetButtonInteractable(spinButton, spinButtonPortrait, isInteractable);
            }
        }
    }

    #endregion

    #region Bet Controls

    internal void UpdateBetDisplay()
    {
        if (gameManager.gameConfig == null) return;

        double totalPay = gameManager.GetTotalPay();

        if (betAmountText) betAmountText.text = FormatAmount(totalPay);
        if (betAmountTextPortrait) betAmountTextPortrait.text = "TOTAL PAY : " + FormatAmount(totalPay);

        UpdateBetButtonStates();
        UpdateGameRulesDynamicTexts();
    }

    private void UpdateBetButtonStates()
    {
        SetButtonInteractable(betMinusButton, betMinusButtonPortrait, true);
        SetButtonInteractable(betPlusButton, betPlusButtonPortrait, true);
    }

    #endregion

    #region Auto Play Panel

    public void OnSpinButtonHeld()
    {
        if (gameManager.currentState == GameState.Idle && !gameManager.isAutoPlaying)
        {
            AudioManager.Instance?.PlayButton();
            OpenAutoPlayPanel();
        }
    }

    private void OpenAutoPlayPanel()
    {
        AudioManager.Instance?.PlayAutoplayPanelOpen();
        if (isSettingsPanelOpen)
            CloseSettingsPanelImmediate();

        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, true);
        if (autoPlayPanelRect)
        {
            autoPlayPanelRect.anchoredPosition = new Vector2(autoPlayPanelRect.anchoredPosition.x, -600f);
            autoPlayPanelRect.DOAnchorPosY(0f, 0.35f).SetEase(Ease.OutCubic);
        }
        if (autoPlayPanelRectPortrait)
        {
            autoPlayPanelRectPortrait.anchoredPosition = new Vector2(autoPlayPanelRectPortrait.anchoredPosition.x, -600f);
            autoPlayPanelRectPortrait.DOAnchorPosY(0f, 0.35f).SetEase(Ease.OutCubic);
        }
    }

    private void CloseAutoPlayPanel()
    {
        AudioManager.Instance?.PlayPopupClose();

        if (autoPlayPanelRect)
        {
            autoPlayPanelRect.DOAnchorPosY(-600f, 0.35f).SetEase(Ease.InCubic).OnComplete(() =>
            {
                if (autoPlayPanel) autoPlayPanel.SetActive(false);
            });
        }
        else
        {
            if (autoPlayPanel) autoPlayPanel.SetActive(false);
        }

        if (autoPlayPanelRectPortrait)
        {
            autoPlayPanelRectPortrait.DOAnchorPosY(-600f, 0.35f).SetEase(Ease.InCubic).OnComplete(() =>
            {
                if (autoPlayPanelPortrait) autoPlayPanelPortrait.SetActive(false);
            });
        }
        else
        {
            if (autoPlayPanelPortrait) autoPlayPanelPortrait.SetActive(false);
        }
    }

    private void StartAutoplayWithRounds(int rounds)
    {
        CloseAutoPlayPanel();
        gameManager.StartAutoPlay(rounds);
    }

    internal void OnAutoPlayStarted()
    {
        UpdateAutoPlayCount();
        SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        SetBetControlsEnabled(false);
    }

    internal void OnAutoPlayStopped()
    {
        SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, false);

        bool isRoundActive = gameManager.IsSpinning() || gameManager.lastResult != null;

        if (!isRoundActive && !gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        }
        else if (isRoundActive)
        {
            SetButtonActive(spinButton, spinButtonPortrait, false);
            SetButtonActive(stopButton, stopButtonPortrait, false);
            SetButtonActive(autoSpinStopButton, autoSpinStopButtonPortrait, true);
            SetButtonInteractable(autoSpinStopButton, autoSpinStopButtonPortrait, false);
        }
    }

    internal void UpdateAutoPlayCount()
    {
        string displayStr = "";
        if (gameManager.autoPlayTotalRounds == -1 || gameManager.autoPlayRemainingRounds < 0)
        {
            displayStr = "∞";
        }
        else
        {
            displayStr = $"{gameManager.autoPlayRemainingRounds}";
        }

        SetTMPText(autoSpinRemainingText, autoSpinRemainingTextPortrait, displayStr);
    }

    #endregion

    #region Spin Speed Universal Toggle Logic

    public void SetSpeedMode(SpinSpeed speed)
    {
        gameManager.SetSpinSpeed(speed);
        UpdateSpeedButtonsVisibility(speed);
    }

    private void UpdateSpeedButtonsVisibility(SpinSpeed speed)
    {
        SetButtonActive(normalSpeedButton, normalSpeedButtonPortrait, speed == SpinSpeed.Normal);
        SetButtonActive(turboSpeedButton, turboSpeedButtonPortrait, speed == SpinSpeed.Turbo);
        SetButtonActive(quickSpeedButton, quickSpeedButtonPortrait, speed == SpinSpeed.QuickSpin);
    }

    #endregion

    #region Sound Panel

    private void OpenSoundPanel()
    {
        AudioManager.Instance?.PlayButton();
        if (soundPanel == null) return;
        soundPanel.SetActive(true);
        if (soundPanelRect != null)
        {
            AnimatePopupOpen(soundPanelRect);
        }
        if (musicSlider && AudioManager.Instance != null)
        {
            musicSlider.value = AudioManager.Instance.MusicVolume;
        }
        if (sfxSlider && AudioManager.Instance != null)
        {
            sfxSlider.value = AudioManager.Instance.SfxVolume;
        }
    }

    private void CloseSoundPanel()
    {
        if (soundPanel == null || !soundPanel.activeSelf) return;
        if (soundPanelRect != null)
        {
            AnimatePopupClose(soundPanelRect, () =>
            {
                soundPanel.SetActive(false);
            });
        }
        else
        {
            soundPanel.SetActive(false);
        }
    }

    private void OnMusicSliderChanged(float val)
    {
        AudioManager.Instance?.SetMusicVolume(val);
    }

    private void OnSfxSliderChanged(float val)
    {
        AudioManager.Instance?.SetSfxVolume(val);
    }

    #endregion

    #region Settings Panel

    private void OpenSettingsPanel()
    {
        if ((autoPlayPanel && autoPlayPanel.activeSelf) || (autoPlayPanelPortrait && autoPlayPanelPortrait.activeSelf))
            CloseAutoPlayPanelImmediate();

        isSettingsPanelOpen = true;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, false);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, true);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, true);

        if (settingsPanel)
        {
            settingsPanel.SetActive(true);
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanel.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(1f, 0.35f);
        }

        if (settingsPanelPortrait)
        {
            settingsPanelPortrait.SetActive(true);
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanelPortrait.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(1f, 0.35f);
        }
    }

    private void CloseSettingsPanel()
    {
        isSettingsPanelOpen = false;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        if (settingsPanel)
        {
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanel.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(0f, 0.35f).OnComplete(() =>
            {
                settingsPanel.SetActive(false);
            });
        }

        if (settingsPanelPortrait)
        {
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanelPortrait.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(0f, 0.35f).OnComplete(() =>
            {
                settingsPanelPortrait.SetActive(false);
            });
        }
    }

    private void CloseSettingsPanelImmediate()
    {
        isSettingsPanelOpen = false;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        if (settingsPanel)
        {
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
            }
            settingsPanel.SetActive(false);
        }

        if (settingsPanelPortrait)
        {
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
            }
            settingsPanelPortrait.SetActive(false);
        }
    }

    private void CloseAutoPlayPanelImmediate()
    {
        if (autoPlayPanelRect) autoPlayPanelRect.localScale = Vector3.one;
        if (autoPlayPanelRectPortrait) autoPlayPanelRectPortrait.localScale = Vector3.one;
        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, false);
    }

    #endregion

    #region Game Rules Panel

    private void OpenGameRulesPanel()
    {
        if (isSettingsPanelOpen)
        {
            CloseSettingsPanelImmediate();
        }
        ShowGameRulesPanel();
    }

    private void ShowGameRulesPanel()
    {
        if (gameRulesPanel == null) return;
        gameRulesPanel.SetActive(true);
    }

    private void CloseGameRulesPanel()
    {
        if (gameRulesPanel == null) return;
        gameRulesPanel.SetActive(false);
    }

    #endregion

    #region Guide Panel

    private void OpenGuidePanel()
    {
        if (isSettingsPanelOpen)
        {
            CloseSettingsPanelImmediate();
        }
        ShowGuidePanel();
    }

    private void ShowGuidePanel()
    {
        if (guidePanel == null) return;
        guidePanel.SetActive(true);
    }

    private void CloseGuidePanel()
    {
        if (guidePanel == null) return;
        guidePanel.SetActive(false);
    }

    #endregion

    #region Free Spins Flow

    internal void OnFreeSpinsStarted(int spins)
    {
        OnFreeSpinsTriggered(spins);
    }

    internal void OnFreeSpinsTriggered(int spinsAwarded)
    {
        // [POPUP TODO] The CNY universal popup announced the award here and only started the
        // round once the player pressed Take. It has been deleted along with the rest of the
        // CNY UI, so the round starts immediately for now. Re-add the beat as a
        // WinPopupController tier once the Rich Piggies free-spin popup is authored.
        StartFreeSpinsSequence(spinsAwarded);
    }

    private void StartFreeSpinsSequence(int spinsAwarded)
    {
        totalFreeSpinWin = 0;
        initialFreeSpins = spinsAwarded;
        totalFreeSpinsAwarded = spinsAwarded;
        
        if (gameLogoObject) gameLogoObject.SetActive(false);

        UpdateFreeSpinCount(0, spinsAwarded);
        UpdateWinDisplay(0);
        gameManager.StartFirstFreeSpin();
    }

    internal void OnFreeSpinsEnded(double serverTotalRoundWin, int serverTotalSpinsUsed)
    {
        initialFreeSpins = 0;
        totalFreeSpinsAwarded = 0;

        // [POPUP TODO] As above — the round-total popup went with the CNY UI, so the
        // transition back to the base game runs straight away. serverTotalRoundWin is left
        // as a parameter because that popup is what will need it.
        StartCoroutine(EndFreeSpinsTransitionSequence());
    }

    private IEnumerator EndFreeSpinsTransitionSequence()
    {
        // 1. Fade in back film
        if (transitionBackFilm != null)
        {
            transitionBackFilm.gameObject.SetActive(true);
            transitionBackFilm.alpha = 0f;
            yield return transitionBackFilm.DOFade(1f, 0.5f).WaitForCompletion();
            yield return new WaitForSeconds(0.2f);
        }

        // 2. Setup main slot UI state behind back film
        if (freeSpinCountContainer) freeSpinCountContainer.SetActive(false);
        if (gameLogoObject) gameLogoObject.SetActive(true);

        // Reset win display for base game after free spins end
        UpdateWinDisplay(0);

        SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
        SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetBetControlsEnabled(true);

        // 3. Fade out back film
        if (transitionBackFilm != null)
        {
            yield return transitionBackFilm.DOFade(0f, 0.5f).WaitForCompletion();
            transitionBackFilm.gameObject.SetActive(false);
        }

        // 4. Resume autoplay if it was active before free spins and has leftover rounds
        if (gameManager != null && gameManager.ShouldResumeAutoPlay())
        {
            gameManager.ResumeAutoPlay();
        }
    }

    internal void UpdateFreeSpinCount(int playedSpins, int totalSpins = -1)
    {
        if (totalSpins > 0)
        {
            totalFreeSpinsAwarded = totalSpins;
        }

        if (freeSpinCountContainer) freeSpinCountContainer.SetActive(true);
        if (remainingFreeSpinsText) remainingFreeSpinsText.text = $"FREE GAME  {playedSpins}  OF  {totalFreeSpinsAwarded}";
    }

    #endregion

    #region Expand / Shrink

    private void InitializeExpandShrink()
    {
        SetExpandShrinkButtons(isExpanded: false);
    }

    private void OnExpand()
    {
        isExpanded = true;
        jsFunctCalls?.RequestExpandGame();
        SetExpandShrinkButtons(isExpanded: true);
    }

    private void OnShrink()
    {
        isExpanded = false;
        jsFunctCalls?.RequestShrinkGame();
        SetExpandShrinkButtons(isExpanded: false);
    }

    private void SetExpandShrinkButtons(bool isExpanded)
    {
        SetButtonActive(expandButton, expandButtonPortrait, !isExpanded);
        SetButtonActive(shrinkButton, shrinkButtonPortrait, isExpanded);
    }

    private void RegisterFullscreenListener()
    {
        jsFunctCalls?.RegisterFullscreenListener(gameObject.name);
    }

    internal void OnFullscreenChanged(string isFullscreen)
    {
        bool newExpandedState = isFullscreen == "1";
        Debug.Log($"[UI] OnFullscreenChanged callback: isFullscreen={isFullscreen}, newState={newExpandedState}");

        if (isExpanded != newExpandedState)
        {
            isExpanded = newExpandedState;
            SetExpandShrinkButtons(isExpanded);
            Debug.Log($"[UI] Button states synced to fullscreen: {(isExpanded ? "EXPANDED" : "SHRINK")}");
        }
    }
    
    #endregion

    #region Popup Animations (Generic)

    private void AnimatePopupOpen(RectTransform popupRect)
    {
        if (!popupRect) return;
        popupRect.localScale = Vector3.zero;
        popupRect.DOScale(1.4f, 0.3f).SetEase(Ease.OutBack);
    }

    private void AnimatePopupClose(RectTransform popupRect, System.Action onComplete)
    {
        if (!popupRect) return;

        AudioManager.Instance?.PlayPopupClose();

        Sequence closeSeq = DOTween.Sequence();
        closeSeq.Append(popupRect.DOScale(1.1f, 0.1f));
        closeSeq.Append(popupRect.DOScale(0f, 0.2f).SetEase(Ease.InBack));
        closeSeq.OnComplete(() =>
        {
            popupRect.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            onComplete?.Invoke();
        });
    }

    #endregion

    #region Display Updates

    internal void UpdatePingDisplay(int pingMs)
    {
        SetTMPText(pingText, pingTextPortrait, $"{pingMs} ms");
    }

    internal void UpdatePingDisplay(string content)
    {
        SetTMPText(pingText, pingTextPortrait, content);
    }

    internal void UpdateJackpotDisplay(JackpotValues values)
    {
        if (values == null) return;

        SetTMPText(grandJackpotText, grandJackpotTextPortrait, FormatJackpotValue(values.grandJackpot));
        SetTMPText(majorJackpotText, majorJackpotTextPortrait, FormatJackpotValue(values.majorJackpot));
        SetTMPText(minorJackpotText, minorJackpotTextPortrait, FormatJackpotValue(values.minorJackpot));
        SetTMPText(miniJackpotText, miniJackpotTextPortrait, FormatJackpotValue(values.miniJackpot));
    }

    private string FormatJackpotValue(string val)
    {
        if (string.IsNullOrEmpty(val)) return "$0.00";
        return val.StartsWith("$") ? val : "$" + val;
    }

    internal void UpdateBalanceDisplay()
    {
        SetTMPText(balanceText, balanceTextPortrait, "BALANCE : " + FormatAmount(gameManager.playerData.balance));
    }

    private void UpdateWinDisplay(double amount)
    {
        currentWinDisplayValue = amount;
        if (winAmountText) winAmountText.text = FormatAmount(amount);
        if (winAmountTextPortrait) winAmountTextPortrait.text = "WIN " + FormatAmount(amount);

        bool showWinText = amount > 0 || (gameManager != null && gameManager.isInFreeSpins);

        if (showWinText)
        {
            SetGameObjectActive(goodLuckObject, goodLuckObjectPortrait, false);
            SetGameObjectActive(winTextObject, winTextObjectPortrait, true);
        }
        else
        {
            SetGameObjectActive(goodLuckObject, goodLuckObjectPortrait, true);
            SetGameObjectActive(winTextObject, winTextObjectPortrait, false);
        }
    }

    private void AnimateBalanceUpdate(double newBalance, double startBalance = -1f, float durationOverride = -1f)
    {
        if (balanceTween != null) balanceTween.Kill();
        hasOptimisticBalance = false;
        SetTMPText(balanceText, balanceTextPortrait, "BALANCE : " + FormatAmount(newBalance));
    }

    private void AnimateWinUpdate(double targetWin, float duration = 0.8f)
    {
        if (winTween != null) winTween.Kill();
        UpdateWinDisplay(targetWin);
    }

    #endregion

    #region Helper Methods

    private string FormatAmount(double amount)
    {
        return amount.ToString("0.###");
    }

    private void SetBetControlsEnabled(bool enabled)
    {
        SetButtonInteractable(betPlusButton, betPlusButtonPortrait, enabled);
        SetButtonInteractable(betMinusButton, betMinusButtonPortrait, enabled);
    }

    #endregion

    #region Dynamic Game Rules Updates

    private void UpdateGameRulesDynamicTexts()
    {
        if (gameManager.gameConfig == null) return;

        if (totalLineCountText != null)
        {
            totalLineCountText.text = gameManager.gameConfig.paylineCount.ToString();
        }

        TMP_Text[] symbolTexts = {
            ruleSymbol0Text, ruleSymbol1Text, ruleSymbol2Text, ruleSymbol3Text,
            ruleSymbol4Text, ruleSymbol5Text, ruleSymbol6Text, ruleSymbol7Text,
            ruleSymbol8Text, ruleSymbol9Text
        };

        if (gameManager.gameConfig.symbols != null)
        {
            for (int i = 0; i < symbolTexts.Length; i++)
            {
                if (symbolTexts[i] == null) continue;

                var symbol = gameManager.gameConfig.symbols.Find(s => s.id == i);
                if (symbol != null && symbol.multipliers != null && symbol.multipliers.Count > 0)
                {
                    double originalBetAmount = gameManager.currentBetAmount;
                    string fullText = "";
                    
                    for (int m = 0; m < symbol.multipliers.Count; m++)
                    {
                        // Read the real match count rather than assuming the list starts at
                        // a 5-of-a-kind: the jackpot symbols pay on ONE symbol, so theirs is
                        // just { 1 } and counting down from 5 would mislabel every row.
                        int currentMatch = (symbol.matchCounts != null && m < symbol.matchCounts.Count)
                            ? symbol.matchCounts[m]
                            : 5 - m;

                        double win = symbol.multipliers[m];
                        string line = $"{currentMatch}     {win.ToString("0.###")}";
                        if (m == 0) fullText = line;
                        else fullText += $"\n{line}";
                    }
                    
                    symbolTexts[i].text = fullText;
                }
            }
        }
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        if (balanceTween != null) balanceTween.Kill();
        if (winTween != null) winTween.Kill();
        DOTween.KillAll();
    }

    #endregion

    #region Connection Popup Management

    private void OnExitButtonPressed()
    {
        if (popupManager != null)
        {
            popupManager.ShowExitGamePopup();
        }
        else if (gameManager != null)
        {
            gameManager.ExitGame();
        }
    }

    #endregion
}
