(function () {
  'use strict';

  // --- Sistema de enmascaramiento de funciones nativas ---
  const customNativeStrings = new WeakMap();
  const originalFunctionToString = Function.prototype.toString;

  try {
    Object.defineProperty(Function.prototype, 'toString', {
      value: function () {
        if (customNativeStrings.has(this)) {
          return customNativeStrings.get(this);
        }
        return originalFunctionToString.apply(this, arguments);
      },
      writable: true,
      configurable: true,
      enumerable: false
    });
  } catch (e) {}

  function makeNative(fn, name) {
    try {
      const nativeStr = `function ${name || 'anonymous'}() { [native code] }`;
      customNativeStrings.set(fn, nativeStr);
      Object.defineProperty(fn, 'name', { value: name, configurable: true });
    } catch (e) {}
    return fn;
  }

  // --- Generador dinámico de pantalla única ---
  function getMockScreen() {
    const rawW = (window.screen && window.screen.width > 0) ? window.screen.width : 1920;
    const rawH = (window.screen && window.screen.height > 0) ? window.screen.height : 1080;
    const rawAw = (window.screen && window.screen.availWidth > 0) ? window.screen.availWidth : rawW;
    const rawAh = (window.screen && window.screen.availHeight > 0) ? window.screen.availHeight : (rawH - 40);
    const dpr = window.devicePixelRatio || 1;
    const cd = (window.screen && window.screen.colorDepth) ? window.screen.colorDepth : 24;

    return Object.freeze({
      availHeight: rawAh,
      availLeft: 0,
      availTop: 0,
      availWidth: rawAw,
      colorDepth: cd,
      devicePixelRatio: dpr,
      height: rawH,
      isExtended: false,
      isInternal: true,
      isPrimary: true,
      label: `Primary Display (${rawW}x${rawH})`,
      left: 0,
      orientation: Object.freeze({ angle: 0, type: "landscape-primary" }),
      pixelDepth: cd,
      top: 0,
      width: rawW
    });
  }

  const eventDelegate = new EventTarget();
  let oncurrentscreenchangeHandler = null;
  let onscreenschangeHandler = null;

  const mockScreenDetails = {
    get currentScreen() {
      return getMockScreen();
    },
    get screens() {
      return Object.freeze([getMockScreen()]);
    },

    get oncurrentscreenchange() {
      return oncurrentscreenchangeHandler;
    },
    set oncurrentscreenchange(handler) {
      oncurrentscreenchangeHandler = typeof handler === 'function' ? handler : null;
    },

    get onscreenschange() {
      return onscreenschangeHandler;
    },
    set onscreenschange(handler) {
      onscreenschangeHandler = typeof handler === 'function' ? handler : null;
    },

    addEventListener: makeNative(function (type, listener, options) {
      if (type === 'screenschange' || type === 'currentscreenchange') {
        return;
      }
      try {
        return eventDelegate.addEventListener(type, listener, options);
      } catch (e) {}
    }, 'addEventListener'),

    removeEventListener: makeNative(function (type, listener, options) {
      try {
        return eventDelegate.removeEventListener(type, listener, options);
      } catch (e) {}
    }, 'removeEventListener'),

    dispatchEvent: makeNative(function (event) {
      try {
        return eventDelegate.dispatchEvent(event);
      } catch (e) {
        return true;
      }
    }, 'dispatchEvent')
  };

  const isExtendedGetter = makeNative(function () {
    return false;
  }, 'get isExtended');

  const zeroOffsetGetter = makeNative(function () {
    return 0;
  }, 'get zeroOffset');

  const mockGetScreenDetails = makeNative(function () {
    return Promise.resolve(mockScreenDetails);
  }, 'getScreenDetails');

  // --- Parcheo del contexto de ventana (window y frames) ---
  function patchWindowContext(win) {
    if (!win) return;

    // 1. Desactivar navigator.webdriver cuando Chrome corre con --enable-automation
    try {
      if (win.navigator) {
        try {
          Object.defineProperty(Object.getPrototypeOf(win.navigator), 'webdriver', {
            get: () => undefined,
            configurable: true
          });
        } catch (e) {}
        try {
          delete win.navigator.webdriver;
        } catch (e) {}
      }
    } catch (e) {}

    // 2. Parchear Screen.prototype
    try {
      if (win.Screen && win.Screen.prototype) {
        Object.defineProperty(win.Screen.prototype, 'isExtended', {
          get: isExtendedGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.Screen.prototype, 'availLeft', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.Screen.prototype, 'availTop', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.Screen.prototype, 'left', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.Screen.prototype, 'top', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
      }
    } catch (e) {}

    // 3. Parchear instancia win.screen directamente
    try {
      if (win.screen) {
        Object.defineProperty(win.screen, 'isExtended', {
          get: isExtendedGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.screen, 'availLeft', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.screen, 'availTop', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.screen, 'left', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
        Object.defineProperty(win.screen, 'top', {
          get: zeroOffsetGetter,
          configurable: true,
          enumerable: true
        });
      }
    } catch (e) {}

    // 4. Parchear APIs de múltiples pantallas: getScreenDetails y getScreens
    try {
      Object.defineProperty(win, 'getScreenDetails', {
        value: mockGetScreenDetails,
        writable: true,
        configurable: true,
        enumerable: true
      });
      Object.defineProperty(win, 'getScreens', {
        value: mockGetScreenDetails,
        writable: true,
        configurable: true,
        enumerable: true
      });
    } catch (e) {}

    // 5. Parchear permissions.query para window-management / window-placement
    try {
      if (win.navigator && win.navigator.permissions && win.navigator.permissions.query) {
        const origQuery = win.navigator.permissions.query;
        win.navigator.permissions.query = makeNative(function (descriptor) {
          if (descriptor && (descriptor.name === 'window-management' || descriptor.name === 'window-placement')) {
            return Promise.resolve({
              state: 'granted',
              name: descriptor.name,
              onchange: null,
              addEventListener: makeNative(function () {}, 'addEventListener'),
              removeEventListener: makeNative(function () {}, 'removeEventListener'),
              dispatchEvent: makeNative(function () { return true; }, 'dispatchEvent')
            });
          }
          return origQuery.apply(this, arguments);
        }, 'query');
      }
    } catch (e) {}

    // 6. Sanitizar coordenadas de ventana si se mueve a un segundo monitor físico
    try {
      function clampCoord(val, maxVal) {
        if (typeof val !== 'number') return 0;
        if (val < 0) return 0;
        if (maxVal > 0 && val >= maxVal) return (val % maxVal);
        return val;
      }

      if (win.screenX !== undefined) {
        const origX = win.screenX;
        Object.defineProperty(win, 'screenX', {
          get: makeNative(function () {
            return clampCoord(origX, win.screen?.width || 1920);
          }, 'get screenX'),
          configurable: true,
          enumerable: true
        });
      }
      if (win.screenLeft !== undefined) {
        const origLeft = win.screenLeft;
        Object.defineProperty(win, 'screenLeft', {
          get: makeNative(function () {
            return clampCoord(origLeft, win.screen?.width || 1920);
          }, 'get screenLeft'),
          configurable: true,
          enumerable: true
        });
      }
    } catch (e) {}

    // 7. Estandarizar displaySurface a 'monitor' en getDisplayMedia
    try {
      if (win.MediaStreamTrack && win.MediaStreamTrack.prototype) {
        const origGetSettings = win.MediaStreamTrack.prototype.getSettings;
        win.MediaStreamTrack.prototype.getSettings = makeNative(function () {
          const settings = origGetSettings.apply(this, arguments) || {};
          if (this.kind === 'video') {
            settings.displaySurface = 'monitor';
          }
          return settings;
        }, 'getSettings');
      }
    } catch (e) {}
  }

  // Aplicar sobre la ventana principal
  patchWindowContext(window);

  // Propagar a iframes dinámicos
  try {
    if (window.HTMLIFrameElement && window.HTMLIFrameElement.prototype) {
      const origContentWindowGetter = Object.getOwnPropertyDescriptor(
        window.HTMLIFrameElement.prototype,
        'contentWindow'
      )?.get;

      if (origContentWindowGetter) {
        Object.defineProperty(window.HTMLIFrameElement.prototype, 'contentWindow', {
          get: makeNative(function () {
            const win = origContentWindowGetter.apply(this, arguments);
            if (win) {
              try { patchWindowContext(win); } catch (e) {}
            }
            return win;
          }, 'get contentWindow'),
          configurable: true,
          enumerable: true
        });
      }
    }
  } catch (e) {}

  try {
    const originalAppendChild = Element.prototype.appendChild;
    Element.prototype.appendChild = makeNative(function (child) {
      const result = originalAppendChild.apply(this, arguments);
      if (child && child.tagName === 'IFRAME' && child.contentWindow) {
        try { patchWindowContext(child.contentWindow); } catch (e) {}
      }
      return result;
    }, 'appendChild');
  } catch (e) {}
})();
