using System;

namespace AzureActiveDirectoryApplication.Models.BloodHound
{
    public class GroupMember : IEquatable<GroupMember>
    {
        private string _userName;
        public string MemberName
        {
            get => _userName;
            set => _userName = value.ToUpper();
        }

        private string _type;
        public string MemberType
        {
            get => _type;
            set => _type = value.ToTitleCase();
        }

        public string Id { get; set; }

        public bool Equals(GroupMember other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(MemberName, other.MemberName) && string.Equals(MemberType, other.MemberType);
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
                return ((MemberName != null ? MemberName.GetHashCode() : 0) * 397) ^
                       (MemberType != null ? MemberType.GetHashCode() : 0);
            }
        }
    }
}