# WebGL: React Native → iframe Migration — Executable Runbook

Port a Unity WebGL slot game from the old **AWT / React Native** host bridge to the **`custom`** WebGL
template embedded in a plain **iframe** host (`GameLoader` / `Iframe.js`). An iframe host talks to
Unity **only** via `window.postMessage`.

This doc is a step-by-step runbook meant to be executed against a fresh game codebase. Follow the
steps in order. Do **not** skip the Discovery step — names differ between games.

---

## 0. Rules for whoever executes this (read first)

- **Only edit these files:**
  - The WebGL template `Assets/WebGLTemplates/<template>/index.html` (create/replace `custom`).
  - The jslib plugin (`*.jslib`, usually `Assets/Plugins/webgl/CustomJsLib.jslib`).
  - C# under `Assets/Scripts/` (specifically the `JSFunctCalls` and `SocketIOManager` equivalents).
- **Never edit, delete, or create:** `.meta`, `.unity` (scenes), `.prefab`, `.asset`, materials,
  `ProjectSettings/`, `Packages/`. Reading a `.unity`/`.meta` file to *discover a name* is fine;
  writing one is not.
- **Do not assign serialized references or rename GameObjects** — those are Editor actions. If a step
  needs one, **stop and write a one-line note for the human developer** instead.
- **You cannot build.** There is no CLI build; verification is static (grep + reading). After you
  finish, tell the human to build with the `custom` template and test (see §9).
- **Idempotent:** every step has a "skip if already present" check. If the game is partially migrated,
  do not duplicate code.
- If a game diverges structurally from what a step describes (different loader, missing method, etc.),
  **stop and report** rather than guessing.

---

## 1. Does this game need migrating? (preflight)

Run these; if the "old" markers are present, proceed. If the "new" markers are already present, the
game may be done — verify with §8 instead of re-applying.

```bash
# OLD (React Native) markers — expect these before migration:
grep -rn "ReactNativeWebView\|SendLogToReactNative" Assets/Scripts Assets/Plugins
ls -d "Assets/WebGLTemplates/AWT" "Assets/Advanced WebGL Template" 2>/dev/null

# NEW markers — if all present, migration is likely already applied:
grep -rn "RegisterTokenListener\|RegisterResizeListener" Assets/Plugins Assets/Scripts
ls -d Assets/WebGLTemplates/custom 2>/dev/null
```

---

## 2. Discovery — fill in the substitution table

The snippets below use placeholders. Find the real values in *this* game and substitute them
everywhere before writing. Most games match the defaults, but **confirm each one.**

| Placeholder | What it is | Default | How to find it |
| --- | --- | --- | --- |
| `<JSLIB>` | the jslib file path | `Assets/Plugins/webgl/CustomJsLib.jslib` | `find Assets -name "*.jslib"` |
| `<JSFUNC>` | class wrapping `[DllImport]` calls | `JSFunctCalls` | `grep -rln "DllImport(\"__Internal\")" Assets/Scripts` |
| `<SOCKET>` | class that opens the socket / auth | `SocketIOManager` | `grep -rln "SendCustomMessage(\"authToken\")" Assets/Scripts` |
| `<AUTH_METHOD>` | C# method receiving the token JSON | `ReceiveAuthToken` | `grep -rn "AuthTokenData\|socketURL" Assets/Scripts` — find the method that `JsonUtility.FromJson`s it |
| `<OC_GO>` | GameObject name carrying the orientation script | `OC` | see note ① |
| `<OC_METHOD>` | its `void (string "w,h")` receiver | `SwitchDisplay` | `grep -rn "void SwitchDisplay" Assets/Scripts` |
| `<FAIL_METHOD>` | RN failed-connect exit method (may not exist) | `ReactNativeCallOnFailedToConnect` | `grep -rn "SendCustomMessage(\"onExit\")" Assets/Scripts` |

① `<OC_GO>`: the resize listener targets a GameObject by name. Find the script:
`grep -rn "void SwitchDisplay\|OrientationChange" Assets/Scripts`. The GameObject it's attached to is
set in the scene — confirm its name by asking the human, or read the `.unity` file
(`grep -n "m_Name: OC" Assets/Scenes/*.unity` and cross-check the script guid). If you cannot confirm,
**leave the default `"OC"` and add a note for the human to verify the GameObject name.**

**Also confirm the auth JSON contract matches.** Read `<AUTH_METHOD>` and the class it deserializes
into (e.g. `AuthTokenData`). The host sends `{ cookie, socketURL, nameSpace }`. The C# fields must
have those exact names. If they differ, **stop and report** — the host keys and C# fields must agree.

