import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { RouteAnnouncement } from './shared/title-strategy';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  /** The new screen's title after a client-side navigation. See `RouteAnnouncement`. */
  protected readonly announcement = inject(RouteAnnouncement).text;
}
