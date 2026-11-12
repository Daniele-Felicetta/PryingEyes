mergeInto(LibraryManager.library, {
  IsMobileBrowser: function () {
    return /Android|iPhone|iPad|iPod/i.test(navigator.userAgent) ||
      (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1) ? 1 : 0;
  }
});
