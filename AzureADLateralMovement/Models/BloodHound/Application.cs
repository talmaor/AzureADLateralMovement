using System.Collections.Generic;

namespace AzureActiveDirectoryApplication.Models.BloodHound
{
    internal class Application : JsonBase
    {
        private string _name;
        public Dictionary<string, object> Properties = new Dictionary<string, object>();

        public string Name
        {
            get => _name;
            set => _name = value.ToUpper();
        }

        public string[] Permissions { get; set; }
        public string PrincipalId { get; set; }
    }
}