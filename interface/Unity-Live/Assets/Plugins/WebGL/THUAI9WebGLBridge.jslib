mergeInto(LibraryManager.library, {
  $THUAI9WebGLBridge: {
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
    dispatchCustomEvent: function (eventName, detail) {
      if (typeof window.CustomEvent === 'function') { window.dispatchEvent(new CustomEvent(eventName, { detail: detail })); return; }
      var event = document.createEvent('CustomEvent'); event.initCustomEvent(eventName, false, false, detail); window.dispatchEvent(event);
    }
  },
  THUAI9_NotifyUnityReady__deps: ['$THUAI9WebGLBridge'],
  THUAI9_NotifyUnityReady: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr); window.THUAI9Unity = window.THUAI9Unity || {}; window.THUAI9Unity.gameObjectName = gameObjectName;
    window.THUAI9Unity.sendMessage = function (methodName, payload) { THUAI9WebGLBridge.sendMessage(gameObjectName, methodName, payload || ''); };
    window.THUAI9Unity.startWebLive = function (sourceName) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StartWebLive', sourceName || 'WebGL Live'); };
    window.THUAI9Unity.stopWebLive = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopWebLive', ''); };
    window.THUAI9Unity.submitLiveFrameBase64 = function (base64) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameBase64', base64 || ''); };
    window.THUAI9Unity.submitLiveFrameJson = function (json) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameJson', typeof json === 'string' ? json : JSON.stringify(json)); };
    window.THUAI9Unity.connectLiveWebSocket = function (webSocketUrl) {
      if (!webSocketUrl) { THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-error', 'missing-url'); return; }
      if (window.THUAI9Unity.liveSocket) window.THUAI9Unity.liveSocket.close();
      var socket = new WebSocket(webSocketUrl); window.THUAI9Unity.liveSocket = socket; socket.binaryType = 'arraybuffer';
      socket.onopen = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StartWebLive', 'WebSocket ' + webSocketUrl); THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-open', webSocketUrl); };
      socket.onmessage = function (event) { if (typeof event.data === 'string') { var payload = event.data.trim(); THUAI9WebGLBridge.sendMessage(gameObjectName, payload.indexOf('{') === 0 ? 'SubmitLiveFrameJson' : 'SubmitLiveFrameBase64', payload); return; } THUAI9WebGLBridge.sendMessage(gameObjectName, 'SubmitLiveFrameBase64', THUAI9WebGLBridge.arrayBufferToBase64(event.data)); };
      socket.onerror = function () { THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-error', webSocketUrl); };
      socket.onclose = function () {
        if (window.THUAI9Unity.liveSocket === socket) {
          window.THUAI9Unity.liveSocket = null;
          THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopWebLive', '');
        }
        THUAI9WebGLBridge.dispatchCustomEvent('thuai9-live-socket-close', webSocketUrl);
      };
    };
    window.THUAI9Unity.disconnectLiveWebSocket = function () { if (window.THUAI9Unity.liveSocket) { window.THUAI9Unity.liveSocket.close(); window.THUAI9Unity.liveSocket = null; } THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopWebLive', ''); };
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-ready', { gameObjectName: gameObjectName, mode: 'live' });
  },
  THUAI9_DispatchUnityEvent__deps: ['$THUAI9WebGLBridge'],
  THUAI9_DispatchUnityEvent: function (eventNamePtr, payloadPtr) {
    var eventName = UTF8ToString(eventNamePtr); var payload = UTF8ToString(payloadPtr);
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-event', { eventName: eventName, payload: payload });
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-' + eventName, payload);
  }
});
