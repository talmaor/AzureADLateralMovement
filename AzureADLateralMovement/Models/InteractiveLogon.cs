using System;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;

namespace AzureActiveDirectoryApplication.Models
{
    public class InteractiveLogon
    {
        public InteractiveLogon(
            DeviceDetail deviceDetail,
            SignInLocation location,
            string userId,
            DateTime creationDateTime,
            string userDisplayName)
        {
            DeviceDetail = deviceDetail;
            DeviceId = deviceDetail.DeviceId;
            DeviceDisplayName = deviceDetail.DisplayName;

            Location = location;
            UserId = userId;
            UserDisplayName = userDisplayName.ToUpper();
            CreationDateTime = creationDateTime;
        }

        public DeviceDetail DeviceDetail { get; }
        public SignInLocation Location { get; }
        public string UserId { get; }
        public string UserDisplayName { get; }
        public string DeviceId { get; }
        public string DeviceDisplayName { get; }
        public DateTime CreationDateTime { get; }
    }
}