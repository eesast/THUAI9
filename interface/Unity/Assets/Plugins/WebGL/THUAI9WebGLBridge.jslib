mergeInto(LibraryManager.library, {
  $THUAI9WebGLBridge: {
    sendMessage: function (gameObjectName, methodName, payload) {
      if (typeof SendMessage === 'function') {
        SendMessage(gameObjectName, methodName, payload || '');
        return;
      }

      var unityInstance = window.unityInstance || window.THUAIGameInstance || window.gameInstance || (typeof Module !== 'undefined' ? Module : null);
      if (unityInstance && typeof unityInstance.SendMessage === 'function') {
        unityInstance.SendMessage(gameObjectName, methodName, payload || '');
        return;
      }

      console.error('[THUAI9] Unity SendMessage is not available', { gameObjectName: gameObjectName, methodName: methodName });
    },

    arrayBufferToBase64: function (arrayBuffer) {
      var bytes = new Uint8Array(arrayBuffer);
      var chunkSize = 0x8000;
      var binary = '';
      for (var i = 0; i < bytes.length; i += chunkSize) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
      }
      return btoa(binary);
    },

    dispatchCustomEvent: function (eventName, detail) {
      if (typeof window.CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent(eventName, { detail: detail }));
        return;
      }

      var event = document.createEvent('CustomEvent');
      event.initCustomEvent(eventName, false, false, detail);
      window.dispatchEvent(event);
    },

    findDevelopmentConsole: function () {
      if (!document.body) {
        return null;
      }

      var nodes = Array.prototype.slice.call(document.body.querySelectorAll('div, section, aside'));
      var candidates = nodes.filter(function (node) {
        var text = (node.innerText || node.textContent || '').trim();
        return text.indexOf('Development Console') >= 0;
      });
      if (candidates.length === 0) {
        return null;
      }

      var controlled = candidates.filter(function (node) {
        var text = (node.innerText || node.textContent || '').trim();
        return text.indexOf('Clear') >= 0 || text.indexOf('Close') >= 0 || node.querySelector('button, input[type="button"]');
      });
      var pool = controlled.length > 0 ? controlled : candidates;
      pool.sort(function (a, b) {
        var aArea = (a.offsetWidth || 0) * (a.offsetHeight || 0);
        var bArea = (b.offsetWidth || 0) * (b.offsetHeight || 0);
        return aArea - bArea;
      });
      return pool[0];
    },

    styleDevelopmentConsole: function () {
      var panel = THUAI9WebGLBridge.findDevelopmentConsole();
      if (!panel) {
        return;
      }

      panel.style.position = 'fixed';
      panel.style.left = '50%';
      panel.style.top = '72px';
      panel.style.bottom = 'auto';
      panel.style.transform = 'translateX(-50%)';
      panel.style.width = 'min(1180px, calc(100vw - 48px))';
      panel.style.maxHeight = '30vh';
      panel.style.overflow = 'auto';
      panel.style.zIndex = '2147483647';
    },

    clearDevelopmentConsole: function () {
      var panel = THUAI9WebGLBridge.findDevelopmentConsole();
      if (!panel) {
        return;
      }

      var buttons = Array.prototype.slice.call(panel.querySelectorAll('button, input[type="button"]'));
      for (var i = 0; i < buttons.length; i++) {
        var button = buttons[i];
        var label = ((button.textContent || button.value || '') + '').trim().toLowerCase();
        if (label === 'clear' || label === 'close') {
          try {
            button.click();
          } catch (_) {
            // Ignore browser/Unity template differences.
          }
        }
      }

      panel.style.display = 'none';
    },

    configureDevelopmentConsole: function () {
      if (window.THUAI9UnityDevelopmentConsoleObserver) {
        return;
      }

      THUAI9WebGLBridge.styleDevelopmentConsole();
      if (typeof MutationObserver !== 'function') {
        return;
      }

      var root = document.body || document.documentElement;
      if (!root) {
        return;
      }

      var observer = new MutationObserver(function () {
        THUAI9WebGLBridge.styleDevelopmentConsole();
      });
      observer.observe(root, { childList: true, subtree: true });
      window.THUAI9UnityDevelopmentConsoleObserver = observer;
    }
  },

  THUAI9_SelectPlaybackFile__deps: ['$THUAI9WebGLBridge'],
  THUAI9_SelectPlaybackFile: function (gameObjectNamePtr, callbackNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackName = UTF8ToString(callbackNamePtr);

    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.thuaipb,application/octet-stream';
    input.style.display = 'none';

    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) {
        input.remove();
        return;
      }

      var url = URL.createObjectURL(file);
      var payload = JSON.stringify({
        url: url,
        name: file.name || 'playback.thuaipb',
        size: file.size || 0
      });

      THUAI9WebGLBridge.sendMessage(gameObjectName, callbackName, payload);
      THUAI9WebGLBridge.dispatchCustomEvent('thuai9-playback-file-selected', {
        url: url,
        name: file.name,
        size: file.size
      });
      input.remove();
    };

    document.body.appendChild(input);
    input.click();
  },

  THUAI9_NotifyUnityReady__deps: ['$THUAI9WebGLBridge'],
  THUAI9_NotifyUnityReady: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    window.THUAI9Unity = window.THUAI9Unity || {};
    THUAI9WebGLBridge.configureDevelopmentConsole();
    window.THUAI9Unity.gameObjectName = gameObjectName;
    window.THUAI9Unity.sendMessage = function (methodName, payload) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, methodName, payload || '');
    };
    window.THUAI9Unity.setPlaybackFile = function (url, name) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'SetPlaybackFile', JSON.stringify({ url: url, name: name || url }));
    };
    window.THUAI9Unity.loadPlaybackBase64 = function (base64, name) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'LoadPlaybackBase64', JSON.stringify({ data: base64, name: name || 'playback.thuaipb' }));
    };
    window.THUAI9Unity.startWebLive = function (sourceName) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'StartWebLive', sourceName || 'WebGL Live');
    };
    window.THUAI9Unity.stopWebLive = function () {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopWebLive', '');
    };
    window.THUAI9Unity.submitLiveFrameBase64 = function (base64) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameBase64', base64 || '');
    };
    window.THUAI9Unity.submitLiveFrameJson = function (json) {
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameJson', typeof json === 'string' ? json : JSON.stringify(json));
    };
    window.THUAI9Unity.connectLiveWebSocket = function (webSocketUrl) {
      if (!webSocketUrl) {
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-error', 'missing-url');
        return;
      }

      if (window.THUAI9Unity.liveSocket) {
        window.THUAI9Unity.liveSocket.close();
      }

      var socket = new WebSocket(webSocketUrl);
      window.THUAI9Unity.liveSocket = socket;
      socket.binaryType = 'arraybuffer';
      socket.onopen = function () {
        THUAI9WebGLBridge.sendMessage(gameObjectName, 'StartWebLive', 'WebSocket ' + webSocketUrl);
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-open', webSocketUrl);
      };
      socket.onmessage = function (event) {
        if (typeof event.data === 'string') {
          var payload = event.data.trim();
          THUAI9WebGLBridge.sendMessage(gameObjectName, payload.indexOf('{') === 0 ? 'SubmitLiveFrameJson' : 'SubmitLiveFrameBase64', payload);
          return;
        }

        THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameBase64', THUAI9WebGLBridge.arrayBufferToBase64(event.data));
      };
      socket.onerror = function () {
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-error', webSocketUrl);
      };
      socket.onclose = function () {
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-close', webSocketUrl);
      };
    };
    window.THUAI9Unity.disconnectLiveWebSocket = function () {
      if (window.THUAI9Unity.liveSocket) {
        window.THUAI9Unity.liveSocket.close();
        window.THUAI9Unity.liveSocket = null;
      }
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopWebLive', '');
    };
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-ready', { gameObjectName: gameObjectName });
  },

  THUAI9_DispatchUnityEvent__deps: ['$THUAI9WebGLBridge'],
  THUAI9_DispatchUnityEvent: function (eventNamePtr, payloadPtr) {
    var eventName = UTF8ToString(eventNamePtr);
    var payload = UTF8ToString(payloadPtr);
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-event', {
      eventName: eventName,
      payload: payload
    });
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-' + eventName, payload);
  },

  THUAI9_ClearDevelopmentConsole__deps: ['$THUAI9WebGLBridge'],
  THUAI9_ClearDevelopmentConsole: function () {
    THUAI9WebGLBridge.clearDevelopmentConsole();
  }
});
