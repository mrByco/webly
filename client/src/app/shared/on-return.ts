import { DestroyRef, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Calls `callback` when somebody comes back to this tab — it becomes visible again, or its window gets the focus
 * back — at most once every few seconds, in the browser only. Call it where `inject` works.
 *
 * A screen here is a picture of state that lives on the server, and other things change that state: the same site
 * open in a second tab, the phone in somebody's pocket, a turn that finished while the laptop was asleep. A turn
 * sent from one tab never reached the other, which went on showing the thread as it was when it loaded, however
 * often it was brought forward. Coming back is the moment somebody is about to read the screen again, so it is the
 * moment to make it true. Both events, because side-by-side windows never stop being visible and a tab switch does
 * not always move the focus; the gap is what stops the pair of them, which usually arrive together, fetching twice.
 */
export function onReturn(callback: () => void, gapMs = 5000): void {
  if (!isPlatformBrowser(inject(PLATFORM_ID))) return;

  // Counted from now, so the page's own first load is not followed by a second one a moment later.
  let last = Date.now();

  const returned = () => {
    if (document.visibilityState !== 'visible' || Date.now() - last < gapMs) return;

    last = Date.now();
    callback();
  };

  document.addEventListener('visibilitychange', returned);
  window.addEventListener('focus', returned);

  inject(DestroyRef).onDestroy(() => {
    document.removeEventListener('visibilitychange', returned);
    window.removeEventListener('focus', returned);
  });
}
