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
    window.THUAI9Unity.startTrial = function (options) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StartTrial', JSON.stringify(options || {})); };
    window.THUAI9Unity.stopTrial = function () { THUAI9WebGLBridge.sendMessage(gameObjectName, 'StopTrial', ''); };
    window.THUAI9Unity.trialAction = function (action) { THUAI9WebGLBridge.sendMessage(gameObjectName, 'TrialAction', action || ''); };
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-ready', { gameObjectName: gameObjectName, mode: 'trial' });
  },
  THUAI9_DispatchUnityEvent__deps: ['$THUAI9WebGLBridge'],
  THUAI9_DispatchUnityEvent: function (eventNamePtr, payloadPtr) {
    var eventName = UTF8ToString(eventNamePtr); var payload = UTF8ToString(payloadPtr);
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-unity-event', { eventName: eventName, payload: payload });
    THUAI9WebGLBridge.dispatchCustomEvent('thuai9-' + eventName, payload);
  }
});
