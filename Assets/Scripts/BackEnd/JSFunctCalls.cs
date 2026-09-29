using System.Runtime.InteropServices;
using UnityEngine;

public class JSFunctCalls : MonoBehaviour
{
  #region External Functions
  [DllImport("__Internal")]
  private static extern void SendPostMessage(string message);

  [DllImport("__Internal")]
  private static extern void RegisterResizeListener(string gameObjectName, string methodName);

  [DllImport("__Internal")]
  private static extern void RegisterTokenListener(string gameObjectName, string methodName);

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
  // Start, not Awake: the receiver's Awake must run before the initial dimensions callback.
  private void Start()
  {
    RegisterDimensionsListener();
  }
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

  /// <summary>Page drives gameObjectName.methodName("width,height") on its own viewport resize.</summary>
  internal void RegisterDimensionsListener(string gameObjectName = "OC", string methodName = "SwitchDisplay")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        RegisterResizeListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Resize listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }

  /// <summary>Routes the host's "TokenReceived" message to gameObjectName.methodName(json).</summary>
  internal void RegisterAuthTokenListener(string gameObjectName, string methodName = "ReceiveAuthToken")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
        RegisterTokenListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Token listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }
  #endregion
}