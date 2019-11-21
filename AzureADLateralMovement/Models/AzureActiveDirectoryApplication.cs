using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using AzureActiveDirectoryApplication.Models.BloodHound;
using AzureActiveDirectoryApplication.Utils;
using Humanizer;
using MoreLinq;
using Newtonsoft.Json.Linq;
using NLog;

namespace AzureActiveDirectoryApplication.Models
{
    public class AzureActiveDirectoryApplication
    {
        private readonly Dictionary<string, string> _deviceObjectIdToDeviceId = new Dictionary<string, string>();
        private readonly MicrosoftGraphApiHelper _microsoftGraphApiHelper;
        private readonly NLog.Logger _logger = LogManager.GetCurrentClassLogger();

        public AzureActiveDirectoryApplication(HttpContextBase httpContext)
        {
            _microsoftGraphApiHelper = new MicrosoftGraphApiHelper(httpContext);
        }

        public async Task RunAzureActiveDirectoryApplication()
        {
            await DeviceOwners();
            await DirectoryRoles();
            await Domains();
            await Groups();
            await Users();
            await InteractiveLogOns();
            await AppSignIns();

            await BloodHoundHelper.Waiter();
        }

        public async Task DirectoryRoles()
        {
            try
            {
                var directoryRoleResults = await _microsoftGraphApiHelper.GetDirectoryRoles();
                var users = await _microsoftGraphApiHelper.GetUsers();
                var userIds = users.Select(_ => _.Id).ToList();

                var administrators = new HashSet<string>();

                await directoryRoleResults.ForEachAsync(async _ =>
                {
                    var roleMembers = await _microsoftGraphApiHelper.GetDirectoryRoleMembers(_.Id);
                    var members = Extensions.DirectoryRoleMembersResultsToList(roleMembers);
                    BloodHoundHelper.DirectoryRoleMembership(_, members);

                    if (Startup.IsCosmosDbGraphEnabled)
                    {
                        CosmosDbGraphHelper.DirectoryRoleMembership(_, members);
                        GetDeviceAdministratorsIds(_.DisplayName, members, administrators);
                        CosmosDbGraphHelper.DirectoryRolePermissions(_, userIds, administrators);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(DirectoryRoles)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task Groups()
        {
            try
            {
                var groupsCollectionPage = await _microsoftGraphApiHelper.GetGroups();

                await groupsCollectionPage.ForEachAsync(async _ =>
                {
                    var groupMembersList = await _microsoftGraphApiHelper.GetGroupMembers(_.Id);
                    var groupMembers = BloodHoundHelper.BuildGroupMembersList(groupMembersList);
                    BloodHoundHelper.GroupMembership(_, groupMembers);
                    if (Startup.IsCosmosDbGraphEnabled) CosmosDbGraphHelper.GroupMembership(_, groupMembers);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Groups)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task DeviceOwners()
        {
            try
            {
                var directoryRoles = await _microsoftGraphApiHelper.GetDirectoryRoles();
                var devices = await _microsoftGraphApiHelper.GetDevices();

                await devices.
                    Where(_ => _.DisplayName != null).
                    ForEachAsync(async _ =>
                    {
                        _deviceObjectIdToDeviceId.Add(_.DeviceId, _.Id);
                        var ownerList = (await _microsoftGraphApiHelper.GetRegisteredOwners(_.Id))
                            .Where(__ => __ != null).ToList();

                        BloodHoundHelper.DeviceOwners(_, ownerList);
                        if (Startup.IsCosmosDbGraphEnabled)
                        {
                            CosmosDbGraphHelper.DeviceOwners(_, ownerList, directoryRoles);
                        }
                    });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(DeviceOwners)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task InteractiveLogOns()
        {
            try
            {
                var signIns = await _microsoftGraphApiHelper.GetSignIns();
                var interactiveLogOns = new List<InteractiveLogon>();

                signIns
                    .Where(_ => _.ClientAppUsed?.Equals("Mobile Apps and Desktop clients",
                                    StringComparison.OrdinalIgnoreCase) == true)
                    .Where(_ => _.ResourceDisplayName?.Equals("Windows Azure Active Directory",
                                    StringComparison.OrdinalIgnoreCase) == true)
                    .Where(_ => _.CreatedDateTime.HasValue &&
                                _.CreatedDateTime.Value.UtcDateTime.IsNotOlderThan(2.Days()))
                    .ForEach(_ =>
                    {
                        interactiveLogOns.Add(
                            new InteractiveLogon(
                                _.DeviceDetail,
                                _.Location,
                                _.UserId,
                                _.CreatedDateTime.GetValueOrDefault().UtcDateTime,
                                _.UserDisplayName));
                    });

                interactiveLogOns.DistinctBy(_ => new {_.UserId, _.DeviceId});

                interactiveLogOns.
                    Where(_ => _.UserId != null && 
                               _.UserDisplayName != null && 
                               _.DeviceDisplayName != null).
                    ForEach(_ =>
                    {
                        BloodHoundHelper.InteractiveLogOns(_);
                        if (Startup.IsCosmosDbGraphEnabled)
                            CosmosDbGraphHelper.InteractiveLogOns(_, _deviceObjectIdToDeviceId);
                    });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(InteractiveLogOns)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task Users()
        {
            try
            {
                var users = await _microsoftGraphApiHelper.GetUsers();
                users
                    .Where(_ => _.DisplayName != null)
                    .ForEach(_ =>
                    {
                        BloodHoundHelper.Users(_);
                        if (Startup.IsCosmosDbGraphEnabled)
                        {
                            CosmosDbGraphHelper.Users(_);
                        }
                    });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Users)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task Domains()
        {
            try
            {
                var domainResults = await _microsoftGraphApiHelper.GetDomains();
                domainResults.ForEach(domain => BloodHoundHelper.Domains(domain));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Domains)} {ex.Message} {ex.InnerException}");
            }
        }

        public async Task AppSignIns()
        {
            try
            {
                var appsPermissions = await _microsoftGraphApiHelper.GetAppsPermission();
                var servicePrincipals = await _microsoftGraphApiHelper.GetServicePrincipals();

                var appIdToNameDictionary = new Dictionary<string, string>();
                servicePrincipals.ForEach(_ =>
                    appIdToNameDictionary.Add(
                        _["id"].Value<string>(),
                        _["appDisplayName"].Value<string>())
                );

                var appIdToPermissionsSetDictionary = new Dictionary<string, HashSet<string>>();
                appsPermissions.ForEach(_ =>
                {
                    var permissionsSet = _["scope"].Value<string>().Split(' ').ToHashSet();
                    var appId = _["clientId"].Value<string>();
                    var principalId = _["principalId"].Value<string>();
                    appIdToNameDictionary.TryGetValue(appId, out var appDisplayName);

                    appIdToPermissionsSetDictionary.CreateOrUpdate(
                        appDisplayName ?? appId,
                        () => permissionsSet,
                        __ => __.Union(permissionsSet).ToHashSet()
                    );

                    //BloodHoundHelper.Applications(appDisplayName ?? appId, permissionsSet, principalId);
                    if (Startup.IsCosmosDbGraphEnabled)
                    {
                        CosmosDbGraphHelper.Applications(appDisplayName, appId, permissionsSet, principalId);
                    }
                });

                /*
                 * Creating connections based on permissions
                foreach (var (appId, appDisplayName) in appIdToNameDictionary)
                {
                    var vertex = new GremlinVertex(appId, nameof(Application));
                    vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, appId.GetHashCode());
                    vertex.AddProperty(nameof(appDisplayName), appDisplayName?.ToUpper() ?? string.Empty);
                    gremlinVertices.Add(vertex);
                }

                var mailBoxes = new GremlinVertex("MailBoxes", "MailBoxes");
                mailBoxes.AddProperty(CosmosDbHelper.CollectionPartitionKey, "MailBoxes".GetHashCode());
                gremlinVertices.Add(mailBoxes);*/
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(AppSignIns)} {ex.Message} {ex.InnerException}");
            }
        }

        private void GetDeviceAdministratorsIds(string directoryRoleDisplayName, List<GroupMember> members,
            HashSet<string> administrators)
        {
            if (MicrosoftGraphApiHelper.DeviceOwnerGroupDisplayNames.Contains(directoryRoleDisplayName))
                administrators.UnionWith(members.Select(__ => __.Id));
        }
    }
}