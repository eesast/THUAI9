mergeInto(LibraryManager.library, {
  $THUAI9WebGLBridge: {
    maxBase64Bytes: 16 * 1024 * 1024,
    maxRemotePlaybackBytes: 64 * 1024 * 1024,
    activePlaybackObjectUrl: null,
    sendMessage: function (gameObjectName, methodName, payload) {
      if (typeof SendMessage === 'function') { SendMessage(gameObjectName, methodName, payload || ''); return; }
      var unityInstance = window.unityInstance || window.THUAIGameInstance || window.gameInstance || (typeof Module !== 'undefined' ? Module : null);
      if (unityInstance && typeof unityInstance.SendMessage === 'function') { unityInstance.SendMessage(gameObjectName, methodName, payload || ''); return; }
      console.error('[THUAI9] Unity SendMessage is not available', { gameObjectName: gameObjectName, methodName: methodName });
    },
    arrayBufferToBase64: function (arrayBuffer) {
      var bytes = new Uint8Array(arrayBuffer), chunkSize = 0x8000, binary = '';
      for (var i = 0; i < bytes.length; i += chunkSize) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
      return btoa(binary);
    },
    normalizeUrl: function (url) {
      if (!url) return '';
      if (/^(blob:|data:|https?:|file:)/i.test(url)) return url;
      try { return new URL(url, window.location.href).href; }
      catch (_) { return url; }
    },
    normalizeBase64: function (base64OrDataUrl) {
      if (!base64OrDataUrl) return '';
      var text = String(base64OrDataUrl).trim();
      if (/^data:/i.test(text)) {
        var comma = text.indexOf(',');
        text = comma >= 0 ? text.substring(comma + 1) : '';
      }
      return text.replace(/ /g, '+').replace(/[\r\n\t]/g, '');
    },
    estimateBase64ByteCount: function (base64) {
      if (!base64) return 0;
      var padding = base64.endsWith('==') ? 2 : (base64.endsWith('=') ? 1 : 0);
      return Math.max(0, Math.floor(base64.length / 4) * 3 - padding);
    },
    dispatchCustomEvent: function (eventName, detail) {
      if (typeof window.CustomEvent === 'function') { window.dispatchEvent(new CustomEvent(eventName, { detail: detail })); return; }
      var event = document.createEvent('CustomEvent'); event.initCustomEvent(eventName, false, false, detail); window.dispatchEvent(event);
    },
    dispatchCustomEventLater: function (eventName, detail) {
      var dispatch = function () { THUAI9WebGLBridge.dispatchCustomEvent(eventName, detail); };
      if (typeof window.setTimeout === 'function') { window.setTimeout(dispatch, 0); return; }
      dispatch();
    },
    revokeActivePlaybackObjectUrl: function () {
      if (!THUAI9WebGLBridge.activePlaybackObjectUrl) return;
      try { URL.revokeObjectURL(THUAI9WebGLBridge.activePlaybackObjectUrl); } catch (_) {}
      THUAI9WebGLBridge.activePlaybackObjectUrl = null;
    },
    isTerminalPlaybackStatus: function (payload) {
      if (!payload) return false;
      try {
        var status = typeof payload === 'string' ? JSON.parse(payload) : payload;
        if (status && status.loaded) return true;
        var text = status && status.statusText ? String(status.statusText) : '';
        return text && text.indexOf('正在') < 0;
      } catch (_) {
        return false;
      }
    }
  },
  THUAI9_SelectPlaybackFile__deps: ['$THUAI9WebGLBridge'],
  THUAI9_SelectPlaybackFile: function (gameObjectNamePtr, callbackNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr); var callbackName = UTF8ToString(callbackNamePtr);
    var input = document.createElement('input'); input.type = 'file'; input.accept = '.thuaipb,application/octet-stream'; input.style.display = 'none';
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) { input.remove(); return; }
      var size = file.size || 0;
      var name = file.name || 'playback.thuaipb';
      if (size > THUAI9WebGLBridge.maxRemotePlaybackBytes) {
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-playback-error', 'file-too-large:' + size);
        input.remove();
        return;
      }

      THUAI9WebGLBridge.revokeActivePlaybackObjectUrl();
      var url = URL.createObjectURL(file);
      THUAI9WebGLBridge.activePlaybackObjectUrl = url;
      var payload = JSON.stringify({ url: url, name: name, size: size });
      THUAI9WebGLBridge.sendMessage(gameObjectName, callbackName, payload);
      THUAI9WebGLBridge.dispatchCustomEvent('thuai9-playback-file-selected', { url: url, name: name, size: size });
      input.remove();
    };
    document.body.appendChild(input); input.click();
  },
  THUAI9_ClearDevelopmentConsole: function () {
    if (typeof console !== 'undefined' && typeof console.clear === 'function') console.clear();
  },
  THUAI9_NotifyUnityReady__deps: ['$THUAI9WebGLBridge'],
  THUAI9_NotifyUnityReady: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr); window.THUAI9Unity = window.THUAI9Unity || {}; window.THUAI9Unity.gameObjectName = gameObjectName;
    window.THUAI9Unity.sendMessage = function (methodName, payload) { THUAI9WebGLBridge.sendMessage(gameObjectName, methodName, payload || ''); };
    window.THUAI9Unity.setPlaybackFile = function (url, name) {
      var selection = (url && typeof url === 'object')
        ? url
        : { url: THUAI9WebGLBridge.normalizeUrl(url), name: name || url };
      if (selection.url) selection.url = THUAI9WebGLBridge.normalizeUrl(selection.url);
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'SetPlaybackFile', JSON.stringify(selection));
    };
    window.THUAI9Unity.loadPlaybackBase64 = function (base64, name) {
      var normalized = THUAI9WebGLBridge.normalizeBase64(base64);
      var estimatedBytes = THUAI9WebGLBridge.estimateBase64ByteCount(normalized);
      if (estimatedBytes > THUAI9WebGLBridge.maxBase64Bytes) {
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-playback-error', 'base64-too-large:' + estimatedBytes);
        return;
      }
      THUAI9WebGLBridge.sendMessage(gameObjectName, 'LoadPlaybackBase64', JSON.stringify({ data: normalized, name: name || 'playback.thuaipb' }));
    };
    window.THUAI9Unity.requestPlaybackFile = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'RequestPlaybackFile', ''); };
    window.THUAI9Unity.play = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'PlayPlayback', ''); };
    window.THUAI9Unity.pause = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'PausePlayback', ''); };
    window.THUAI9Unity.togglePlayPause = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'TogglePlayback', ''); };
    window.THUAI9Unity.stop = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopPlayback', ''); };
    window.THUAI9Unity.seekToFrame = function (frameIndex) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'SeekPlaybackFrame', String(frameIndex || 0)); };
    window.THUAI9Unity.setSpeed = function (speed) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'SetPlaybackSpeed', String(speed || 1)); };
    window.THUAI9Unity.stepForward = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StepPlaybackForward', ''); };
    window.THUAI9Unity.stepBackward = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StepPlaybackBackward', ''); };
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-ready', { gameObjectName: gameObjectName, mode: 'playback' });
  },
  THUAI9_DispatchUnityEvent__deps: ['$THUAI9WebGLBridge'],
  THUAI9_DispatchUnityEvent: function (eventNamePtr, payloadPtr) {
    var eventName = UTF8ToString(eventNamePtr); var payload = UTF8ToString(payloadPtr);
    var dispatch = eventName === 'playback-status'
      ? THUAI9WebGLBridge.dispatchCustomEventLater
      : THUAI9WebGLBridge.dispatchCustomEvent;
    dispatch('thuai9-unity-event', { eventName: eventName, payload: payload });
    dispatch('thuai9-' + eventName, payload);
    if (eventName === 'playback-error' || (eventName === 'playback-status' && THUAI9WebGLBridge.isTerminalPlaybackStatus(payload))) {
      THUAI9WebGLBridge.revokeActivePlaybackObjectUrl();
    }
  }
});
