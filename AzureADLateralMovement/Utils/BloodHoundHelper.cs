using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AzureActiveDirectoryApplication.Models;
using AzureActiveDirectoryApplication.Models.BloodHound;
using Microsoft.Graph;
using DirectoryRole = Microsoft.Graph.DirectoryRole;
using Domain = Microsoft.Graph.Domain;
using Group = AzureActiveDirectoryApplication.Models.BloodHound.Group;
using User = Microsoft.Graph.User;

namespace AzureActiveDirectoryApplication.Utils
{
    public static class BloodHoundHelper
    {
        private static Task _groupsWriter;
        private static Task _devicesWriter;
        private static Task _signInWriter;
        private static Task _usersWriter;
        private static Task _domainsWriter;
        private static Task _applicationsWriter;

        private static BlockingCollection<JsonBase> _groupsOutput;
        private static BlockingCollection<JsonBase> _devicesOutput;
        private static BlockingCollection<JsonBase> _signInOutput;
        private static BlockingCollection<JsonBase> _usersOutput;
        private static BlockingCollection<JsonBase> _domainsOutput;
        private static BlockingCollection<JsonBase> _applicationsOutput;

        static BloodHoundHelper()
        {
            _signInOutput = new BlockingCollection<JsonBase>();
            _signInWriter = Extensions.StartOutputWriter(_signInOutput);

            _devicesOutput = new BlockingCollection<JsonBase>();
            _devicesWriter = Extensions.StartOutputWriter(_devicesOutput);

            _groupsOutput = new BlockingCollection<JsonBase>();
            _groupsWriter = Extensions.StartOutputWriter(_groupsOutput);

            _groupsOutput = new BlockingCollection<JsonBase>();
            _groupsWriter = Extensions.StartOutputWriter(_groupsOutput);

            _usersOutput = new BlockingCollection<JsonBase>();
            _usersWriter = Extensions.StartOutputWriter(_usersOutput);

            _domainsOutput = new BlockingCollection<JsonBase>();
            _domainsWriter = Extensions.StartOutputWriter(_domainsOutput);

            _applicationsOutput = new BlockingCollection<JsonBase>();
            _applicationsWriter = Extensions.StartOutputWriter(_applicationsOutput);
        }

        private static readonly List<LocalMember> DeviceGroupOwners =
            MicrosoftGraphApiHelper.DeviceOwnerGroupDisplayNames.Select(_ => new LocalMember
            {
                Name = _.ToUpper(),
                Type = nameof(Group)
            }).ToList();

        public static void GroupMembership(Microsoft.Graph.Group _,
            List<GroupMember> groupMembers)
        {
            if (_groupsOutput.IsCompleted)
            {
                _groupsOutput = new BlockingCollection<JsonBase>();
                _groupsWriter = Extensions.StartOutputWriter(_groupsOutput);
            }

            _groupsOutput.Add(new Group
            {
                Name = _.DisplayName,
                Members = groupMembers.ToArray(),
                Properties = new Dictionary<string, object>()
            });
        }

        public static void DirectoryRoleMembership(DirectoryRole _,
            List<GroupMember> groupMembers)
        {
            if (_groupsOutput.IsCompleted)
            {
                _groupsOutput = new BlockingCollection<JsonBase>();
                _groupsWriter = Extensions.StartOutputWriter(_groupsOutput);
            }

            var properties = new Dictionary<string, object> {{nameof(_.RoleTemplateId), _.RoleTemplateId}};
            _groupsOutput.Add(new Models.BloodHound.DirectoryRole
            {
                Name = _.DisplayName,
                Members = groupMembers.ToArray(),
                Properties = properties
            });
        }

