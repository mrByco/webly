import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

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

  /** The backdrop was clicked. Closing is the caller's decision, so this only reports it. */
  readonly close = output<void>();
}
