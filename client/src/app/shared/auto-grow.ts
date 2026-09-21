import { Directive, ElementRef, PLATFORM_ID, effect, inject, input } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * A textarea that grows with what is in it, up to a point, and scrolls after that.
 *
 * The chat's composer is one line high and `resize-none`, which is right for the ordinary case — most
 * messages here are a sentence — and wrong for two that are not. Adding photographs writes their paths into
 * the box, one per line, and the second one was simply cut off by the bottom of the window; and anybody who
 * writes three sentences about their business is typing into a slot that shows one of them.
 *
 * The cap matters as much as the growth. Without one, a long paragraph would push the transcript off the top
 * of the screen, which is the other way to lose what somebody is doing.
 *
 * <b>Bound to the value as well as listening for input</b>, because the case that started this is a
 * *programmatic* change: `ngModel` writing the uploaded paths into the box fires no `input` event, so a
 * directive that only listened for typing would have missed the one thing it was written for.
 */
@Directive({
  selector: 'textarea[appAutoGrow]',
  host: { '(input)': 'resize()' },
})
export class AutoGrow {
  /** The bound value, so a change from anywhere re-measures. */
  readonly appAutoGrow = input<string>('');

  /** How tall it may get before it starts scrolling, in pixels. About eight lines. */
  readonly maxHeight = input(200);

  private readonly element = inject<ElementRef<HTMLTextAreaElement>>(ElementRef);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  constructor() {
    // Reading `scrollHeight` needs a laid-out element, which the server does not have.
    effect(() => {
      this.appAutoGrow();

      if (this.isBrowser) this.resize();
    });
  }

  protected resize(): void {
    const textarea = this.element.nativeElement;

    // Collapsed first, or `scrollHeight` reports the height it already has and the box can only ever grow.
    textarea.style.height = 'auto';

    const wanted = Math.min(textarea.scrollHeight, this.maxHeight());

    textarea.style.height = `${wanted}px`;
    textarea.style.overflowY = textarea.scrollHeight > this.maxHeight() ? 'auto' : 'hidden';
  }
}