---

## 3. WebGL template → `Assets/WebGLTemplates/custom/index.html`

Create the folder + file (this is a template asset, not a scene/prefab — safe to write). Then set
Player Settings → Resolution and Presentation → WebGL Template = **custom** — that dropdown is an
**Editor action; leave a note for the human** (you can create the file, but you can't flip the
setting). Full file:

```html
<!DOCTYPE html>
<html lang="en-us">

<head>
  <meta charset="utf-8">
  <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
  <title>{{{ PRODUCT_NAME }}}</title>
  <style>
    body,
    html {
      margin: 0;
      padding: 0;
      overflow: hidden;
      height: 100%;
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      background-color: black;
    }

    #unity-canvas {
      height: 100%;
      width: 100%;
    }

    #loading-screen {
      position: absolute;
      width: 100%;
      height: 100%;
    }
  </style>
</head>

<body>

  <canvas id="unity-canvas" tabindex="-1"></canvas>
  <script>
    const canvas = document.querySelector("#unity-canvas");
    var unityInstance = null;   // captured on load; jslib listeners use it as a SendMessage fallback
    var lastProgressSent = -1;

    // Forward loader progress to the host; loading-complete is signalled by Unity's "OnEnter".
    function sendProgressToPlatform(progressValue) {
      var percentage = Math.round(progressValue * 100);
      if (percentage === lastProgressSent) return;
      lastProgressSent = percentage;
      if (window.parent) {
        window.parent.postMessage({ type: "UnityLoaderProgress", progress: percentage }, "*");
      }
    }
    // Pin the canvas to the visible viewport; Unity (OrientationChange) owns aspect ratio & rotation.
    function resizeCanvas() {
      // visualViewport is the accurate visible area on iOS; fall back to innerWidth/Height.
      var vv = window.visualViewport;
      var w = Math.round(vv ? vv.width : window.innerWidth);
      var h = Math.round(vv ? vv.height : window.innerHeight);
      window.scrollTo(0, 0);
      document.documentElement.style.width = w + "px";
      document.documentElement.style.height = h + "px";
      document.body.style.width = w + "px";
      document.body.style.height = h + "px";
      canvas.style.width = w + "px";
      canvas.style.height = h + "px";
    }

    window.addEventListener('resize', resizeCanvas);
    window.addEventListener('orientationchange', resizeCanvas);
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', resizeCanvas);
      window.visualViewport.addEventListener('scroll', function () { window.scrollTo(0, 0); });
    }
    // Scroll-lock: CSS touch-action alone won't stop iOS pinch-pan; non-passive touchmove does.
    // Unity still receives the touch events (preventDefault only cancels browser scroll/zoom).
    window.addEventListener('scroll', function () { window.scrollTo(0, 0); }, { passive: true });
    document.addEventListener('touchmove', function (e) { e.preventDefault(); }, { passive: false });
    document.addEventListener('gesturestart', function (e) { e.preventDefault(); });
    document.addEventListener('gesturechange', function (e) { e.preventDefault(); });

    var buildUrl = "Build";
    var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}";
    var config = {
      dataUrl: buildUrl + "/{{{ DATA_FILENAME }}}",
      frameworkUrl: buildUrl + "/{{{ FRAMEWORK_FILENAME }}}",
#if USE_THREADS
      workerUrl: buildUrl + "/{{{ WORKER_FILENAME }}}",
#endif
#if USE_WASM
      codeUrl: buildUrl + "/{{{ CODE_FILENAME }}}",
#endif
#if MEMORY_FILENAME
      memoryUrl: buildUrl + "/{{{ MEMORY_FILENAME }}}",
#endif
#if SYMBOLS_FILENAME
      symbolsUrl: buildUrl + "/{{{ SYMBOLS_FILENAME }}}",
#endif
      streamingAssetsUrl: "StreamingAssets",
      companyName: {{{ JSON.stringify(COMPANY_NAME) }}},
      productName: {{{ JSON.stringify(PRODUCT_NAME) }}},
      productVersion: {{{ JSON.stringify(PRODUCT_VERSION) }}},
    };

    var script = document.createElement("script");
    script.src = loaderUrl;
    script.onload = () => {
      createUnityInstance(canvas, config, (progress) => {
        sendProgressToPlatform(progress);
      }).then((instance) => {
        unityInstance = instance;
        resizeCanvas();
      }).catch((message) => {
        alert(message);
      });
    };
    document.body.appendChild(script);
  </script>
  <!-- Inbound host messages (TokenReceived) are handled by CustomJsLib.jslib, registered from C#. -->
  <script>
    window.focus();
  </script>

</body>

</html>
```

