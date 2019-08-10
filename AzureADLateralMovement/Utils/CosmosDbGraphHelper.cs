using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using AzureActiveDirectoryApplication.Models;
using AzureActiveDirectoryApplication.Models.BloodHound;
using Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element;
using Microsoft.Graph;
using MoreLinq.Extensions;
using DirectoryRole = Microsoft.Graph.DirectoryRole;
using Group = Microsoft.Graph.Group;
using User = Microsoft.Graph.User;

namespace AzureActiveDirectoryApplication.Utils
{
    public static class CosmosDbGraphHelper
    {
        private static readonly Dictionary<string, List<string>> AzureDictionaryRolesToPermissionsMapping;

        static CosmosDbGraphHelper()
        {
            AzureDictionaryRolesToPermissionsMapping = HttpContext.Current.Application
                .GetApplicationState<Dictionary<string, List<string>>>(
                    nameof(AzureDictionaryRolesToPermissionsMapping));
        }

        private static void TryGetPermissions(DirectoryRole _, out List<string> permissions)
        {
            if (_?.DisplayName == null)
                permissions = null;
            else
                AzureDictionaryRolesToPermissionsMapping.TryGetValue(_.DisplayName, out permissions);
        }

        public static void GroupMembership(Group _,
            List<GroupMember> groupMembers)
        {
            var gremlinVertices = new List<GremlinVertex>();
            var gremlinEdges = new List<GremlinEdge>();

            var vertex = new GremlinVertex(_.Id, nameof(Models.BloodHound.Group));
            vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
            vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);
            gremlinVertices.Add(vertex);

            groupMembers.ForEach(member =>
            {
                var gremlinEdge = new GremlinEdge(
                    _.Id + member.Id,
                    "MemberOf",
                    member.Id,
                    _.Id,
                    member.MemberType,
                    nameof(Models.BloodHound.Group),
                    member.Id.GetHashCode(),
                    _.Id.GetHashCode());

                gremlinEdges.Add(gremlinEdge);
            });

            CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }

        public static void DirectoryRoleMembership(DirectoryRole _,
            List<GroupMember> members)
        {
            var gremlinVertices = new List<GremlinVertex>();
            var gremlinEdges = new List<GremlinEdge>();

            var vertex = new GremlinVertex(_.Id, nameof(Models.BloodHound.DirectoryRole));
            vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
            vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);

            gremlinVertices.Add(vertex);

            members.ForEach(member =>
            {
                gremlinEdges.Add(new GremlinEdge(
                    _.Id + member.Id,
                    "MemberOf",
                    member.Id,
                    _.Id,
                    nameof(User),
                    nameof(Models.BloodHound.DirectoryRole),
                    member.Id.GetHashCode(),
                    _.Id.GetHashCode()));
            });

            CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }

        public static void DirectoryRolePermissions(DirectoryRole _,
            List<string> userIds,
            HashSet<string> administrators)
        {
            var gremlinEdges = new List<GremlinEdge>();
            TryGetPermissions(_, out var permissions);

            if (permissions?.Contains("microsoft.aad.directory/users/password/update") == true)
                userIds.Where(userId => !administrators.Contains(userId)).ForEach(
                    userId =>
                        gremlinEdges.Add(
                            new GremlinEdge(
                                _.Id + userId,
                                "ForceChangePassword",
                                _.Id,
                                userId,
                                nameof(Models.BloodHound.DirectoryRole),
                                nameof(User),
                                _.Id.GetHashCode(),
                                userId.GetHashCode()
                            )));

            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }

        public static void DeviceOwners(Device _,
            List<DirectoryObject> ownerList,
            IGraphServiceDirectoryRolesCollectionPage directoryRoles)
        {
            var gremlinVertices = new List<GremlinVertex>();
            var gremlinEdges = new List<GremlinEdge>();

            var deviceOwnerGroups =
                directoryRoles.Where(__ =>
                    MicrosoftGraphApiHelper.DeviceOwnerGroupDisplayNames.Contains(__.DisplayName));

            var vertex = new GremlinVertex(_.Id, nameof(Computer));
            vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
            vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);
            gremlinVertices.Add(vertex);

            ownerList.ForEach(__ =>
            {
                var user = (User) __;
                var gremlinEdge = new GremlinEdge(
                    user.Id + _.Id,
                    "AdminTo",
                    user.Id,
                    _.Id,
                    nameof(User),
                    nameof(Computer),
                    user.Id.GetHashCode(),
                    _.Id.GetHashCode());

                gremlinEdges.Add(gremlinEdge);
            });

            deviceOwnerGroups.ForEach(directoryRole =>
            {
                var gremlinEdge = new GremlinEdge(
                    directoryRole.Id + _.Id,
                    "AdminTo",
                    directoryRole.Id,
                    _.Id,
                    nameof(DirectoryRole),
                    nameof(Computer),
                    directoryRole.Id.GetHashCode(),
                    _.Id.GetHashCode());

                gremlinEdges.Add(gremlinEdge);
            });

            CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }

        public static void InteractiveLogOns(InteractiveLogon _,
            Dictionary<string, string> deviceObjectIdToDeviceId)
        {
            var gremlinEdges = new List<GremlinEdge>();

            deviceObjectIdToDeviceId.TryGetValue(_.DeviceId, out var deviceId);

            if (deviceId == null) return;

            var gremlinEdge = new GremlinEdge(
                deviceId + _.UserId,
                "HasSession",
                deviceId,
                _.UserId,
                nameof(Computer),
                nameof(User),
                deviceId.GetHashCode(),
                _.UserId.GetHashCode());

            gremlinEdges.Add(gremlinEdge);

            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }

        public static void Users(User user)
        {
            var gremlinVertices = new List<GremlinVertex>();

            var userVertex = new GremlinVertex(user.Id, nameof(User));
            userVertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, user.Id.GetHashCode());
            userVertex.AddProperty(nameof(user.UserPrincipalName), user.UserPrincipalName ?? string.Empty);
            userVertex.AddProperty(nameof(user.Mail), user.Mail ?? string.Empty);
            userVertex.AddProperty(nameof(user.DisplayName), user.DisplayName?.ToUpper() ?? string.Empty);
            gremlinVertices.Add(userVertex);

            CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
        }

        public static void Applications(
            string appDisplayName,
            string appId,
            HashSet<string> permissionsSet,
            string principalId)
        {
            var gremlinVertices = new List<GremlinVertex>();
            var gremlinEdges = new List<GremlinEdge>();

            var vertex = new GremlinVertex(appId, nameof(Application));
            vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, appId.GetHashCode());
            vertex.AddProperty(nameof(appDisplayName), appDisplayName?.ToUpper() ?? string.Empty);
            vertex.AddProperty(nameof(permissionsSet), permissionsSet.ToDelimitedString(",") ?? string.Empty);
            gremlinVertices.Add(vertex);

            var outVertexId = principalId ?? "AccessToAllPrincipals";

            var gremlinEdge = new GremlinEdge(
                outVertexId + appId,
                "Granted",
                appId,
                outVertexId,
                nameof(Models.BloodHound.User),
                nameof(Application),
                appId.GetHashCode(),
                outVertexId.GetHashCode());

            gremlinEdge.AddProperty(nameof(permissionsSet), permissionsSet.ToDelimitedString(",") ?? string.Empty);
            gremlinEdges.Add(gremlinEdge);

            var mailPermissions = new List<string>
            {
                "Mail.Read", "Mail.ReadBasic", "Mail.ReadWrite", "Mail.Read.Shared", "Mail.ReadWrite.Shared",
                "Mail.Send", "Mail.Send.Shared", "MailboxSettings.Read", "Mail.Read", "Mail.ReadWrite",
                "Mail.Send", "MailboxSettings.Read", "MailboxSettings.ReadWrite"
            };

            if (permissionsSet.Overlaps(mailPermissions))
            {
                gremlinEdge = new GremlinEdge(
                    appId + "MailBoxes",
                    "CanManipulate",
                    appId,
                    "MailBoxes",
                    nameof(Application),
                    nameof(Application),
                    appId.GetHashCode(),
                    "MailBoxes".GetHashCode());
                gremlinEdges.Add(gremlinEdge);
            }

            CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
            CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
        }
    }
}