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

        public async Task<List<string>> RunAzureActiveDirectoryApplication()
        {
            var deviceOwners = await DeviceOwners();
            var directoryRoles = await DirectoryRoles();
            var domains = await Domains();
            var groups = await Groups();
            var users = await Users();
            var interactiveLogons = await InteractiveLogOns();
            await AppSignIns();
            await BloodHoundHelper.Waiter();

            return new List<string>
            {
                $"{nameof(DeviceOwners)} | {deviceOwners.Count} ",
                $"{nameof(DirectoryRoles)} | {directoryRoles.ToDelimitedString(", ")}",
                $"{nameof(Domains)} | {domains.ToDelimitedString(", ")}",
                $"{nameof(Groups)} | {groups.ToDelimitedString(", ")}",
                $"{nameof(Users)} | {users.ToDelimitedString(", ")}",
                $"{nameof(InteractiveLogOns)}   | {interactiveLogons.Count()}"
            };
        }

        public async Task<HashSet<string>> DirectoryRoles()
        {
            try
            {
                var directoryRoleResults = await _microsoftGraphApiHelper.GetDirectoryRoles();
                var users = await _microsoftGraphApiHelper.GetUsers();
                var userIds = users.Select(_ => _.Id).ToList();

                var administrators = new HashSet<string>();
                var directoryRoles = new HashSet<string>();

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

                    directoryRoles.Add(_.DisplayName);
                });

                return directoryRoles;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(DirectoryRoles)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<HashSet<string>> Groups()
        {
            try
            {
                var groupsCollectionPage = await _microsoftGraphApiHelper.GetGroups();
                var groups = new HashSet<string>();
                await groupsCollectionPage.ForEachAsync(async _ =>
                {
                    var groupMembersList = await _microsoftGraphApiHelper.GetGroupMembers(_.Id);
                    var groupMembers = BloodHoundHelper.BuildGroupMembersList(groupMembersList);
                    BloodHoundHelper.GroupMembership(_, groupMembers);
                    if (Startup.IsCosmosDbGraphEnabled) CosmosDbGraphHelper.GroupMembership(_, groupMembers);
                    groups.Add(_.DisplayName);
                });

                return groups;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Groups)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<HashSet<Microsoft.Graph.DirectoryObject>> DeviceOwners()
        {
            try
            {
                var directoryRoles = await _microsoftGraphApiHelper.GetDirectoryRoles();
                var devices = await _microsoftGraphApiHelper.GetDevices();
                HashSet<Microsoft.Graph.DirectoryObject> ownersList = new HashSet<Microsoft.Graph.DirectoryObject>();

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

                        ownersList.UnionWith(ownerList);
                    });

                return ownersList;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(DeviceOwners)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<List<InteractiveLogon>> InteractiveLogOns()
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

                return interactiveLogOns;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(InteractiveLogOns)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<HashSet<string>> Users()
        {
            try
            {
                var users = await _microsoftGraphApiHelper.GetUsers();
                var usersDisplayNames = new HashSet<string>();

                users
                    .Where(_ => _.DisplayName != null)
                    .ForEach(_ =>
                    {
                        BloodHoundHelper.Users(_);
                        if (Startup.IsCosmosDbGraphEnabled)
                        {
                            CosmosDbGraphHelper.Users(_);
                        }
                        usersDisplayNames.Add(_.DisplayName);
                    });

                return usersDisplayNames;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Users)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<HashSet<string>> Domains()
        {
            try
            {
                var domainResults = await _microsoftGraphApiHelper.GetDomains();
                var domains = new HashSet<string>();
                domainResults.ForEach(_ => {
                        domains.Add(_.Id);
                        BloodHoundHelper.Domains(_);
                    });

                return domains;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Domains)} {ex.Message} {ex.InnerException}");
                return null;
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