**Variance checks:**
- This template targets **Unity 2020.1+/Unity 6** loaders (`createUnityInstance` + `{{{ }}}` macros).
  If the game's existing templates use `UnityLoader.instantiate(...)` (very old Unity), **stop and
  report** — the loader glue is different.
- Unity owns aspect ratio / rotation via the orientation script, so the template just fills the
  viewport — no aspect math here.
- `unityInstance` is intentionally kept even though nothing in the template reads it: the jslib
  listeners use it as a `SendMessage` fallback.
- Do **not** add `console.log`/`warn`/`error` overrides to silence output — they suppress the
  diagnostics needed to debug the `authToken`/`TokenReceived` handshake (this is exactly what made a
  "loads but never connects" failure invisible during migration testing).

---

## 4. `<JSLIB>` — jslib bridge

The jslib holds all outbound + inbound bridges. Target state (keep any *unrelated* existing exports
this game has, e.g. a custom log or fullscreen helper):

1. **`SendPostMessage`** → iframe-only. Delete any `window.ReactNativeWebView` branch and any dead
   `authToken` listener branch inside it.
2. **Delete `SendLogToReactNative`** entirely (host doesn't consume Unity logs). *(Also remove its C#
   usage — §5.)*
3. **Keep `RegisterVisibilityChangeListener`** if present.
4. **Add `RegisterResizeListener`** and **`RegisterTokenListener`**.

Reference target (adjust only if the game has extra unrelated exports to preserve):

```js
mergeInto(LibraryManager.library, {
  // Outbound: Unity -> iframe host, as { type, data } via window.parent.postMessage.
  SendPostMessage: function (messagePtr) {
    var message = UTF8ToString(messagePtr);
    if (typeof window !== "undefined" && window.parent && typeof window.parent.postMessage === "function") {
      window.parent.postMessage({ type: message, data: {} }, "*");
    }
  },

  RegisterVisibilityChangeListener: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);

    function sendFocusToUnity(focused) {
      if (typeof SendMessage === 'function') {
        SendMessage(gameObjectName, 'OnFocusChanged', focused ? '1' : '0');
      }
    }
    function handleVisibility() { sendFocusToUnity(document.visibilityState === 'visible'); }
    function handleBlur() { sendFocusToUnity(false); }
    function handleFocus() { sendFocusToUnity(true); }

    document.removeEventListener('visibilitychange', handleVisibility);
    document.removeEventListener('webkitvisibilitychange', handleVisibility);
    window.removeEventListener('blur', handleBlur);
    window.removeEventListener('focus', handleFocus);

    document.addEventListener('visibilitychange', handleVisibility);
    document.addEventListener('webkitvisibilitychange', handleVisibility);
    window.addEventListener('blur', handleBlur);
    window.addEventListener('focus', handleFocus);
  },

  // Self-contained resize bridge: the Unity page listens to its own viewport and pushes
  // "width,height" into Unity (<OC_GO>.<OC_METHOD>) — no dependency on the iframe host.
  RegisterResizeListener: function (gameObjectNamePtr, methodNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var methodName = UTF8ToString(methodNamePtr);

    function sendDimensionsToUnity() {
      try {
        // visualViewport is the accurate visible area on iOS; fall back to innerWidth/Height.
        var vv = window.visualViewport;
        var w = Math.round(vv ? vv.width : window.innerWidth);
        var h = Math.round(vv ? vv.height : window.innerHeight);
        var dimensions = w + ',' + h;
        if (typeof SendMessage === 'function') {
          SendMessage(gameObjectName, methodName, dimensions);
        } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
          unityInstance.SendMessage(gameObjectName, methodName, dimensions);
        }
      } catch (err) {
        console.error('[JS] resize send failed:', err);
      }
    }

    // No debounce here — the orientation receiver coalesces via StopCoroutine + waitForRotation,
    // so send on every event and let C# settle it. Remove any prior listener before re-adding.
    if (window._unityResizeCallback) {
      window.removeEventListener('resize', window._unityResizeCallback);
      window.removeEventListener('orientationchange', window._unityResizeCallback);
      if (window.visualViewport) window.visualViewport.removeEventListener('resize', window._unityResizeCallback);
    }
    window._unityResizeCallback = sendDimensionsToUnity;
    window.addEventListener('resize', window._unityResizeCallback);
    window.addEventListener('orientationchange', window._unityResizeCallback);
    if (window.visualViewport) window.visualViewport.addEventListener('resize', window._unityResizeCallback);

    sendDimensionsToUnity();   // initial sync
  },

  // Inbound auth: host posts { type:"TokenReceived", data:{cookie,socketURL,nameSpace} } -> Unity.
  RegisterTokenListener: function (gameObjectNamePtr, methodNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var methodName = UTF8ToString(methodNamePtr);

    if (window._unityTokenCallback) {
      window.removeEventListener('message', window._unityTokenCallback);
    }
    window._unityTokenCallback = function (event) {
      if (!event.data || event.data.type !== 'TokenReceived') return;
      var json = JSON.stringify(event.data.data);
      if (typeof SendMessage === 'function') {
        SendMessage(gameObjectName, methodName, json);
      } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
        unityInstance.SendMessage(gameObjectName, methodName, json);
      }
    };
    window.addEventListener('message', window._unityTokenCallback);
  }
});
```

