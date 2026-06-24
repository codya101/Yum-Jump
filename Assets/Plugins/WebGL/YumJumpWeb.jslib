// WebGL-only bridge for "exit game". In a browser there is no app to quit:
// Application.Quit() just halts Unity's player loop and leaves a frozen,
// black canvas. The game is embedded in the site's /play iframe, so the
// natural "exit" is to send the whole page back to the site home.
mergeInto(LibraryManager.library, {
  YumJumpExitToSite: function () {
    try {
      // Top-level browsing context (the site around the /play iframe).
      (window.top || window).location.href = '/';
    } catch (e) {
      // Cross-origin top frame or opened standalone: navigate this frame.
      window.location.href = '/';
    }
  },
});
