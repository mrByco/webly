import { Routes } from '@angular/router';
import { AppRoutes } from './app.routes.paths';
import { authGuard } from './guards/auth.guard';
import { verifiedGuard } from './guards/verified.guard';

export const routes: Routes = [
  {
    path: AppRoutes.login.path,
    loadComponent: () => import('./pages/login/login').then(m => m.LoginPage),
  },
  {
    path: AppRoutes.register.path,
    loadComponent: () => import('./pages/register/register').then(m => m.RegisterPage),
  },
  {
    path: AppRoutes.forgotPassword.path,
    loadComponent: () => import('./pages/forgot-password/forgot-password').then(m => m.ForgotPasswordPage),
  },
  {
    // Unguarded: the link is usually opened in a browser that has never signed in, and demanding a
    // login before letting someone set a new password would defeat the point of the flow.
    path: AppRoutes.resetPassword.path,
    loadComponent: () => import('./pages/reset-password/reset-password').then(m => m.ResetPasswordPage),
  },
  {
    // Also unguarded, for the same reason — the verification link may well arrive on a phone.
    path: AppRoutes.verifyEmail.path,
    loadComponent: () => import('./pages/verify-email/verify-email').then(m => m.VerifyEmailPage),
  },
  {
    path: AppRoutes.account.path,
    canActivate: [authGuard, verifiedGuard],
    loadComponent: () => import('./pages/account/account').then(m => m.AccountPage),
  },
  {
    path: AppRoutes.newSite.path,
    canActivate: [authGuard, verifiedGuard],
    loadComponent: () => import('./pages/new-site/new-site').then(m => m.NewSitePage),
  },
  {
    // One shell, four children. The shell owns the site — it loads it once, holds the publish button
    // and the switcher — so moving between the editor, the history and the domains does not reload it
    // and cannot show a header that disagrees with the pane below it.
    path: AppRoutes.site.path,
    canActivate: [authGuard, verifiedGuard],
    loadComponent: () => import('./pages/site-editor/site-editor').then(m => m.SiteEditorPage),
    children: [
      {
        path: AppRoutes.siteHistory.childPath,
        loadComponent: () => import('./pages/site-history/site-history').then(m => m.SiteHistoryPage),
      },
      {
        path: AppRoutes.siteCode.childPath,
        loadComponent: () => import('./pages/site-code/site-code').then(m => m.SiteCodePage),
      },
      {
        path: AppRoutes.siteMessages.childPath,
        loadComponent: () => import('./pages/site-messages/site-messages').then(m => m.SiteMessagesPage),
      },
      {
        path: AppRoutes.siteDomains.childPath,
        loadComponent: () => import('./pages/site-domains/site-domains').then(m => m.SiteDomainsPage),
      },
      {
        path: AppRoutes.siteSettings.childPath,
        loadComponent: () => import('./pages/site-settings/site-settings').then(m => m.SiteSettingsPage),
      },
    ],
  },
  {
    path: AppRoutes.home.path,
    canActivate: [authGuard, verifiedGuard],
    loadComponent: () => import('./pages/sites/sites').then(m => m.SitesPage),
  },
  {
    path: '**',
    redirectTo: '',
  },
];
