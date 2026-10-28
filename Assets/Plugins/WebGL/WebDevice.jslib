// The browser version's questions about the device it runs on (TouchControls).
mergeInto(LibraryManager.library, {
  // 1 when the screen is mainly touched (a phone or tablet, an iPad that calls itself a Mac too), else 0.
  WebTouchScreen: function () {
    try {
      var coarse = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
      return coarse && navigator.maxTouchPoints > 0 ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },
});
