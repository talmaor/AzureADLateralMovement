using System;
using AzureActiveDirectoryApplication.Models;

namespace AzureAdLateralMovement.Models.BloodHound
{
    public class GroupMember : IEquatable<GroupMember>
    {
        private string _userName;
        public string Name
        {
            get => _userName;
            set => _userName = value.ToUpper();
        }

        private string _type;
        public string Type
        {
            get => _type;
            set => _type = value.ToTitleCase();
        }

        public string Id { get; set; }

        public bool Equals(GroupMember other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Name, other.Name) && string.Equals(Type, other.Type);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((GroupMember) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^
                       (Type != null ? Type.GetHashCode() : 0);
            }
        }
    }
}