        public static void DeviceOwners(Device device,
            List<DirectoryObject> localMembers)
        {
            if (_devicesOutput.IsCompleted)
            {
                _devicesOutput = new BlockingCollection<JsonBase>();
                _devicesWriter = Extensions.StartOutputWriter(_devicesOutput);
            }

            var deviceOwners = localMembers.Select(_ => _ as User)
                .Where(_ => _ != null)
                .Select(_ => new LocalMember
                {
                    Name = _.DisplayName.ToUpper(),
                    Type = nameof(User)
                })
                .ToList();

            /* the list should be filled also with the list in (once the api supports it):
             Home -> tenant -> Devices -> Device settings -> Local administrators on devices*/
            if (device.TrustType.Equals("AzureAd", StringComparison.CurrentCultureIgnoreCase))
            {
                deviceOwners.AddRange(DeviceGroupOwners);
            }

            _devicesOutput.Add(new Computer
            {
                Name = device.DisplayName,
                LocalAdmins = deviceOwners.ToArray()
            });
        }

        public static void InteractiveLogOns(InteractiveLogon _)
        {
            if (_signInOutput.IsCompleted)
            {
                _signInOutput = new BlockingCollection<JsonBase>();
                _signInWriter = Extensions.StartOutputWriter(_signInOutput);
            }

            _signInOutput.Add(new Session
            {
                UserName = _.UserDisplayName,
                ComputerName = _.DeviceDisplayName
            });
        }

        public static void Users(User _)
        {
            if (_usersOutput.IsCompleted)
            {
                _usersOutput = new BlockingCollection<JsonBase>();
                _usersWriter = Extensions.StartOutputWriter(_usersOutput);
            }

            _usersOutput.Add(new Models.BloodHound.User
            {
                Name = _.DisplayName,
                Properties = new Dictionary<string, object>
                {
                    {nameof(_.Id), _.Id},
                    {nameof(_.UserPrincipalName), _.UserPrincipalName},
                    {nameof(_.Mail), _.Mail}
                }
            });
        }

        public static void Domains(Domain _)
        {
            if (_domainsOutput.IsCompleted)
            {
                _domainsOutput = new BlockingCollection<JsonBase>();
                _domainsWriter = Extensions.StartOutputWriter(_domainsOutput);
            }

            _domainsOutput.Add(new Models.BloodHound.Domain
            {
                Name = _.Id
            });
        }

        public static void Applications(string appId, HashSet<string> permissionsSet, string principalId)
        {
            if (_applicationsOutput.IsCompleted)
            {
                _applicationsOutput = new BlockingCollection<JsonBase>();
                _applicationsWriter = Extensions.StartOutputWriter(_applicationsOutput);
            }

            _applicationsOutput.Add(new Application
            {
                Name = appId,
                Permissions = permissionsSet.ToArray(),
                PrincipalId = principalId
            });
        }

        public static List<GroupMember> BuildGroupMembersList(
            IGroupMembersCollectionWithReferencesPage groupMembersList)
        {
            var groupMembers = new List<GroupMember>();

            foreach (var __ in groupMembersList)
                if (__ is User user)
                    groupMembers.Add(new GroupMember
                    {
                        Id = user.Id,
                        MemberName = user.DisplayName,
                        MemberType = nameof(Models.BloodHound.User)
                    });
                else if (__ is Microsoft.Graph.Group group)
                    groupMembers.Add(new GroupMember
                    {
                        Id = group.Id,
                        MemberName = group.DisplayName,
                        MemberType = nameof(Group)
                    });
                else if (__ is Device device)
                    groupMembers.Add(new GroupMember
                    {
                        Id = device.Id,
                        MemberName = device.DisplayName,
                        MemberType = nameof(Computer)
                    });
                else
                    throw new NotImplementedException();

            return groupMembers;
        }

        public static async Task Waiter()
        {
            _groupsOutput?.CompleteAdding();
            _devicesOutput?.CompleteAdding();
            _signInOutput?.CompleteAdding();
            _usersOutput?.CompleteAdding();
            _domainsOutput?.CompleteAdding();
            _applicationsOutput?.CompleteAdding();

            await _groupsWriter;
            await _devicesWriter;
            await _signInWriter;
            await _usersWriter;
            await _domainsWriter;
            await _applicationsWriter;
        }
    }
}