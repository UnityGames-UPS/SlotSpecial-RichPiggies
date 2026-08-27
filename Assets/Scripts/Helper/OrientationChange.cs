using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System;
using System.Collections;

public class OrientationChange : MonoBehaviour
{
    public enum OrientationMode
    {
        Landscape,
        DesktopPortrait,
        MobilePortrait
    }

    [Header("UI References")]
    [SerializeField] private RectTransform UIWrapper;
    [SerializeField] private CanvasScaler CanvasScaler;

    [Header("Transition Settings")]
    [SerializeField] private float transitionDuration = 0.2f;
    [SerializeField] private float waitForRotation = 0.2f;

    [Header("Device Detection Settings")]
    [SerializeField] private string androidKeyword = "MB";
    [SerializeField] private string iphoneKeyword = "IP";
    [SerializeField] private string mobileKeyword = "mobile";
    [SerializeField] private string currentDevice = "";

    public static event Action<OrientationMode, int, int> OnOrientationChanged;
    public event Action<OrientationMode, int, int> OnOrientationChangedInstance;

    private Vector2 ReferenceAspect;
    private Tween matchTween;
    private Tween rotationTween;
    private Coroutine rotationRoutine;
    private bool isLandscape;
    private OrientationMode currentMode = OrientationMode.Landscape;

    private int lastWidth = 0;
    private int lastHeight = 0;

    public string CurrentDevice => currentDevice;
    public OrientationMode CurrentMode => currentMode;
    public bool IsLandscape => isLandscape;
    public bool IsMobile => IsMobileDevice();

    private void Awake()
    {
        if (CanvasScaler != null)
        {
            ReferenceAspect = CanvasScaler.referenceResolution;
        }
        else
        {
            ReferenceAspect = new Vector2(1920, 1080);
        }
    }

    private void Start()
    {
        ApplyMatch(Screen.width, Screen.height);
    }

    public void DeviceCheck(string device)
    {
        DiviceCheck(device);
    }

    public void DiviceCheck(string device)
    {
        Debug.Log($"[OrientationChange] Device detected: {device}");
        currentDevice = device;
        int w = lastWidth > 0 ? lastWidth : Screen.width;
        int h = lastHeight > 0 ? lastHeight : Screen.height;
        ApplyMatch(w, h);
    }

    public bool IsMobileDevice()
    {
        if (!string.IsNullOrEmpty(currentDevice))
        {
            string dev = currentDevice.ToLower();
            if (dev.Contains("desktop"))
            {
                return false;
            }
            return (!string.IsNullOrEmpty(androidKeyword) && dev.Contains(androidKeyword.ToLower())) ||
                   (!string.IsNullOrEmpty(iphoneKeyword) && dev.Contains(iphoneKeyword.ToLower())) ||
                   (!string.IsNullOrEmpty(mobileKeyword) && dev.Contains(mobileKeyword.ToLower()));
        }
#if UNITY_EDITOR
        return true;
#else
        return Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;
#endif
    }

    public void SwitchDisplay(string dimensions)
    {
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(RotationCoroutine(dimensions));
    }

    private IEnumerator RotationCoroutine(string dimensions)
    {
        yield return new WaitForSecondsRealtime(waitForRotation);
        string[] parts = dimensions.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) && width > 0 && height > 0)
        {
            ApplyMatch(width, height);
        }
        else
        {
            Debug.LogWarning("Unity: Invalid format received in SwitchDisplay");
        }
    }

    private void ApplyMatch(int width, int height)
    {
        lastWidth = width;
        lastHeight = height;
        isLandscape = width > height;
        bool isMobile = IsMobileDevice();

        if (isLandscape)
        {
            currentMode = OrientationMode.Landscape;
        }
        else if (isMobile)
        {
            currentMode = OrientationMode.MobilePortrait;
        }
        else
        {
            currentMode = OrientationMode.DesktopPortrait;
        }

        // Apply Rotation: DesktopPortrait gets -90 degrees rotation, MobilePortrait & Landscape get 0 degrees.
        Quaternion targetRotation = (currentMode == OrientationMode.DesktopPortrait) ? Quaternion.Euler(0, 0, -90) : Quaternion.identity;
        if (UIWrapper != null)
        {
            if (rotationTween != null && rotationTween.IsActive()) rotationTween.Kill();
            rotationTween = UIWrapper.DOLocalRotateQuaternion(targetRotation, transitionDuration).SetEase(Ease.OutCubic);
        }

        // Calculate CanvasScaler Match Width/Height
        if (CanvasScaler != null)
        {
            Vector2 refRes = (currentMode == OrientationMode.MobilePortrait) ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
            CanvasScaler.referenceResolution = refRes;

            float refW = refRes.x;
            float refH = refRes.y;

            float scaleW, scaleH;
            if (currentMode == OrientationMode.DesktopPortrait)
            {
                // In DesktopPortrait, UIWrapper is rotated -90 degrees.
                // Canvas width (1920) corresponds to screen height.
                // Canvas height (1080) corresponds to screen width.
                scaleW = (float)height / refW;
                scaleH = (float)width / refH;
            }
            else
            {
                scaleW = (float)width / refW;
                scaleH = (float)height / refH;
            }

            float targetMatch = (scaleW <= scaleH) ? 0f : 1f;

            if (matchTween != null && matchTween.IsActive()) matchTween.Kill();
            matchTween = DOTween.To(() => CanvasScaler.matchWidthOrHeight, x => CanvasScaler.matchWidthOrHeight = x, targetMatch, transitionDuration).SetEase(Ease.InOutQuad);
        }

        Debug.Log($"[OrientationChange] Dimensions: {width}x{height}, Mode: {currentMode}, isLandscape: {isLandscape}, isMobile: {isMobile}");

        // Notify Listeners (including OCController)
        OnOrientationChanged?.Invoke(currentMode, width, height);
        OnOrientationChangedInstance?.Invoke(currentMode, width, height);
    }

    private void Update()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            ApplyMatch(Screen.width, Screen.height);
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Space))
        {
            int w = lastHeight > 0 ? lastHeight : Screen.height;
            int h = lastWidth > 0 ? lastWidth : Screen.width;
            SwitchDisplay(w + "," + h);
        }
        if (Input.GetKeyDown(KeyCode.M))
        {
            string nextDevice = IsMobileDevice() ? "desktop" : "mobile";
            DiviceCheck(nextDevice);
        }
#endif
    }
}