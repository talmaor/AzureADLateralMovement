using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AzureActiveDirectoryApplication.Utils;
using AzureAdLateralMovement.Utils;
using EnsureThat;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.Graph;
using AzureAdLateralMovement;
using MoreLinq.Extensions;
using Newtonsoft.Json.Linq;

namespace AzureAdLateralMovement.Helpers
{
    public static class GraphServiceHelper
    {
        private const int _maxPageRequestsPerTenant = 10;

        public static async Task<List<Group>> GetGroupsAsync(GraphServiceClient graphClient, HttpContext httpContext)
        {
            IGroupDeltaCollectionPage groupsPage = new GroupDeltaCollectionPage();
            groupsPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Groups.
                    Delta().
                    Request().
                    Select("Id,displayName").
                    RequestUrl);

            var pageRequestCount = 0;
            var groups = new List<Group>();
            do
            {
                groupsPage = await groupsPage.NextPageRequest.GetAsync(CancellationToken.None);
                groupsPage.ForEach(_ => groups.Add(_));
                pageRequestCount++;
            }
            while (groupsPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return groups;
        }

        public static async Task<List<string>> GetUsersAsync(GraphServiceClient graphClient, HttpContext httpContext)
        {
            IUserDeltaCollectionPage userPage = new UserDeltaCollectionPage();
            userPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Users.
                    Delta().
                    Request().
                    Select("Id,displayName,userPrincipalName,mail").
                    RequestUrl);

            var pageRequestCount = 0;
            var userIds = new List<string>();
            do
            {
                userPage = await userPage.NextPageRequest.GetAsync(CancellationToken.None);
                userPage
                    .Where(_ => _.DisplayName != null)
                    .ForEach(_ =>
                    {
                        if (Startup.IsCosmosDbGraphEnabled)
                        {
                            CosmosDbGraphHelper.Users(_);
                            userIds.Add(_.Id);
                        }
                    });

                pageRequestCount++;
            }
            while (userPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return userIds;
        }

        public static async Task<List<Domain>> GetDomains(GraphServiceClient graphClient, HttpContext httpContext)
        {
            IGraphServiceDomainsCollectionPage domainsCollectionPage = new GraphServiceDomainsCollectionPage();
            domainsCollectionPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Domains.
                    Request().
                    Select("Id").
                    RequestUrl);

            var pageRequestCount = 0;
            var domains = new List<Domain>();
            do
            {
                domainsCollectionPage = await domainsCollectionPage.NextPageRequest.GetAsync(CancellationToken.None);
                domainsCollectionPage.ForEach(_ => domains.Add(_));
                pageRequestCount++;
            }
            while (domainsCollectionPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return domains;
        }

        public static async Task<List<Device>> GetDevicesAsync(GraphServiceClient graphClient, HttpContext httpContext)
        {
            IGraphServiceDevicesCollectionPage devicesPage = new GraphServiceDevicesCollectionPage();
            devicesPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Devices.
                    Request().
                    Select("Id,DeviceId,displayName,trustType").
                    RequestUrl);

            var pageRequestCount = 0;
            var devices = new List<Device>();
            do
            {
                devicesPage = await devicesPage.NextPageRequest.GetAsync(CancellationToken.None);
                devicesPage.ForEach(_ => devices.Add(_));
                pageRequestCount++;
            }
            while (devicesPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return devices;
        }

        public static async Task<List<DirectoryRole>> GetDirectoryRolesAsync(GraphServiceClient graphClient, HttpContext httpContext)
        {
            IDirectoryRoleDeltaCollectionPage directoryRolePage = new DirectoryRoleDeltaCollectionPage();
            directoryRolePage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    DirectoryRoles.
                    Delta().
                    Request().
                    Select("Id,displayName,roleTemplateId").
                    RequestUrl);

            var pageRequestCount = 0;
            var directoryRoles = new List<DirectoryRole>();
            do
            {
                directoryRolePage = await directoryRolePage.NextPageRequest.GetAsync(CancellationToken.None);
                directoryRolePage.ForEach(_ => directoryRoles.Add(_));
                pageRequestCount++;
            }
            while (directoryRolePage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return directoryRoles;
        }

        public static async Task<List<Application>> GetApplications(GraphServiceClient graphClient)
        {
            IApplicationDeltaCollectionPage applicationsPage = new ApplicationDeltaCollectionPage();
            applicationsPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Applications.
                    Delta().
                    Request().
                    Select("Id,displayName,appId").
                    RequestUrl);

