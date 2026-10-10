namespace Fms.Domain.Rcs;

public class RcsProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RcsBaseUrl { get; set; } = "http://127.0.0.1:9000";
    public string PushServerUrl { get; set; } = "http://127.0.0.1:9000/api/v1/xinsong/report_robot_status";
    public bool PushEnabled { get; set; } = true;

    /// <summary>
    /// 当任务类型带有下划线时（如 PA_14_get），回调路由是否拼接 /{type} 前缀（台湾晶技等项目要求）
    /// </summary>
    public bool UseTypePrefixOnCallback { get; set; } = false;

    /// <summary>
    /// 动作许可响应信封格式：DataNested (规范要求的 { data: { option_code, task_serial } }) 或 Root
    /// </summary>
    public string OptionCodeEnvelope { get; set; } = "DataNested";

    /// <summary>
    /// 放货前是否强制执行机械臂条码/RFID物料核验回调
    /// </summary>
    public bool EnableRfidCallback { get; set; } = true;

    /// <summary>
    /// 离线仿真沙箱模式：当上游 RCS 未启动或无法联通时，自动放行许可与RFID校验，保证全套仿真闭环流转
    /// </summary>
    public bool AutoPermitInSimulation { get; set; } = true;
    public bool AutoRfidVerifiedInSimulation { get; set; } = true;

    // 动力学与机构仿真参数
    public double CruiseSpeed { get; set; } = 1.2;
    public double TurningSpeed { get; set; } = 0.4;
    public double CreepSpeed { get; set; } = 0.15;
    public double LiftDurationSec { get; set; } = 3.0;
    public double PassBoxDwellSec { get; set; } = 4.0;
    public double ElevatorDwellSec { get; set; } = 10.0;
    public bool IsActive { get; set; } = false;

    public static List<RcsProfile> GetBuiltInProfiles()
    {
        return new List<RcsProfile>
        {
            new RcsProfile
            {
                Id = "standard",
                Name = "新松标准 (Siasun TM-V1)",
                Description = "新松标准调度外部 HTTP 接口规范，根路径统一回调",
                RcsBaseUrl = "http://127.0.0.1:9000",
                PushServerUrl = "http://127.0.0.1:9000/api/v1/xinsong/report_robot_status",
                PushEnabled = true,
                UseTypePrefixOnCallback = false,
                OptionCodeEnvelope = "Root",
                EnableRfidCallback = false,
                AutoPermitInSimulation = true,
                AutoRfidVerifiedInSimulation = true,
                CruiseSpeed = 1.2,
                TurningSpeed = 0.4,
                CreepSpeed = 0.15,
                LiftDurationSec = 3.0,
                PassBoxDwellSec = 4.0,
                ElevatorDwellSec = 10.0,
                IsActive = false
            },
            new RcsProfile
            {
                Id = "txc",
                Name = "台湾晶技 (TXC-Crystal-Fab)",
                Description = "半导体石英晶振洁净室，任务下划线动态路由 /{type}，动作前强制核验 RFID，数据嵌套",
                RcsBaseUrl = "http://127.0.0.1:5000",
                PushServerUrl = "http://127.0.0.1:5000/api/v1/xinsong/report_robot_status",
                PushEnabled = true,
                UseTypePrefixOnCallback = true,
                OptionCodeEnvelope = "DataNested",
                EnableRfidCallback = true,
                AutoPermitInSimulation = true,
                AutoRfidVerifiedInSimulation = true,
                CruiseSpeed = 1.0,
                TurningSpeed = 0.35,
                CreepSpeed = 0.1,
                LiftDurationSec = 3.5,
                PassBoxDwellSec = 4.0,
                ElevatorDwellSec = 10.0,
                IsActive = true // 默认激活晶圆洁净室仿真
            },
            new RcsProfile
            {
                Id = "nxp-tw",
                Name = "高雄恩智浦 (NXP-TW e-Rack)",
                Description = "半导体引线键合(WB)与E-Rack，对接STKC立库控制器与晖哲PassBox双门传递窗充氮",
                RcsBaseUrl = "http://127.0.0.1:8080",
                PushServerUrl = "http://127.0.0.1:8080/api/v1/xinsong/report_robot_status",
                PushEnabled = true,
                UseTypePrefixOnCallback = false,
                OptionCodeEnvelope = "DataNested",
                EnableRfidCallback = true,
                AutoPermitInSimulation = true,
                AutoRfidVerifiedInSimulation = true,
                CruiseSpeed = 1.2,
                TurningSpeed = 0.4,
                CreepSpeed = 0.12,
                LiftDurationSec = 3.2,
                PassBoxDwellSec = 5.0,
                ElevatorDwellSec = 10.0,
                IsActive = false
            },
            new RcsProfile
            {
                Id = "sandisk-mt",
                Name = "上海晟碟 (SanDisk MT / 跨楼层)",
                Description = "闪存封装大厂，跨楼层乘梯协同调度，组任务首末门控，CoverTray假目标修正与车载TaskCode",
                RcsBaseUrl = "http://127.0.0.1:9001",
                PushServerUrl = "http://127.0.0.1:9001/api/v1/xinsong/report_robot_status",
                PushEnabled = true,
                UseTypePrefixOnCallback = false,
                OptionCodeEnvelope = "DataNested",
                EnableRfidCallback = true,
                AutoPermitInSimulation = true,
                AutoRfidVerifiedInSimulation = true,
                CruiseSpeed = 1.0,
                TurningSpeed = 0.3,
                CreepSpeed = 0.1,
                LiftDurationSec = 4.0,
                PassBoxDwellSec = 4.0,
                ElevatorDwellSec = 14.0,
                IsActive = false
            },
            new RcsProfile
            {
                Id = "nxp-tj",
                Name = "天津恩智浦 (NXP-TJ Molding)",
                Description = "芯片模塑封装生产线，Molding模塑机对接台与Magazine料盒周转，支持Mica WMS握手",
                RcsBaseUrl = "http://127.0.0.1:8081",
                PushServerUrl = "http://127.0.0.1:8081/api/v1/xinsong/report_robot_status",
                PushEnabled = true,
                UseTypePrefixOnCallback = false,
                OptionCodeEnvelope = "DataNested",
                EnableRfidCallback = false,
                AutoPermitInSimulation = true,
                AutoRfidVerifiedInSimulation = true,
                CruiseSpeed = 1.1,
                TurningSpeed = 0.38,
                CreepSpeed = 0.15,
                LiftDurationSec = 3.8,
                PassBoxDwellSec = 4.0,
                ElevatorDwellSec = 10.0,
                IsActive = false
            }
        };
    }
}
