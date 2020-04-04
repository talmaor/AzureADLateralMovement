using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AzureActiveDirectoryApplication.Models.BloodHound;
using AzureActiveDirectoryApplication.Utils;
using AzureAdLateralMovement.Helpers;
using AzureAdLateralMovement.Models.BloodHound;
using AzureAdLateralMovement.Utils;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element;
using Microsoft.Azure.Documents.SystemFunctions;
using Microsoft.Graph;
using AzureAdLateralMovement;
using MoreLinq;
using NLog;
using ToHashSetExtension = MoreLinq.Extensions.ToHashSetExtension;
using User = Microsoft.Graph.User;

namespace AzureActiveDirectoryApplication.Models
{
    public class AzureActiveDirectoryHelper
    {
        private readonly Dictionary<string, string> _deviceObjectIdToDeviceId = new Dictionary<string, string>();
        private readonly GraphServiceClient _graphClient;
        private readonly HttpContext _httpContext;
        private readonly NLog.Logger _logger = LogManager.GetCurrentClassLogger();
        private HashSet<string> UserIds = new HashSet<string>();

        public static readonly List<string> DeviceOwnerGroupDisplayNames = new List<string>
            {"Company Administrator", "Cloud Device Administrator"};

        public AzureActiveDirectoryHelper(GraphServiceClient graphClient, HttpContext httpContext)
        {
            this._graphClient = graphClient;
            this._httpContext = httpContext;
        }

        public async Task<List<string>> RunAzureActiveDirectoryApplication()
        {
            var applications = await Applications(); //Directory.Read.All
            var deviceOwners = await DeviceOwners(); //Directory.Read.All
            var directoryRoles = await DirectoryRoles(); //Directory.Read.All
            var domains = await Domains(); //Directory.Read.All
            var groups = await Groups(); //Group.Read.All or Directory.Read.All
            var users = await Users(); //User.Read.All, Directory.Read.All,
            var interactiveLogins = await InteractiveLogins(); // AuditLog.Read.All and Directory.Read.All
            var servicePrincipals = await ServicePrincipals(); // Application.ReadWrite.All, Directory.Read.All

            await BloodHoundHelper.Waiter();

            return new List<string>
            {
                $"{nameof(DeviceOwners)} | {deviceOwners?.Count} ",
                $"{nameof(DirectoryRoles)} | {directoryRoles?.Count}",
                $"{nameof(Domains)} | {domains?.Count}",
                $"{nameof(Groups)} | {groups?.Count}",
                $"{nameof(Users)} | {users}",
                $"{nameof(InteractiveLogins)} | {interactiveLogins?.Count}",
                $"{nameof(ServicePrincipals)} | {servicePrincipals}",
                $"{nameof(Applications)} | {applications}"
            };
        }

