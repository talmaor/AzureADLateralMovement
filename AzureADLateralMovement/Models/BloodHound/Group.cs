using System.Collections.Generic;
using AzureActiveDirectoryApplication.Models.BloodHound;

namespace AzureAdLateralMovement.Models.BloodHound
{
    public class Group : JsonBase
    {
        private string _userName;

        public Dictionary<string, object> Properties = new Dictionary<string, object>();

        public string Name
        {
            get => _userName;
            set => _userName = value.ToUpper();
        }

        public ACL[] Aces { get; set; }

        public GroupMember[] Members { get; set; }
    }

    public class DirectoryRole : Group
    {
    }
}