import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  output,
  viewChild,
} from '@angular/core';

let headings = 0;

/**
 * The app's one modal.
 *
 * Every dialog here was the same nine lines of daisyUI chrome copied again — `modal modal-open`, a
 * `modal-box`, a `modal-action` row and a backdrop button with its own aria-label — which is how
 * one of them ends up missing the backdrop or spelling the label differently.
 *
 * What is open stays in the caller's own signal rather than in a service holding a stack of
 * dynamically created components. That is how the rest of this app holds state, it keeps the
 * dialog's content statically typed and visible in the template that owns it, and it means a modal
 * cannot outlive the screen that opened it.
 *
 * <b>A native `<dialog>` opened with `showModal()`, because a keyboard could not use the old one.</b> It was a
 * `div` with `role="dialog"` drawn over the page, and drawing is all it did: focus stayed on the button that
 * opened it, so Tab walked through the screen behind the overlay — every link and field of a page nobody could
 * see — Escape did nothing, and closing it dropped focus on the page's body. Every one of these four dialogs
 * guards something that cannot be undone, which is exactly where somebody on a keyboard has to be able to tell
 * where they are. `showModal()` is the browser doing all of it: the page behind becomes inert, focus moves to
 * the dialog's first control — "Keep it" in three of them, the password in the fourth — Escape asks to close,
 * and closing returns focus to whatever opened it. It is labelled by its first heading, so a screen reader
 * announces "Delete Ridgeway Cycles?" rather than "dialog".
 *
 * The dialog stays in the DOM while closed, and that changes nothing for the callers: content projected into a
 * component is created and checked by the parent whether or not it is shown, so the four templates already
 * had to cope with being evaluated closed — and they do, with `?.` throughout.
 */
@Component({
  selector: 'app-modal',
  templateUrl: './modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Modal {
  readonly open = input.required<boolean>();

  /** Rendered as the heading. Omit it when the content brings its own. */
  readonly title = input('');

  /** The backdrop was clicked, or Escape pressed. Closing is the caller's decision, so this only reports it. */
  readonly close = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    // After rendering and in the browser only: the server draws the dialog closed, which is what it is until a
    // click opens it, and `showModal` does not exist there.
    afterRenderEffect(() => {
      const dialog = this.dialog().nativeElement;

      if (this.open() && !dialog.open) {
        dialog.showModal();
        this.labelByHeading(dialog);
      } else if (!this.open() && dialog.open) {
        dialog.close();
      }
    });
  }

  /** Escape. Held open and reported, like the backdrop: the caller's signal is what says whether it is open. */
  protected dismiss(event: Event): void {
    event.preventDefault();
    this.close.emit();
  }

  /**
   * The browser closed it without asking — a second Escape in a row is not cancellable, by design, so a page cannot
   * trap somebody in a dialog. Tell the caller, or its signal would go on saying open over a closed dialog.
   */
  protected closed(): void {
    if (this.open()) this.close.emit();
  }

  private labelByHeading(dialog: HTMLDialogElement): void {
    const heading = dialog.querySelector<HTMLElement>('h1, h2, h3');

    if (!heading) return;

    heading.id ||= `modal-heading-${++headings}`;
    dialog.setAttribute('aria-labelledby', heading.id);
  }
}
