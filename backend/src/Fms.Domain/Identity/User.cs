using System;
using System.Collections.Generic;

namespace Fms.Domain.Identity;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    
    public ICollection<Role> Roles { get; set; } = new List<Role>();
}

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Resource { get; set; } = string.Empty; // e.g. "Map", "Vehicle", "Simulation"
    public string Action { get; set; } = string.Empty;   // e.g. "Read", "Write", "Execute"
}