**Variance — the "no debounce" decision:** removing the jslib debounce is correct **only if** the
orientation receiver self-coalesces. Confirm `<OC_METHOD>` looks like:
`StopCoroutine(...)` then `StartCoroutine(...)` with a `WaitForSeconds/WaitForSecondsRealtime(...)`
before applying. If it does **not** (it applies immediately on every call), keep a debounce in the
jslib instead — wrap the callback:
`window._unityResizeCallback = function(){ clearTimeout(t); t = setTimeout(sendDimensionsToUnity, 100); };`

---

## 5. `<JSFUNC>` (e.g. `JSFunctCalls.cs`)

Remove RN log-forwarding; add the resize + token imports/wrappers and a `Start()` that registers the
resize listener. If the class already has a `Start()`, add the `RegisterDimensionsListener()` call
into it rather than adding a second `Start()`.

Target file:

```csharp
using System.Runtime.InteropServices;
using UnityEngine;

public class JSFunctCalls : MonoBehaviour
{
  [DllImport("__Internal")] private static extern void SendPostMessage(string message);

  [DllImport("__Internal")] private static extern void RegisterVisibilityChangeListener(string gameObjectName);

  [DllImport("__Internal")] private static extern void RegisterResizeListener(string gameObjectName, string methodName);

  [DllImport("__Internal")] private static extern void RegisterTokenListener(string gameObjectName, string methodName);

  // Start, not Awake: the receiver's Awake must run before the initial dimensions callback.
  void Start()
  {
    RegisterDimensionsListener();
  }

  internal void SendCustomMessage(string message)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    SendPostMessage(message);
#endif
  }

  internal void RegisterVisibilityListener(string gameObjectName)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    RegisterVisibilityChangeListener(gameObjectName);
#else
    Debug.Log("[JS] Visibility listener not registered (editor mode)");
#endif
  }

  // Self-contained resize bridge: the page drives <OC_GO>.<OC_METHOD>("width,height") on its own resize.
  internal void RegisterDimensionsListener(string gameObjectName = "OC", string methodName = "SwitchDisplay")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    RegisterResizeListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Resize listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }

  // Inbound auth: routes the host's "TokenReceived" message to gameObjectName.methodName(json).
  internal void RegisterAuthTokenListener(string gameObjectName, string methodName = "ReceiveAuthToken")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    RegisterTokenListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Token listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }
}
```

**Gotchas:**
- The `[DllImport]` extern and its C# wrapper **must have different names** (extern
  `RegisterResizeListener` / wrapper `RegisterDimensionsListener`; extern `RegisterTokenListener` /
  wrapper `RegisterAuthTokenListener`). Same name + same `(string, string)` signature = an overload
  ambiguity that won't compile.
- Substitute `"OC"`/`"SwitchDisplay"` with `<OC_GO>`/`<OC_METHOD>` if discovery found different names.
- Also delete the old `SendLogToReactNative` extern and the `OnEnable/OnDisable/HandleLog`
  `Application.logMessageReceived` wiring if the game had them.

---

## 6. `<SOCKET>` (e.g. `SocketIOManager.cs`)

Two small changes; the rest of the auth/exit/error path already works via `SendCustomMessage`.

**a)** Register the token listener in the **WebGL branch** where the game asks for the token. Find it:
`grep -n 'SendCustomMessage("authToken")' <SOCKET path>`. Add the registration on the line *before*
it, using `gameObject.name` (no hardcoded GO name needed — this component lives on the socket GO):

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    JSManager.RegisterAuthTokenListener(gameObject.name); // listen for host's TokenReceived before asking
    JSManager.SendCustomMessage("authToken");
    StartCoroutine(WaitForAuthToken(options));
