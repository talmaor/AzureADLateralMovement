using System.Collections.Generic;

namespace AzureActiveDirectoryApplication.Models.BloodHound
{
    public class Group : JsonBase
    {
        private string _userName;
        public string Name
        {
            get => _userName;
            set => _userName = value.ToUpper();
        }

        public Dictionary<string, object> Properties = new Dictionary<string, object>();

        public ACL[] Aces { get; set; }

        public GroupMember[] Members { get; set; }
    }

    public class DirectoryRole : Group
    {
    }
}
