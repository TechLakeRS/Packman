namespace Packman.Models;

/// <summary>An Entra (Azure AD) security group returned by a directory search.</summary>
public class EntraGroup
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

/// <summary>A member of an Entra group (device, user or nested group).</summary>
public class GroupMember
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "";   // Device | User | Group
}
