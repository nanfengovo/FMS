using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Vehicles;

public class VehicleKinematics
{
    public double Length { get; set; } = 1.2;
    public double Width { get; set; } = 0.8;
    public Fms.Domain.Shared.Enums.DriveType DriveType { get; set; } = Fms.Domain.Shared.Enums.DriveType.Differential;
    public double MaxSpeed { get; set; } = 1.5;
    public double MaxAngularSpeed { get; set; } = 1.2; // rad/s
    public double Acceleration { get; set; } = 0.8;    // m/s^2
    public double Deceleration { get; set; } = 1.0;    // m/s^2
    public double MinTurnRadius { get; set; } = 0.0;   // 0 = 原地自转
    public bool CanRotateInPlace { get; set; } = true;
    public double StoppingDistance(double currentSpeed) => (currentSpeed * currentSpeed) / (2.0 * Math.Max(0.1, Deceleration));
}
