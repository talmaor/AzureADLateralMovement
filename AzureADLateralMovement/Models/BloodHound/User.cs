using System.Collections.Generic;

namespace AzureActiveDirectoryApplication.Models.BloodHound
{
    internal class User : JsonBase
    {
        private string _userName;
        public Dictionary<string, object> Properties = new Dictionary<string, object>();

        public string Name
        {
            get => _userName;
            set => _userName = value.ToUpper();
        }

        public string PrimaryGroup { get; set; }

        public ACL[] Aces { get; set; }

        public string[] AllowedToDelegate { get; set; }
    }
}