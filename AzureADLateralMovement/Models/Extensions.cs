using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AzureActiveDirectoryApplication.Models.BloodHound;
using EnsureThat;
using Microsoft.Graph;
using Newtonsoft.Json;
using DirectoryRole = AzureActiveDirectoryApplication.Models.BloodHound.DirectoryRole;
using Domain = AzureActiveDirectoryApplication.Models.BloodHound.Domain;
using Group = AzureActiveDirectoryApplication.Models.BloodHound.Group;
using User = AzureActiveDirectoryApplication.Models.BloodHound.User;

namespace AzureActiveDirectoryApplication.Models
{
    public static class Extensions
    {
        public static string ToTitleCase(this string str)
        {
            return str.Substring(0, 1).ToUpper() + str.Substring(1).ToLower();
        }

        private static void CloseC(JsonTextWriter writer, int count, string type)
        {
            if (writer == null) return;

            writer.WriteEndArray();
            writer.WritePropertyName("meta");
            writer.WriteStartObject();
            writer.WritePropertyName("count");
            writer.WriteValue(count);
            writer.WritePropertyName("type");
            writer.WriteValue(type);
            writer.WriteEndObject();
            writer.Close();
        }

        private static JsonTextWriter CreateFileStream(string fileName)
        {
            Ensure.That(Startup.OutputFolderLocation, nameof(Startup.OutputFolderLocation)).IsNotNullOrWhiteSpace();
            Ensure.That(fileName, nameof(fileName)).IsNotNullOrWhiteSpace();

            System.IO.Directory.CreateDirectory(Startup.OutputFolderLocation);
            var streamWriter = new StreamWriter(Startup.OutputFolderLocation + fileName, false, Encoding.UTF8);
            var jsonTextWriter = new JsonTextWriter(streamWriter) {Formatting = Formatting.Indented};

            jsonTextWriter.WriteStartObject();
            jsonTextWriter.WritePropertyName(fileName);
            jsonTextWriter.WriteStartArray();
            return jsonTextWriter;
        }

        internal static Task StartOutputWriter(BlockingCollection<JsonBase> outputQueue)
        {
            return Task.Factory.StartNew(() =>
                {
                    var serializer = new JsonSerializer
                    {
                        NullValueHandling = NullValueHandling.Include
                    };

                    var computerCount = 0;
                    var userCount = 0;
                    var groupCount = 0;
                    var domainCount = 0;
                    var sessionCount = 0;
                    var applicationCount = 0;

                    JsonTextWriter computers = null;
                    JsonTextWriter users = null;
                    JsonTextWriter groups = null;
                    JsonTextWriter domains = null;
                    JsonTextWriter sessions = null;
                    JsonTextWriter applications = null;

                    foreach (var obj in outputQueue.GetConsumingEnumerable())
                        switch (obj)
                        {
                            case Computer computer:
                                computers = computers ?? CreateFileStream(nameof(computers));
                                SerializeAndFlush(computers, computer, ref computerCount);
                                break;
                            case DirectoryRole directoryRole:
                                SerializeGroup(directoryRole);
                                break;
                            case Domain domain:
                                domains = domains ?? CreateFileStream(nameof(domains));
                                SerializeAndFlush(domains, domain, ref domainCount);
                                break;
                            case Group group:
                                SerializeGroup(group);
                                break;
                            case Session session:
                                sessions = sessions ?? CreateFileStream(nameof(sessions));
                                SerializeAndFlush(sessions, session, ref sessionCount);
                                break;
                            case User user:
                                users = users ?? CreateFileStream(nameof(users));
                                SerializeAndFlush(users, user, ref userCount);
                                break;
                            case BloodHound.Application application:
                                applications = applications ?? CreateFileStream(nameof(applications));
                                SerializeAndFlush(applications, application, ref applicationCount);
                                break;
                        }

                    void SerializeAndFlush(JsonTextWriter jsonTextWriter, JsonBase jsonBase, ref int counter)
                    {
                        serializer.Serialize(jsonTextWriter, jsonBase);
                        counter++;
                        if (counter % 100 == 0) jsonTextWriter.Flush();
                    }

                    void SerializeGroup(Group group)
                    {
                        EnsureArg.IsNotNull(group, nameof(group));

                        groups = groups ?? CreateFileStream(nameof(groups));
                        SerializeAndFlush(groups, group, ref groupCount);
                    }

                    CloseC(computers, computerCount, nameof(computers));
                    CloseC(domains, domainCount, nameof(domains));
                    CloseC(groups, groupCount, nameof(groups));
                    CloseC(users, userCount, nameof(users));
                    CloseC(sessions, sessionCount, nameof(sessions));
                    CloseC(applications, applicationCount, nameof(applications));
                },
                TaskCreationOptions.LongRunning);
        }

        public static List<GroupMember> DirectoryRoleMembersResultsToList(
            IDirectoryRoleMembersCollectionWithReferencesPage roleMembers)
        {
            return roleMembers.Cast<Microsoft.Graph.User>().Select(__ => new GroupMember
            {
                Id = __.Id,
                MemberName = __.DisplayName,
                MemberType = nameof(Microsoft.Graph.User)
            }).ToList();
        }
    }
}