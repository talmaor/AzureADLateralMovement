using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using AzureActiveDirectoryApplication.TokenStorage;
using EnsureThat;
using Humanizer;
using Microsoft.Ajax.Utilities;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;
using NLog;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace AzureActiveDirectoryApplication.Utils
{
    public class MicrosoftGraphApiHelper
    {
        private static readonly NLog.Logger Logger = LogManager.GetCurrentClassLogger();


        public static readonly List<string> DeviceOwnerGroupDisplayNames = new List<string>
            {"Company Administrator", "Cloud Device Administrator"};

        private readonly GraphServiceClient _graphServiceClient;
        private readonly HttpContextBase _httpContext;

        public MicrosoftGraphApiHelper(HttpContextBase httpContext)
        {
            _httpContext = httpContext;
            _graphServiceClient = GetGraphClient().Result;
        }

        public async Task<GraphServiceClient> GetGraphClient()
        {
            var token = await GetAccessToken(_httpContext);
            if (string.IsNullOrEmpty(token)) return null;

            return new GraphServiceClient(
                new DelegateAuthenticationProvider(
                    requestMessage =>
                    {
                        requestMessage.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", token);

                        return Task.FromResult(0);
                    }));
        }

        public async Task<string> GetAccessToken(HttpContextBase httpContextBase)
        {
            string accessToken = null;

            // Load the app config from web.config
            var appId = ConfigurationManager.AppSettings["ida:AppId"];
            var appPassword = ConfigurationManager.AppSettings["ida:AppPassword"];
            var redirectUri = ConfigurationManager.AppSettings["ida:RedirectUri"];
            var scopes = ConfigurationManager.AppSettings["ida:AppScopes"]
                .Replace(' ', ',').Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);

            // Get the current user's ID
            var userId = ClaimsPrincipal.Current.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Get the user's token cache
                var tokenCache = new SessionTokenCache(userId, httpContextBase);

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
            }

            return accessToken;
        }

        public async Task<IGraphServiceGroupsCollectionPage> GetGroups()
        {
            return await _graphServiceClient.Groups.Request()
                .Select("Id,displayName")
                .GetAsync();
        }

        public async Task<IGraphServiceUsersCollectionPage> GetUsers()
        {
            return await _graphServiceClient.Users.Request()
                .Select("Id,displayName,userPrincipalName,mail")
                .GetAsync();
        }

        public async Task<IGraphServiceDomainsCollectionPage> GetDomains()
        {
            return await _graphServiceClient.Domains.Request()
                .Select("Id")
                .GetAsync();
        }

        public async Task<IGraphServiceDevicesCollectionPage> GetDevices()
        {
            return await _graphServiceClient.Devices.Request()
                .Select("Id,DeviceId,displayName,trustType")
                .GetAsync();
        }

        public async Task<IGraphServiceDirectoryRolesCollectionPage> GetDirectoryRoles()
        {
            return await _graphServiceClient.DirectoryRoles.Request()
                .Select("Id,displayName,roleTemplateId")
                .GetAsync();
        }

        public async Task<IAuditLogRootSignInsCollectionPage> GetSignIns()
        {
            return await _graphServiceClient.AuditLogs.SignIns.Request()
                .GetAsync();
        }

        public async Task<IGroupMembersCollectionWithReferencesPage> GetGroupMembers(string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await _graphServiceClient.Groups[id].Members.Request().GetAsync();
        }

        public async Task<IDirectoryRoleMembersCollectionWithReferencesPage> GetDirectoryRoleMembers(string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await _graphServiceClient.DirectoryRoles[id].Members.Request().GetAsync();
        }

        public async Task<IDeviceRegisteredOwnersCollectionWithReferencesPage> GetRegisteredOwners(string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await _graphServiceClient.Devices[id].RegisteredOwners.Request().GetAsync();
        }

        public async Task<List<JToken>> GetAppsPermission()
        {
            var permissionGrants = await GetGraphDataPrivate("https://graph.microsoft.com/beta/oAuth2Permissiongrants");
            return permissionGrants
                .Where(_ => _["expiryTime"].Value<DateTime>().IsNotOlderThan(100.Days()))
                .ToList();
        }

        public async Task<List<JToken>> GetServicePrincipals()
        {
            var servicePrincipals = await GetGraphDataPrivate("https://graph.microsoft.com/beta/serviceprincipals");
            return servicePrincipals.ToList();
        }

        private async Task<JToken> GetGraphDataPrivate(string graphUrl)
        {
            JToken trendingResponseBody = null;
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, graphUrl);
                await _graphServiceClient.AuthenticationProvider.AuthenticateRequestAsync(httpRequestMessage);
                var response = await _graphServiceClient.HttpProvider.SendAsync(httpRequestMessage);
                var content = await response.Content.ReadAsStringAsync();
                trendingResponseBody = JObject.Parse(content).GetValue("value");
                
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{nameof(GetGraphDataPrivate)} {ex.Message} {ex.InnerException}");
            }

            return trendingResponseBody;
        }
    }
}