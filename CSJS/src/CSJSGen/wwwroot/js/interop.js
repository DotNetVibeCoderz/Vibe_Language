// Small browser-side helpers the Blazor components call through IJSRuntime.
// Everything here is DOM work that has no C# equivalent inside a WebView.
//
// Gravicode Studios — led by Kang Fadhil.

window.csjsgen = {
  /** Applies the theme by setting the attribute the stylesheet keys off. */
  setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
  },

  /** Scrolls the transcript to the newest message. */
  scrollToBottom(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollTop = el.scrollHeight;
  },

  /** True when the transcript is already near the bottom, so auto-scroll does not fight the user. */
  isNearBottom(selector, threshold = 120) {
    const el = document.querySelector(selector);
    if (!el) return true;
    return el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
  },

  /** Keeps the code gutter aligned with the textarea as it scrolls. */
  syncGutter(inputSelector, gutterSelector) {
    const input = document.querySelector(inputSelector);
    const gutter = document.querySelector(gutterSelector);
    if (!input || !gutter) return;

    input.addEventListener('scroll', () => {
      gutter.scrollTop = input.scrollTop;
    });
  },

  /** Reports the caret's line and column, for the status bar. */
  caretPosition(selector) {
    const el = document.querySelector(selector);
    if (!el) return { line: 1, column: 1 };

    const upToCaret = el.value.slice(0, el.selectionStart);
    const lines = upToCaret.split('\n');
    return { line: lines.length, column: lines[lines.length - 1].length + 1 };
  },

  /** Moves the caret to the start of a line, for go-to-line. */
  goToLine(selector, line) {
    const el = document.querySelector(selector);
    if (!el) return;

    const lines = el.value.split('\n');
    const target = Math.max(1, Math.min(line, lines.length));

    let offset = 0;
    for (let i = 0; i < target - 1; i++) offset += lines[i].length + 1;

    el.focus();
    el.setSelectionRange(offset, offset + (lines[target - 1]?.length ?? 0));

    // Put the target line near the middle rather than at the very top edge.
    const lineHeight = parseFloat(getComputedStyle(el).lineHeight) || 20;
    el.scrollTop = Math.max(0, (target - 1) * lineHeight - el.clientHeight / 2);
  },

  /** Returns the current selection, used by search and replace. */
  getSelection(selector) {
    const el = document.querySelector(selector);
    if (!el) return '';
    return el.value.slice(el.selectionStart, el.selectionEnd);
  },

  /** Selects a range, so a search hit is visible and replaceable. */
  selectRange(selector, start, end) {
    const el = document.querySelector(selector);
    if (!el) return;

    el.focus();
    el.setSelectionRange(start, end);

    const before = el.value.slice(0, start).split('\n').length;
    const lineHeight = parseFloat(getComputedStyle(el).lineHeight) || 20;
    el.scrollTop = Math.max(0, (before - 1) * lineHeight - el.clientHeight / 2);
  },

  /**
   * Inserts text at the caret, preserving undo history where the browser supports it.
   * execCommand is deprecated but is still the only way to keep the undo stack intact.
   */
  insertAtCaret(selector, text) {
    const el = document.querySelector(selector);
    if (!el) return;

    el.focus();
    if (!document.execCommand('insertText', false, text)) {
      const { selectionStart: start, selectionEnd: end } = el;
      el.value = el.value.slice(0, start) + text + el.value.slice(end);
      el.setSelectionRange(start + text.length, start + text.length);
      el.dispatchEvent(new Event('input', { bubbles: true }));
    }
  },

  async copyText(text) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  },

  async readClipboard() {
    try {
      return await navigator.clipboard.readText();
    } catch {
      return '';
    }
  },

  /** Wires the copy buttons the Markdown renderer emits on code blocks. */
  bindCodeCopy() {
    document.addEventListener('click', async (event) => {
      const button = event.target.closest('[data-copy]');
      if (!button) return;

      const code = button.closest('.code-block')?.querySelector('code');
      if (!code) return;

      await navigator.clipboard.writeText(code.textContent ?? '');

      const original = button.textContent;
      button.textContent = 'Tersalin';
      setTimeout(() => { button.textContent = original; }, 1400);
    });
  },

  focus(selector) {
    document.querySelector(selector)?.focus();
  },
};

window.csjsgen.bindCodeCopy();

/**
 * Forwards application shortcuts to Blazor.
 *
 * The editor handles its own keys because it owns the caret, but menu shortcuts have to work
 * wherever focus happens to be — otherwise the accelerators printed in the menu are a promise
 * the app does not keep.
 */
window.csjsgen.bindShortcuts = function (dotnet) {
  document.addEventListener('keydown', (event) => {
    // Build and run need no modifier, the way they do not in any other editor.
    if (event.key === 'F6' || event.key === 'F5') {
      event.preventDefault();
      // Shift+F5 previews in a real browser; Shift+F6 stops what is running.
      const command = event.key === 'F6'
        ? (event.shiftKey ? 'stop' : 'compile')
        : (event.shiftKey ? 'run-browser' : 'run');
      dotnet.invokeMethodAsync('OnShortcut', command);
      return;
    }

    if (!event.ctrlKey) return;

    const key = event.key.toLowerCase();
    const shift = event.shiftKey;

    let command = null;
    if (shift && key === 'n') command = 'new-project';
    else if (shift && key === 'o') command = 'open-project';
    else if (shift && key === 's') command = 'save-all';
    else if (!shift && key === 'n') command = 'new-file';
    else if (!shift && key === 'o') command = 'open-file';

    if (!command) return;

    event.preventDefault();
    dotnet.invokeMethodAsync('OnShortcut', command);
  });
};

/**
 * Drives a panel resize.
 *
 * Pointer movement is tracked here rather than through Blazor: a pointermove per frame crossing
 * the interop boundary lags behind the cursor visibly, and the only thing .NET needs is the
 * resulting width.
 */
window.csjsgen.beginResize = function (dotnet, startX, startWidth, growsLeft) {
  const previousCursor = document.body.style.cursor;
  const previousSelect = document.body.style.userSelect;

  document.body.style.cursor = 'col-resize';
  // Without this, dragging selects the text under the cursor as it moves.
  document.body.style.userSelect = 'none';

  let frame = 0;
  let pending = startWidth;

  function onMove(event) {
    const delta = event.clientX - startX;
    pending = startWidth + (growsLeft ? -delta : delta);

    // One update per animation frame; pointermove fires far more often than the UI can redraw.
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      dotnet.invokeMethodAsync('OnResize', pending);
    });
  }

  function onUp() {
    window.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    window.removeEventListener('pointercancel', onUp);

    if (frame) cancelAnimationFrame(frame);
    document.body.style.cursor = previousCursor;
    document.body.style.userSelect = previousSelect;

    dotnet.invokeMethodAsync('OnResizeEnd');
  }

  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);
  window.addEventListener('pointercancel', onUp);
};
