mergeInto(LibraryManager.library, {
  THUAI9_SelectPlaybackFile: function (gameObjectNamePtr, callbackNamePtr) {
    const gameObjectName = UTF8ToString(gameObjectNamePtr);
    const callbackName = UTF8ToString(callbackNamePtr);
    const sendMessage = function (methodName, payload) {
      if (typeof window !== 'undefined' && typeof window.THUAI9SendMessage === 'function') {
        window.THUAI9SendMessage(gameObjectName, methodName, payload || '');
        return true;
      }

      if (typeof SendMessage === 'function') {
        SendMessage(gameObjectName, methodName, payload || '');
        return true;
      }

      if (typeof Module !== 'undefined' && Module && typeof Module.SendMessage === 'function') {
        Module.SendMessage(gameObjectName, methodName, payload || '');
        return true;
      }

      const unityInstance =
        (window.THUAI9Unity && window.THUAI9Unity.unityInstance) ||
        window.unityInstance ||
        window.THUAIGameInstance ||
        window.gameInstance;
      if (unityInstance && typeof unityInstance.SendMessage === 'function') {
        unityInstance.SendMessage(gameObjectName, methodName, payload || '');
        return true;
      }

      console.error('[THUAI9] Unity SendMessage is not available', { gameObjectName, methodName });
      return false;
    };

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

      sendMessage(callbackName, payload);
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
    window.THUAI9SendMessage = function (targetGameObjectName, methodName, payload) {
      const target = targetGameObjectName || gameObjectName;
      if (typeof SendMessage === 'function') {
        SendMessage(target, methodName, payload || '');
        return true;
      }

      if (typeof Module !== 'undefined' && Module && typeof Module.SendMessage === 'function') {
        Module.SendMessage(target, methodName, payload || '');
        return true;
      }

      const unityInstance =
        (window.THUAI9Unity && window.THUAI9Unity.unityInstance) ||
        window.unityInstance ||
        window.THUAIGameInstance ||
        window.gameInstance;
      if (unityInstance && typeof unityInstance.SendMessage === 'function') {
        unityInstance.SendMessage(target, methodName, payload || '');
        return true;
      }

      console.error('[THUAI9] Unity SendMessage is not available', { gameObjectName: target, methodName });
      return false;
    };
    const sendMessage = function (methodName, payload) {
      return window.THUAI9SendMessage(gameObjectName, methodName, payload || '');
    };
    window.THUAI9ArrayBufferToBase64 = function (arrayBuffer) {
      const bytes = new Uint8Array(arrayBuffer);
      const chunkSize = 0x8000;
      let binary = '';
      for (let i = 0; i < bytes.length; i += chunkSize) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
      }
      return btoa(binary);
    };
    const arrayBufferToBase64 = window.THUAI9ArrayBufferToBase64;

    window.THUAI9Unity = window.THUAI9Unity || {};
    window.THUAI9Unity.gameObjectName = gameObjectName;
    window.THUAI9Unity.unityInstance = window.THUAI9Unity.unityInstance || window.unityInstance || window.THUAIGameInstance || window.gameInstance;
    window.THUAI9Unity.sendMessage = function (methodName, payload) {
      sendMessage(methodName, payload || '');
    };
    window.THUAI9Unity.setPlaybackFile = function (url, name) {
      sendMessage('SetPlaybackFile', JSON.stringify({ url: url, name: name || url }));
    };
    window.THUAI9Unity.loadPlaybackBase64 = function (base64, name) {
      sendMessage('LoadPlaybackBase64', JSON.stringify({ data: base64, name: name || 'playback.thuaipb' }));
    };
    window.THUAI9Unity.startWebLive = function (sourceName) {
      sendMessage('StartWebLive', sourceName || 'WebGL Live');
    };
    window.THUAI9Unity.stopWebLive = function () {
      sendMessage('StopWebLive', '');
    };
    window.THUAI9Unity.submitLiveFrameBase64 = function (base64) {
      sendMessage('SubmitLiveFrameBase64', base64 || '');
    };
    window.THUAI9Unity.submitLiveFrameJson = function (json) {
      sendMessage('SubmitLiveFrameJson', typeof json === 'string' ? json : JSON.stringify(json));
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
        sendMessage('StartWebLive', 'WebSocket ' + webSocketUrl);
        window.dispatchEvent(new CustomEvent('thuai9-live-socket-open', { detail: webSocketUrl }));
      };
      socket.onmessage = function (event) {
        if (typeof event.data === 'string') {
          const payload = event.data.trim();
          sendMessage(payload.startsWith('{') ? 'SubmitLiveFrameJson' : 'SubmitLiveFrameBase64', payload);
          return;
        }

        sendMessage('SubmitLiveFrameBase64', arrayBufferToBase64(event.data));
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
      sendMessage('StopWebLive', '');
    };
    window.THUAI9Unity.onPlayerAction = function (handler) {
      if (typeof handler !== 'function') {
        return function () {};
      }
      const listener = function (event) {
        handler(event.detail);
      };
      window.addEventListener('thuai9-player-action', listener);
      return function () {
        window.removeEventListener('thuai9-player-action', listener);
      };
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
  }
});
