mergeInto(LibraryManager.library, {
  THUAI9_SelectPlaybackFile: function (gameObjectNamePtr, callbackNamePtr) {
    const gameObjectName = UTF8ToString(gameObjectNamePtr);
    const callbackName = UTF8ToString(callbackNamePtr);

    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.thuaipb,application/octet-stream';
    input.style.display = 'none';

    input.onchange = function () {
      const file = input.files && input.files[0];
      if (!file) {
        input.remove();
        return;
      }

      const url = URL.createObjectURL(file);
      const payload = JSON.stringify({
        url: url,
        name: file.name || 'playback.thuaipb',
        size: file.size || 0
      });

      THUAI9SendMessage(gameObjectName, callbackName, payload);
      window.dispatchEvent(new CustomEvent('thuai9-playback-file-selected', {
        detail: { url: url, name: file.name, size: file.size }
      }));
      input.remove();
    };

    document.body.appendChild(input);
    input.click();
  },

  THUAI9_NotifyUnityReady: function (gameObjectNamePtr) {
    const gameObjectName = UTF8ToString(gameObjectNamePtr);
    window.THUAI9Unity = window.THUAI9Unity || {};
    THUAI9ConfigureDevelopmentConsole();
    window.THUAI9Unity.gameObjectName = gameObjectName;
    window.THUAI9Unity.sendMessage = function (methodName, payload) {
      THUAI9SendMessage(gameObjectName, methodName, payload || '');
    };
    window.THUAI9Unity.setPlaybackFile = function (url, name) {
      THUAI9SendMessage(gameObjectName, 'SetPlaybackFile', JSON.stringify({ url: url, name: name || url }));
    };
    window.THUAI9Unity.loadPlaybackBase64 = function (base64, name) {
      THUAI9SendMessage(gameObjectName, 'LoadPlaybackBase64', JSON.stringify({ data: base64, name: name || 'playback.thuaipb' }));
    };
    window.THUAI9Unity.startWebLive = function (sourceName) {
      THUAI9SendMessage(gameObjectName, 'StartWebLive', sourceName || 'WebGL Live');
    };
    window.THUAI9Unity.stopWebLive = function () {
      THUAI9SendMessage(gameObjectName, 'StopWebLive', '');
    };
    window.THUAI9Unity.submitLiveFrameBase64 = function (base64) {
      THUAI9SendMessage(gameObjectName, 'SubmitLiveFrameBase64', base64 || '');
    };
    window.THUAI9Unity.submitLiveFrameJson = function (json) {
      THUAI9SendMessage(gameObjectName, 'SubmitLiveFrameJson', typeof json === 'string' ? json : JSON.stringify(json));
    };
    window.THUAI9Unity.connectLiveWebSocket = function (webSocketUrl) {
      if (!webSocketUrl) {
        window.dispatchEvent(new CustomEvent('thuai9-live-socket-error', { detail: 'missing-url' }));
        return;
      }

      if (window.THUAI9Unity.liveSocket) {
        window.THUAI9Unity.liveSocket.close();
      }

      const socket = new WebSocket(webSocketUrl);
      window.THUAI9Unity.liveSocket = socket;
      socket.binaryType = 'arraybuffer';
      socket.onopen = function () {
        THUAI9SendMessage(gameObjectName, 'StartWebLive', 'WebSocket ' + webSocketUrl);
        window.dispatchEvent(new CustomEvent('thuai9-live-socket-open', { detail: webSocketUrl }));
      };
      socket.onmessage = function (event) {
        if (typeof event.data === 'string') {
          const payload = event.data.trim();
          THUAI9SendMessage(gameObjectName, payload.startsWith('{') ? 'SubmitLiveFrameJson' : 'SubmitLiveFrameBase64', payload);
          return;
        }

        THUAI9SendMessage(gameObjectName, 'SubmitLiveFrameBase64', THUAI9ArrayBufferToBase64(event.data));
      };
      socket.onerror = function () {
        window.dispatchEvent(new CustomEvent('thuai9-live-socket-error', { detail: webSocketUrl }));
      };
      socket.onclose = function () {
        window.dispatchEvent(new CustomEvent('thuai9-live-socket-close', { detail: webSocketUrl }));
      };
    };
    window.THUAI9Unity.disconnectLiveWebSocket = function () {
      if (window.THUAI9Unity.liveSocket) {
        window.THUAI9Unity.liveSocket.close();
        window.THUAI9Unity.liveSocket = null;
      }
      THUAI9SendMessage(gameObjectName, 'StopWebLive', '');
    };
    window.dispatchEvent(new CustomEvent('thuai9-unity-ready', { detail: { gameObjectName: gameObjectName } }));
  },

  THUAI9_DispatchUnityEvent: function (eventNamePtr, payloadPtr) {
    const eventName = UTF8ToString(eventNamePtr);
    const payload = UTF8ToString(payloadPtr);
    window.dispatchEvent(new CustomEvent('thuai9-unity-event', {
      detail: { eventName: eventName, payload: payload }
    }));
    window.dispatchEvent(new CustomEvent('thuai9-' + eventName, {
      detail: payload
    }));
  },

  THUAI9_ClearDevelopmentConsole: function () {
    THUAI9ClearDevelopmentConsole();
  }
});

