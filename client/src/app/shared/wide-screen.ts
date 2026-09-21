import { PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Whether the two-pane screens have room for two panes, asked once.
 *
 * The Code and History tabs open on something — the home page's source, the newest version — because an empty
 * pane beside a list is a screen that asks a question instead of answering one. Below `lg` there is no pane
 * beside the list: the two stack, and one at a time is showing, so opening on the *detail* means landing on a
 * file with the list of files hidden behind a back button. On a phone the list is the answer.
 *
 * `1024px` is Tailwind's `lg`, which is the breakpoint both those templates switch on. Two places have to
 * agree about one number; this is the one that says so.
 *
 * Read once rather than watched, because it decides an initial selection and nothing else. Somebody who
 * resizes their window afterwards gets whatever was already open, which is what they would expect.
 */
export function isWideScreen(): boolean {
  return isPlatformBrowser(inject(PLATFORM_ID)) && window.matchMedia('(min-width: 1024px)').matches;
}
