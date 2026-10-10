using System;

namespace Fms.Domain.Logging;

public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Operator { get; set; } = "SYSTEM";
    public string Action { get; set; } = string.Empty; // e.g., "E-STOP", "MODIFY_MAP", "CANCEL_ORDER", "UPDATE_CONFIG"
    public string Resource { get; set; } = string.Empty; // e.g., "Vehicle-AGV-101", "Map-NXP-Cleanroom", "Config-custom.set"
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "127.0.0.1";
    public bool Success { get; set; } = true;
}
