import config from '../utils/config';
import "regenerator-runtime/runtime";
import { PublicClientApplication, LogLevel, RedirectRequest, InteractionRequiredAuthError } from "@azure/msal-browser";
import { AppInsights } from '../AppInsights';

export class AuthProvider {
  public static Application: PublicClientApplication;
  public static _accessToken: string;
  public static _idToken: string;
  
  public static _initialize() {
    AuthProvider.Application = new PublicClientApplication(
      {
        auth: {
          authority: config.authority,
          knownAuthorities: [config.authority],
          clientId: config.clientId,
          redirectUri: config.redirectUrl,
          postLogoutRedirectUri: config.redirectUrl, //???
          navigateToLoginRequestUrl: true,
          //validateAuthority: false, //???
        },
        system: {
          loggerOptions: {
            logLevel: LogLevel.Verbose,
            loggerCallback: (level: LogLevel, message: string, containsPii: boolean) => AppInsights.logEvent(message),
            piiLoggingEnabled: false
          }
        },
        cache: {
          cacheLocation: 'localStorage',
          storeAuthStateInCookie: true,
        }
      })
  }

  // Add here scopes for id token to be used at MS Identity Platform endpoints.
  public static loginRequest: RedirectRequest = {
    scopes: [config.scope],
    prompt: 'login',
  };

  public static GetAccessToken() {
    if (!AuthProvider._accessToken) {
      AuthProvider.Application.acquireTokenSilent(AuthProvider.loginRequest).then(response => {
          AuthProvider._accessToken = response.accessToken;
        }).catch(error => {
            // acquireTokenSilent can fail for a number of reasons, fallback to interaction
            if (error instanceof InteractionRequiredAuthError) {
              AuthProvider.Application.acquireTokenSilent(AuthProvider.loginRequest).then(response => {
                AuthProvider._accessToken = response.accessToken;
              });
            }
        });
    }

    return AuthProvider._accessToken;
}

  public static GetIdToken() {
    if (!AuthProvider._idToken) {
      AuthProvider.Application.acquireTokenSilent(AuthProvider.loginRequest).then(response => {
        AuthProvider._idToken = response.idToken;
      }).catch(error => {
            // acquireTokenSilent can fail for a number of reasons, fallback to interaction
            if (error instanceof InteractionRequiredAuthError) {
              AuthProvider.Application.acquireTokenSilent(AuthProvider.loginRequest).then(response => {
                AuthProvider._idToken = response.idToken;
              });
            }
        });
    }
  
    return AuthProvider._idToken;
  }
}

