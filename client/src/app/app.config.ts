import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { TitleStrategy, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideClientHydration } from '@angular/platform-browser';
import { credentialsInterceptor } from './interceptors/credentials.interceptor';
import { sessionInterceptor } from './interceptors/session.interceptor';
import { WeblyTitleStrategy } from './shared/title-strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    { provide: TitleStrategy, useClass: WeblyTitleStrategy },
    provideClientHydration(),
    provideHttpClient(withFetch(), withInterceptors([credentialsInterceptor, sessionInterceptor])),
  ]
};