#endif
```

Use the game's actual field name for the `JSFunctCalls` reference (here `JSManager`) and pass
`<AUTH_METHOD>` as the 2nd arg if it isn't `ReceiveAuthToken`:
`JSManager.RegisterAuthTokenListener(gameObject.name, "<AUTH_METHOD>")`.

**b)** If `<FAIL_METHOD>` exists and emits lowercase `"onExit"`, fix the casing so the host matches it:

```csharp
JSManager.SendCustomMessage("OnExit"); // was "onExit" — host matches "OnExit"
```

Leave `OrientationChange.cs` (the `<OC_METHOD>` receiver) **unchanged** — the jslib now drives it.
The C# `CloseGame()` method (if any) can stay; nothing calls it from the host anymore.

---

## 7. Do-not-touch (repeat)
Scenes/prefabs/materials/`.asset`/`.meta`/`ProjectSettings`/`Packages`; serialized `[SerializeField]`
wiring; GameObject names. If a change seems to need one of these, write a note for the human instead.

---

## 8. Static verification (no build — run these greps)

All of these should hold after migration:

```bash
# Template exists and has the bridge pieces:
grep -c "UnityLoaderProgress" Assets/WebGLTemplates/custom/index.html      # >= 1
grep -c "visualViewport" Assets/WebGLTemplates/custom/index.html           # >= 1
grep -c "addEventListener(\"message\"" Assets/WebGLTemplates/custom/index.html  # 0 (moved to jslib)

# jslib has the three listeners + iframe-only send, and NO RN leftovers:
grep -c "RegisterTokenListener\|RegisterResizeListener" <JSLIB>            # >= 2
grep -c "ReactNativeWebView\|SendLogToReactNative" <JSLIB>                 # 0

# C# wiring:
grep -rn "RegisterAuthTokenListener" Assets/Scripts                        # expect wrapper def + 1 call site
grep -c  "RegisterResizeListener\|RegisterTokenListener" <JSFUNC>          # 2 externs (single file)
grep -rn "SendLogToReactNative\|logMessageReceived" Assets/Scripts         # expect no matches

# No dangling refs to deleted AWT editor scripts:
grep -rn "awtConfig\|awt_editor\|awtConfigInspector" Assets/Scripts        # expect no matches
```

Then reason about compilation: each `[DllImport]` extern has a matching jslib export; extern and
wrapper names differ; the `Start()`/`RegisterAuthTokenListener` call sites reference real methods.

---

## 9. Host side — hand to the platform team (DO NOT edit their code here)

The host (`Iframe.js` / `GameLoader`) must: reply to Unity's `authToken` by posting
`{ type:"TokenReceived", data:{ cookie, socketURL, nameSpace } }` into the iframe; drive its loader
bar off `UnityLoaderProgress`; hide the loader on `OnEnter`. It must **remove** the OC sync
(`DiviceCheck`/`SwitchOrientation`/`SwitchDisplay`/`SetDevicePixelRatio` sends and their listeners)
and **remove `CloseGame`** (removing the iframe closes the socket by itself). Full details live in
`IFRAME_HOST_CHANGES.md` — give them that file.

---

## 10. Cleanup / delete (safe file deletions)
- `Assets/WebGLTemplates/AWT/` (+ `AWT.meta`) and `Assets/Advanced WebGL Template/` (+ its `.meta`).
- Optional: `Assets/Scripts/JS/JSHandler.cs` is dead in most of these games (its `GetAuthToken`
  DllImport isn't in the jslib). If you remove it, also remove any `[SerializeField] ... JSHandler ...`
  field in `<SOCKET>` (it's unused) — that's a code edit, fine.

---

## Message contract (summary)
- **Host → Unity:** `TokenReceived { cookie, socketURL, nameSpace }` — the only message the host sends.
- **Unity → Host:** `authToken`, `UnityLoaderProgress { progress: 0..100 }`, `OnEnter` (ready — hide
  loader), `OnExit`, `session_expired`, `error`.

> **Decision — do not reintroduce `CloseGame` / `unityInstance.Quit()`.** A graceful teardown
> (host posts `CloseGame` → in-iframe listener calls `unityInstance.Quit()` → `OnExit` → host removes
> the iframe) was evaluated and rejected. Closing = **remove the iframe**: that alone closes the
> socket and frees the WASM heap, which `unityInstance.Quit()` does not do. See §3 of
> `IFRAME_HOST_CHANGES.md` for the full rationale.

## Final: tell the human to build & test (Safari iOS is the reference)
Build with the `custom` template, load in the iframe host, and confirm: token/connect →
`initData` → `OnEnter` hides the host loader; progress bar advances; page can't scroll/pinch;
orientation change re-orients cleanly (no stutter); in-game Quit → `OnExit`; closing the iframe drops
the socket in the Network tab; project compiled after the AWT deletions.
