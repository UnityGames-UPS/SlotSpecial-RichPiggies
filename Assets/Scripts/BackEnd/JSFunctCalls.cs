using System.Runtime.InteropServices;
using UnityEngine;

public class JSFunctCalls : MonoBehaviour
{
  #region External Functions
  [DllImport("__Internal")]
  private static extern void SendLogToReactNative(string message);

  [DllImport("__Internal")]
  private static extern void SendPostMessage(string message);

  [DllImport("__Internal")]
  private static extern void RequestFullscreen();

  [DllImport("__Internal")]
  private static extern void ExitFullscreen();

  [DllImport("__Internal")]
  private static extern void RegisterFullscreenChangeListener(string gameObjectName);

  [DllImport("__Internal")]
  private static extern void RegisterVisibilityChangeListener(string gameObjectName);
  #endregion

  #region Unity Lifecycle
  private void OnEnable()
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.logMessageReceived += HandleLog;
        Debug.Log("[JS] Log forwarding enabled");
#endif
  }

  private void OnDisable()
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.logMessageReceived -= HandleLog;
        Debug.Log("[JS] Log forwarding disabled");
#endif
  }
  #endregion

  #region Private Methods
#if UNITY_WEBGL && !UNITY_EDITOR
    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        string formattedMessage = $"[{type}] {logString}";
        SendLogToReactNative(formattedMessage);
    }
#endif
  #endregion

  #region Public API
  internal void SendCustomMessage(string message)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log($"[JS] Sending message to platform: {message}");
        SendPostMessage(message);
#else
    Debug.Log($"[JS] Would send message (editor mode): {message}");
#endif
  }

  /// <summary>Requests browser fullscreen (expand).</summary>
  internal void RequestExpandGame()
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("[JS] Requesting fullscreen expand");
        RequestFullscreen();
#else
    Debug.Log("[JS] Would request fullscreen (editor mode)");
#endif
  }

  /// <summary>Exits browser fullscreen (shrink).</summary>
  internal void RequestShrinkGame()
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("[JS] Requesting exit fullscreen (shrink)");
        ExitFullscreen();
#else
    Debug.Log("[JS] Would exit fullscreen (editor mode)");
#endif
  }

  /// <summary>
  /// Registers a browser fullscreenchange listener that calls back into Unity
  /// on the given GameObject when the user exits fullscreen externally (e.g. Escape key).
  /// </summary>
  internal void RegisterFullscreenListener(string gameObjectName)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log($"[JS] Registering fullscreen change listener on '{gameObjectName}'");
        RegisterFullscreenChangeListener(gameObjectName);
#else
    Debug.Log("[JS] Fullscreen listener not registered (editor mode)");
#endif
  }

  /// <summary>
  /// Registers a browser visibilitychange/blur/focus listener that calls back into Unity
  /// on the given GameObject when tab visibility or window focus changes.
  /// </summary>
  internal void RegisterVisibilityListener(string gameObjectName)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log($"[JS] Registering visibility change listener on '{gameObjectName}'");
        RegisterVisibilityChangeListener(gameObjectName);
#else
    Debug.Log("[JS] Visibility listener not registered (editor mode)");
#endif
  }
  #endregion
}