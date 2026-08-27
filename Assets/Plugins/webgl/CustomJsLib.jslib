mergeInto(LibraryManager.library, {
    SendLogToReactNative: function (messagePtr) {
        var message = UTF8ToString(messagePtr);
        if (window.ReactNativeWebView) {
          window.ReactNativeWebView.postMessage(message);
        } 
    },

    SendPostMessage: function(messagePtr) {
      var message = UTF8ToString(messagePtr);
      if(window.ReactNativeWebView){
        if(message == "authToken"){
          window.ReactNativeWebView.postMessage("if message is authtoken");
          var injectedObjectJson = window.ReactNativeWebView.injectedObjectJson();
          var injectedObj = JSON.parse(injectedObjectJson);

          window.ReactNativeWebView.postMessage('Injected obj : ' + injectedObjectJson);
          
          var combinedData = JSON.stringify({
              socketURL: injectedObj.socketURL.trim(),
              cookie: injectedObj.token.trim(),
              nameSpace: injectedObj.nameSpace ? injectedObj.nameSpace.trim() : ""
          });

          if (typeof SendMessage === 'function') {
            SendMessage('SocketManager', 'ReceiveAuthToken', combinedData);
          }
        }
        window.ReactNativeWebView.postMessage(message);
      }
      else if(window.parent){
        if(window.parent.dispatchReactUnityEvent){
          console.log("Inside window parent");
          window.parent.dispatchReactUnityEvent(message); 
        }
      }
    },

    RequestFullscreen: function () {
      console.log('[JS] RequestFullscreen called');
      var el = document.documentElement;
      var req = el.requestFullscreen
             || el.webkitRequestFullscreen
             || el.mozRequestFullScreen
             || el.msRequestFullscreen;
      if (req) {
        req.call(el).then(function() {
          console.log('[JS] Fullscreen request succeeded');
        }).catch(function(err) {
          console.warn('[JS] RequestFullscreen failed:', err);
        });
      } else {
        console.error('[JS] No fullscreen API available!');
      }
    },

    ExitFullscreen: function () {
      console.log('[JS] ExitFullscreen called');
      var exit = document.exitFullscreen
              || document.webkitExitFullscreen
              || document.mozCancelFullScreen
              || document.msExitFullscreen;
      if (exit) {
        exit.call(document).then(function() {
          console.log('[JS] Exit fullscreen succeeded');
        }).catch(function(err) {
          console.warn('[JS] ExitFullscreen failed:', err);
        });
      } else {
        console.error('[JS] No exit fullscreen API available!');
      }
    },

    RegisterFullscreenChangeListener: function(gameObjectNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        console.log('[JS] RegisterFullscreenChangeListener called for GameObject:', gameObjectName);

        // Helper to check current fullscreen state
        function isCurrentlyFullscreen() {
            return !!(document.fullscreenElement || 
                      document.webkitFullscreenElement || 
                      document.mozFullScreenElement || 
                      document.msFullscreenElement);
        }

        // Helper to find the Unity instance
        function getUnityInstance() {
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance && window.unityInstance.SendMessage) {
                return window.unityInstance;
            }
            if (typeof window.gameInstance !== 'undefined' && window.gameInstance && window.gameInstance.SendMessage) {
                return window.gameInstance;
            }
            if (typeof Module !== 'undefined' && Module && Module.SendMessage) {
                return Module;
            }
            if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
                return unityInstance;
            }
            if (window.parent && window.parent !== window) {
                if (window.parent.unityInstance && window.parent.unityInstance.SendMessage) {
                    return window.parent.unityInstance;
                }
                if (window.parent.gameInstance && window.parent.gameInstance.SendMessage) {
                    return window.parent.gameInstance;
                }
            }
            for (var key in window) {
                try {
                    if (window.hasOwnProperty(key)) {
                        var obj = window[key];
                        if (obj && typeof obj === 'object' && typeof obj.SendMessage === 'function') {
                            return obj;
                        }
                    }
                } catch(e) {}
            }
            return null;
        }

        // Send fullscreen state to Unity
        function sendToUnity(isFS) {
            try {
                var instance = getUnityInstance();
                if (instance && instance.SendMessage) {
                    instance.SendMessage(gameObjectName, 'OnFullscreenChanged', isFS ? '1' : '0');
                    console.log('[JS] Sent fullscreen state to Unity: ' + (isFS ? 'EXPANDED' : 'SHRINK'));
                } else {
                    console.warn('[JS] Unity instance not available, cannot send');
                }
            } catch (err) {
                console.error('[JS] Error sending message to Unity:', err);
            }
        }

        // Fullscreen change callback
        window._unityFullscreenCallback = function() {
            var isFS = isCurrentlyFullscreen();
            console.log('[JS] Fullscreen event fired. State:', isFS ? 'EXPANDED' : 'SHRINK');
            sendToUnity(isFS);
        };

        // Remove any previously registered listeners to avoid duplicates
        document.removeEventListener('fullscreenchange',       window._unityFullscreenCallback);
        document.removeEventListener('webkitfullscreenchange', window._unityFullscreenCallback);
        document.removeEventListener('mozfullscreenchange',    window._unityFullscreenCallback);
        document.removeEventListener('MSFullscreenChange',     window._unityFullscreenCallback);

        // Register listeners for all browser engines
        document.addEventListener('fullscreenchange',       window._unityFullscreenCallback);
        document.addEventListener('webkitfullscreenchange', window._unityFullscreenCallback);
        document.addEventListener('mozfullscreenchange',    window._unityFullscreenCallback);
        document.addEventListener('MSFullscreenChange',     window._unityFullscreenCallback);

        console.log('[JS] Fullscreen event listeners registered for:', gameObjectName);
    },

    RegisterVisibilityChangeListener: function(gameObjectNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        console.log('[JS] RegisterVisibilityChangeListener called for GameObject:', gameObjectName);

        function setUnityAudioSuspended(suspended) {
            try {
                var wa = (typeof WEBAudio !== 'undefined') ? WEBAudio
                       : (typeof Module !== 'undefined' && Module.WEBAudio) ? Module.WEBAudio
                       : null;
                if (!wa || !wa.audioContext) return;
                if (suspended) {
                    if (wa.audioContext.state === 'running') wa.audioContext.suspend();
                } else {
                    if (wa.audioContext.state === 'suspended') wa.audioContext.resume();
                }
            } catch (err) { console.warn('[JS] Unity audio suspend/resume failed:', err); }
        }

        function sendFocusToUnity(focused) {
            setUnityAudioSuspended(!focused);
            try {
                var value = focused ? '1' : '0';
                if (typeof SendMessage === 'function') {
                    SendMessage(gameObjectName, 'OnFocusChanged', value);
                } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
                    unityInstance.SendMessage(gameObjectName, 'OnFocusChanged', value);
                }
            } catch (err) {
                console.error('[JS] Error sending focus message to Unity:', err);
            }
        }

        window._unityVisibilityCallback = function() {
            var hidden = document.hidden || document.webkitHidden;
            sendFocusToUnity(!hidden);
        };
        window._unityWindowBlurCallback  = function() { sendFocusToUnity(false); };
        window._unityWindowFocusCallback = function() { sendFocusToUnity(true); };

        document.removeEventListener('visibilitychange',       window._unityVisibilityCallback);
        document.removeEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
        window.removeEventListener('blur',  window._unityWindowBlurCallback);
        window.removeEventListener('focus', window._unityWindowFocusCallback);

        document.addEventListener('visibilitychange',       window._unityVisibilityCallback);
        document.addEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
        window.addEventListener('blur',  window._unityWindowBlurCallback);
        window.addEventListener('focus', window._unityWindowFocusCallback);
    }
});