        public async Task<HashSet<string>> DirectoryRoles()
        {
            try
            {
                var directoryRoles = await GraphServiceHelper.GetDirectoryRolesAsync(_graphClient,_httpContext);
                var userIds = await GraphServiceHelper.GetUsersAsync(_graphClient, _httpContext);

                var administrators = new HashSet<string>();
                var directoryRolesNames = new HashSet<string>();

                await directoryRoles.ForEachAsync(async _ =>
                {
                    var roleMembers = await GraphServiceHelper.GetDirectoryRoleMembers(_graphClient, _httpContext, _.Id);
                    var members = Extensions.DirectoryRoleMembersResultsToList(roleMembers);
                    BloodHoundHelper.DirectoryRoleMembership(_, members);

                    if (Startup.IsCosmosDbGraphEnabled)
                    {
                        CosmosDbGraphHelper.DirectoryRoleMembership(_, members);
                        GetDeviceAdministratorsIds(_.DisplayName, members, administrators);
                        CosmosDbGraphHelper.DirectoryRolePermissions(_, userIds, administrators);
                    }

                    directoryRolesNames.Add(_.DisplayName);
                });

                return directoryRolesNames;
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
                var groupsCollectionPage = await GraphServiceHelper.GetGroupsAsync(_graphClient, _httpContext);

                var groups = new HashSet<string>();
                await groupsCollectionPage.ForEachAsync(async _ =>
                {
                    var groupOwner = await GraphServiceHelper.GetGroupOwnersAsync(_graphClient, _httpContext, _.Id);
                    var groupOwnership = BloodHoundHelper.BuildGroupOwnership(groupOwner);

                    var groupMembersList = await GraphServiceHelper.GetGroupMembers(_graphClient, _httpContext,_.Id);
                    var groupMembers = BloodHoundHelper.BuildGroupMembersList(groupMembersList);
                    BloodHoundHelper.GroupMembership(_, groupMembers);
                    if (Startup.IsCosmosDbGraphEnabled)
                    {
                        CosmosDbGraphHelper.GroupMembership(_, groupMembers);
                        CosmosDbGraphHelper.GroupMembership(_, groupOwnership);
                    }
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

        public async Task<HashSet<DirectoryObject>> DeviceOwners()
        {
            try
            {
                var directoryRoles = await GraphServiceHelper.GetDirectoryRolesAsync(_graphClient, _httpContext);
                var devices = await GraphServiceHelper.GetDevicesAsync(_graphClient, _httpContext);
                HashSet<DirectoryObject> ownersList = new HashSet<DirectoryObject>();

                await devices.
                    Where(_ => _.DisplayName != null).
                    ForEachAsync(async _ =>
                    {
                        
                        _deviceObjectIdToDeviceId.Add(_.DeviceId, _.Id);
                        var ownerList = 
                            (await GraphServiceHelper.GetDeviceOwners(_graphClient, _httpContext,_.Id))
                            .Where(__ => __ != null)
                            .ToList();

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

        public async Task<List<InteractiveLogon>> InteractiveLogins()
        {
            try
            {
                var signIns = await GraphServiceHelper.GetSignIns(_graphClient, _httpContext);
                var interactiveLogOns = new List<InteractiveLogon>();

                signIns
                    .Where(_ => _.ClientAppUsed?.Equals("Mobile Apps and Desktop clients",
                                    StringComparison.OrdinalIgnoreCase) == true)
                    .Where(_ => _.ResourceDisplayName?.Equals("Windows Azure Active Directory",
                                    StringComparison.OrdinalIgnoreCase) == true)
                    .Where(_ => _.CreatedDateTime.HasValue &&
                                _.CreatedDateTime.Value.UtcDateTime.IsNotOlderThan(10.Days()))
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
                _logger.Error(ex, $"{nameof(InteractiveLogins)} {ex.Message} {ex.InnerException}");
                return null;
            }
        }

        public async Task<int> Users()
        {
            try
            {
                return (await GraphServiceHelper.GetUsersAsync(_graphClient, _httpContext)).Count;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(Users)} {ex.Message} {ex.InnerException}");
                return -1;
            }
        }

        public async Task<HashSet<string>> Domains()
        {
            try
            {
                var domainResults = await GraphServiceHelper.GetDomains(_graphClient, _httpContext);
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

        public async Task<int> Applications()
        {
            try
            {
                var applications = await GraphServiceHelper.GetApplications(_graphClient);
                
                applications.ForEach(async _ =>
                    {
                        try
                        {
                            var owners = await GraphServiceHelper.GetApplicationsOwner(_graphClient, _.Id);
                            CosmosDbGraphHelper.AppOwnership(_, owners);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                );

                return applications.Count;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return 0;
        }

        public async Task<int> ServicePrincipals()
        {
            try
            {
                var appsPermissions = await GraphServiceHelper.GetAppsPermission(_graphClient, _httpContext);
                var principalsPermissions = await GraphServiceHelper.GetDirectoryAudits(_graphClient, _httpContext);
                var servicePrincipals = await GraphServiceHelper.GetServicePrincipals(_graphClient, _httpContext);

                var principalIdToPermissions = new Dictionary<string, HashSet<string>>();
                principalsPermissions.ForEach(_ =>
                {
                    principalIdToPermissions.TryAdd(
                        _.TargetResources.First().Id,
                        ToHashSetExtension.ToHashSet(_.TargetResources.First().ModifiedProperties.First(__ => __.DisplayName == "ConsentAction.Permissions").NewValue.Split("Scope:").Last().
                            Split("]").First().Split(" ").Where(__ => __ != ""))
                    );
                });

                var appIdToPermissionsSetDictionary = new Dictionary<string, HashSet<string>>();
                appsPermissions.ForEach(_ =>
                {
                    var permissionsSet =
                        ToHashSetExtension.ToHashSet(Newtonsoft.Json.Linq.Extensions.Value<string>(_["scope"])
                            .Split(' '));

                    var appId = Newtonsoft.Json.Linq.Extensions.Value<string>(_["clientId"]);
                    appIdToPermissionsSetDictionary.TryAdd(appId, permissionsSet);
                });

                var appIdToNameDictionary = new Dictionary<string, Tuple<string, string, string, string>>();
                servicePrincipals.ForEach(_ =>
                    appIdToNameDictionary.Add(
                        Newtonsoft.Json.Linq.Extensions.Value<string>(_["id"]),
                        new Tuple<string, string, string,string>(
                                Newtonsoft.Json.Linq.Extensions.Value<string>(_["appId"]),
                                Newtonsoft.Json.Linq.Extensions.Value<string>(_["displayName"]),
                                Newtonsoft.Json.Linq.Extensions.Value<string>(_["homepage"]),
                                Newtonsoft.Json.Linq.Extensions.Value<string>(_["appOwnerOrganizationId"])
                                )));

                appIdToNameDictionary.ForEach(_ =>
                {
                    appIdToPermissionsSetDictionary.TryGetValue(_.Key, out var appPermissions);
                    principalIdToPermissions.TryGetValue(_.Key, out var principalPermissions);

                    if (Startup.IsCosmosDbGraphEnabled && (principalPermissions != null || appPermissions != null))
                    {
                        if (principalPermissions != null)
                        {
                            CosmosDbGraphHelper.Applications(_.Value.Item2, _.Value.Item1, principalPermissions, UserIds , _.Key, _.Value.Item3, _.Value.Item4);
                        }
                        else
                        {
                            CosmosDbGraphHelper.Applications(_.Value.Item2, _.Key, appPermissions, UserIds, _.Value.Item1, _.Value.Item3, _.Value.Item4);

                        }
                    }
                });

                return appIdToNameDictionary.Count;

            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{nameof(ServicePrincipals)} {ex.Message} {ex.InnerException}");
            }

            return 0;
        }

        private void GetDeviceAdministratorsIds(string directoryRoleDisplayName, List<GroupMember> members,
            HashSet<string> administrators)
        {
            if (DeviceOwnerGroupDisplayNames.Contains(directoryRoleDisplayName))
                administrators.UnionWith(members.Select(__ => __.Id));
        }

    }
}