            var pageRequestCount = 0;
            var applications = new List<Application>();
            do
            {
                applicationsPage = await applicationsPage.NextPageRequest.GetAsync(CancellationToken.None);
                applicationsPage.ForEach(_ => applications.Add(_));
                pageRequestCount++;
            }
            while (applicationsPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return applications;
        }

        public static async Task<IAuditLogRootSignInsCollectionPage> GetSignIns(GraphServiceClient graphClient, HttpContext httpContext)
        {
            return await graphClient.AuditLogs.SignIns.Request()
                .GetAsync();
        }

        public static async Task<IAuditLogRootDirectoryAuditsCollectionPage> GetDirectoryAudits(GraphServiceClient graphClient, HttpContext httpContext)
        {
            return await graphClient.AuditLogs.DirectoryAudits.Request()
                .Filter("activityDisplayName eq 'Consent to application'")
                .GetAsync();
        }

        public static async Task<IGroupMembersCollectionWithReferencesPage> GetGroupMembers(GraphServiceClient graphClient, HttpContext httpContext,string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await graphClient.Groups[id].Members.Request().GetAsync();
        }

        public static async Task<IDirectoryRoleMembersCollectionWithReferencesPage> GetDirectoryRoleMembers(GraphServiceClient graphClient, HttpContext httpContext,string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await graphClient.DirectoryRoles[id].Members.Request().GetAsync();
        }

        public static async Task<IDeviceRegisteredOwnersCollectionWithReferencesPage> GetDeviceOwners(GraphServiceClient graphClient, HttpContext httpContext,string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            return await graphClient.Devices[id].RegisteredOwners.Request().GetAsync();
        }

        public static async Task<List<DirectoryObject>> GetApplicationsOwner(GraphServiceClient graphClient, string id)
        {
            Ensure.That(id, nameof(id)).IsNotNullOrWhiteSpace();
            
            IApplicationOwnersCollectionWithReferencesPage applicationOwnerPage = new ApplicationOwnersCollectionWithReferencesPage();
            applicationOwnerPage.InitializeNextPageRequest(
                graphClient,
                graphClient.
                    Applications[id].
                    Owners.
                    Request().
                    Select("Id,displayName,appId").
                    RequestUrl);

            var pageRequestCount = 0;
            var applications = new List<DirectoryObject>();
            do
            {
                applicationOwnerPage = await applicationOwnerPage.NextPageRequest.GetAsync(CancellationToken.None);
                applicationOwnerPage.ForEach(_ => applications.Add(_));
                pageRequestCount++;
            }
            while (applicationOwnerPage.NextPageRequest != null && pageRequestCount < _maxPageRequestsPerTenant);

            return applications;
        }

        public static async Task<IGroupOwnersCollectionWithReferencesPage> GetGroupOwnersAsync(GraphServiceClient graphClient, HttpContext httpContext, string groupId)
        {
            return await graphClient.Groups[groupId].Owners
                .Request()
                .GetAsync();
        }

        public static async Task<List<JToken>> GetAppsPermission(GraphServiceClient graphClient, HttpContext httpContext)
        {
            var permissionGrants = await GetGraphDataPrivate(graphClient,httpContext,"https://graph.microsoft.com/beta/oAuth2Permissiongrants");
            return permissionGrants
                .Where(_ => _["expiryTime"].Value<DateTime>().IsNotOlderThan(100.Days()))
                .ToList();
        }

        public static async Task<List<JToken>> GetServicePrincipals(GraphServiceClient graphClient, HttpContext httpContext)
        {
            var servicePrincipals = await GetGraphDataPrivate(graphClient, httpContext,"https://graph.microsoft.com/beta/serviceprincipals");
            return servicePrincipals.ToList();
        }

        private static async Task<JToken> GetGraphDataPrivate(GraphServiceClient graphClient, HttpContext httpContext, string graphUrl)
        {
            JToken trendingResponseBody = null;
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, graphUrl);
                await graphClient.AuthenticationProvider.AuthenticateRequestAsync(httpRequestMessage);
                var response = await graphClient.HttpProvider.SendAsync(httpRequestMessage);
                var content = await response.Content.ReadAsStringAsync();
                trendingResponseBody = JObject.Parse(content).GetValue("value");

            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"{nameof(GetGraphDataPrivate)} {ex.Message} {ex.InnerException}");
            }

            return trendingResponseBody;
        }
    }
}