function THUAI9SendMessage(gameObjectName, methodName, payload) {
  if (typeof SendMessage === 'function') {
    SendMessage(gameObjectName, methodName, payload);
    return;
  }

  const unityInstance = window.unityInstance || window.THUAIGameInstance || window.gameInstance;
  if (unityInstance && typeof unityInstance.SendMessage === 'function') {
    unityInstance.SendMessage(gameObjectName, methodName, payload);
    return;
  }

  console.error('[THUAI9] Unity SendMessage is not available', { gameObjectName, methodName });
}

function THUAI9ArrayBufferToBase64(arrayBuffer) {
  const bytes = new Uint8Array(arrayBuffer);
  const chunkSize = 0x8000;
  let binary = '';
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}

function THUAI9FindDevelopmentConsole() {
  const nodes = Array.from(document.body ? document.body.querySelectorAll('div, section, aside') : []);
  const candidates = nodes.filter(function (node) {
    const text = (node.innerText || node.textContent || '').trim();
    return text.includes('Development Console');
  });
  if (candidates.length === 0) {
    return null;
  }

  const controlled = candidates.filter(function (node) {
    const text = (node.innerText || node.textContent || '').trim();
    return text.includes('Clear') || text.includes('Close') || node.querySelector('button, input[type="button"]');
  });
  const pool = controlled.length > 0 ? controlled : candidates;
  pool.sort(function (a, b) {
    const aArea = (a.offsetWidth || 0) * (a.offsetHeight || 0);
    const bArea = (b.offsetWidth || 0) * (b.offsetHeight || 0);
    return aArea - bArea;
  });
  return pool[0];
}

function THUAI9StyleDevelopmentConsole() {
  const panel = THUAI9FindDevelopmentConsole();
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
}

function THUAI9ClearDevelopmentConsole() {
  const panel = THUAI9FindDevelopmentConsole();
  if (!panel) {
    return;
  }

  const buttons = Array.from(panel.querySelectorAll('button, input[type="button"]'));
  for (const button of buttons) {
    const label = ((button.textContent || button.value || '') + '').trim().toLowerCase();
    if (label === 'clear' || label === 'close') {
      try {
        button.click();
      } catch (_) {
        // Ignore browser/Unity template differences.
      }
    }
  }

  panel.style.display = 'none';
}

function THUAI9ConfigureDevelopmentConsole() {
  if (window.THUAI9UnityDevelopmentConsoleObserver) {
    return;
  }

  THUAI9StyleDevelopmentConsole();
  const observer = new MutationObserver(function () {
    THUAI9StyleDevelopmentConsole();
  });
  observer.observe(document.body || document.documentElement, { childList: true, subtree: true });
  window.THUAI9UnityDevelopmentConsoleObserver = observer;
}
