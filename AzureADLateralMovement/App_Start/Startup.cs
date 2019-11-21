using System;
using System.Configuration;
using System.IdentityModel.Claims;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.WebPages;
using AzureActiveDirectoryApplication;
using AzureActiveDirectoryApplication.TokenStorage;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Notifications;
using Microsoft.Owin.Security.OpenIdConnect;
using Owin;

[assembly: OwinStartup(typeof(Startup))]
namespace AzureActiveDirectoryApplication
{
    public class 
        Startup
    {
        public static readonly string AppId = ConfigurationManager.AppSettings["ida:AppId"];
        public static readonly string AppPassword = ConfigurationManager.AppSettings["ida:AppPassword"];
        public static string RedirectUri = ConfigurationManager.AppSettings["ida:RedirectUri"];
        public static readonly string[] Scopes = ConfigurationManager.AppSettings["ida:AppScopes"]
          .Replace(' ', ',').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        public static readonly string OutputFolderLocation = ConfigurationManager.AppSettings["ida:OutputFolderLocation"];

        public static readonly bool IsCosmosDbGraphEnabled =
            ConfigurationManager.AppSettings["ida:IsCosmosDbGraphEnabled"].AsBool();

        public void Configuration(IAppBuilder app)
        {
            if (HttpContext.Current.IsDebuggingEnabled)
            {
                RedirectUri = "http://localhost:44302/";
            }

            app.SetDefaultSignInAsAuthenticationType(CookieAuthenticationDefaults.AuthenticationType);
            app.UseCookieAuthentication(new CookieAuthenticationOptions());
            app.UseOpenIdConnectAuthentication(
              new OpenIdConnectAuthenticationOptions
              {
                  ClientId = AppId,
                  Authority = "https://login.microsoftonline.com/common/v2.0",
                  Scope = "openid offline_access profile email " + string.Join(" ", Scopes),
                  RedirectUri = RedirectUri,
                  PostLogoutRedirectUri = "/",
                  TokenValidationParameters = new TokenValidationParameters
                  {
                      // For demo purposes only, see below
                      ValidateIssuer = false

                      // In a real multitenant app, you would add logic to determine whether the
                      // issuer was from an authorized tenant
                      //ValidateIssuer = true,
                      //IssuerValidator = (issuer, token, tvp) =>
                      //{
                      //  if (MyCustomTenantValidation(issuer))
                      //  {
                      //    return issuer;
                      //  }
                      //  else
                      //  {
                      //    throw new SecurityTokenInvalidIssuerException("Invalid issuer");
                      //  }
                      //}
                  },
                  Notifications = new OpenIdConnectAuthenticationNotifications
                  {
                      AuthenticationFailed = OnAuthenticationFailed,
                      AuthorizationCodeReceived = OnAuthorizationCodeReceived
                  }
              }
            );
        }

        private Task OnAuthenticationFailed(AuthenticationFailedNotification<OpenIdConnectMessage,
          OpenIdConnectAuthenticationOptions> notification)
        {
            notification.HandleResponse();
            string redirect = "/Home/Error?message=" + notification.Exception.Message;
            if (notification.ProtocolMessage != null && !string.IsNullOrEmpty(notification.ProtocolMessage.ErrorDescription))
            {
                redirect += "&debug=" + notification.ProtocolMessage.ErrorDescription;
            }
            notification.Response.Redirect(redirect);
            return Task.FromResult(0);
        }

        public async Task<string> GetAccessToken(SessionTokenCache tokenCache)
        {
            string accessToken = null;

            // Load the app config from web.config
            var appId = ConfigurationManager.AppSettings["ida:AppId"];
            var appPassword = ConfigurationManager.AppSettings["ida:AppPassword"];
            var redirectUri = ConfigurationManager.AppSettings["ida:RedirectUri"];
            var scopes = ConfigurationManager.AppSettings["ida:AppScopes"]
                .Replace(' ', ',').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var confidentialClientApplication = new ConfidentialClientApplication(
                appId,
                redirectUri,
                new ClientCredential(appPassword),
                tokenCache.GetMsalCacheInstance(),
                null);

            // Call AcquireTokenSilentAsync, which will return the cached
            // access token if it has not expired. If it has expired, it will
            // handle using the refresh token to get a new one.
            var result = await confidentialClientApplication.AcquireTokenSilentAsync(scopes, confidentialClientApplication.Users.First());

            accessToken = result.AccessToken;

            return accessToken;
        }

        private async Task OnAuthorizationCodeReceived(AuthorizationCodeReceivedNotification notification)
        {
            // Get the signed in user's id and create a token cache
            string signedInUserId = notification.AuthenticationTicket.Identity.FindFirst(ClaimTypes.NameIdentifier).Value;
            SessionTokenCache tokenCache = new SessionTokenCache(
                signedInUserId,
                notification.OwinContext.Environment["System.Web.HttpContextBase"] as HttpContextBase);

            ConfidentialClientApplication confidentialClientApplication = 
                    new ConfidentialClientApplication(
                                                        AppId, 
                                                        RedirectUri, 
                                                        new ClientCredential(AppPassword), 
                                                        tokenCache.GetMsalCacheInstance(), 
                                                        null);

            try
            {
                var userToken = await confidentialClientApplication.AcquireTokenByAuthorizationCodeAsync(notification.Code, Scopes);
                //var appToken = GetAccessToken(tokenCache);
            }
            catch (MsalException ex)
            {
                string message = "AcquireTokenByAuthorizationCodeAsync threw an exception";
                string debug = ex.Message;
                notification.HandleResponse();
                notification.Response.Redirect("/Home/Error?message=" + message + "&debug=" + debug);
            }
        }
    }
}
