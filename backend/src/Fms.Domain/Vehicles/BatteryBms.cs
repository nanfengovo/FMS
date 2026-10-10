namespace Fms.Domain.Vehicles;

public class BatteryBms
{
    public double SocPercent { get; set; } = 95.0; // 0 ~ 100%
    public double VoltageV { get; set; } = 48.0;
    public double CurrentA { get; set; } = 5.0;     // 负值表示充电
    public double TemperatureC { get; set; } = 25.0;
    public int CycleCount { get; set; } = 10;
    public double HealthPercent { get; set; } = 99.0;
    public double LowBatteryThreshold { get; set; } = 20.0;
    public bool IsCharging { get; set; } = false;

    public bool IsLow => SocPercent <= LowBatteryThreshold;

    public void Consume(double distanceTraveledM, double dtSec)
    {
        if (IsCharging) return;
        // 简化电量消耗模型：行走功耗 + 待机底噪
        double deltaSoc = (distanceTraveledM * 0.005) + (dtSec * 0.0005);
        SocPercent = Math.Max(0.0, SocPercent - deltaSoc);
    }

    public void Charge(double dtSec, double chargeRatePercentPerSec = 0.5)
    {
        IsCharging = true;
        SocPercent = Math.Min(100.0, SocPercent + chargeRatePercentPerSec * dtSec);
    }
}
