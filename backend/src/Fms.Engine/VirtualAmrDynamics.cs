using Fms.Domain.Shared.Geometry;
using Fms.Domain.Vehicles;

namespace Fms.Engine;

public class VirtualAmrDynamics
{
    public void StepSimulation(VehicleAggregate vehicle, double dtSec, DateTime currentTimeUtc)
    {
        if (!vehicle.IsOnline || vehicle.IsEstop || vehicle.IsPaused)
        {
            vehicle.CurrentSpeed = 0.0;
            return;
        }

        // 处于等待中 (以仿真时钟为唯一基准)
        if (vehicle.WaitUntilTime.HasValue)
        {
            if (currentTimeUtc < vehicle.WaitUntilTime.Value)
            {
                vehicle.CurrentSpeed = 0.0;
                // 工艺动作物理进度积分 (举升/下降 机构平滑动态)
                if (vehicle.ActionPhase == "LiftingUp")
                {
                    vehicle.LiftProgress = Math.Min(1.0, vehicle.LiftProgress + dtSec / 2.0);
                }
                else if (vehicle.ActionPhase == "PuttingDown")
                {
                    vehicle.LiftProgress = Math.Max(0.0, vehicle.LiftProgress - dtSec / 2.0);
                }
                return;
            }
            vehicle.WaitUntilTime = null;
            if (vehicle.ActionPhase == "LiftingUp") vehicle.LiftProgress = 1.0;
            else if (vehicle.ActionPhase == "PuttingDown") vehicle.LiftProgress = 0.0;
        }

        // 充电状态真实处理 (BMS 快充闭环)
        if (vehicle.Status == Domain.Shared.Enums.VehicleStatus.Charging)
        {
            vehicle.Bms.Charge(dtSec);
            return;
        }

        // 沿规划采样点轨迹行进
        if (vehicle.TrajectoryPoints.Count == 0 || vehicle.CurrentTrajectoryIndex >= vehicle.TrajectoryPoints.Count)
        {
            vehicle.CurrentSpeed = 0.0;
            if (vehicle.Status == Domain.Shared.Enums.VehicleStatus.Running)
            {
                vehicle.Status = Domain.Shared.Enums.VehicleStatus.Idle;
            }
            return;
        }

        var currentPos = vehicle.Pose.Position;
        var targetPoint = vehicle.TrajectoryPoints[vehicle.CurrentTrajectoryIndex];
        double distToTarget = currentPos.DistanceTo(targetPoint);

        // 如果距离当前目标点极近，切入下一个目标点
        if (distToTarget < 0.15)
        {
            vehicle.CurrentTrajectoryIndex++;
            if (vehicle.CurrentTrajectoryIndex >= vehicle.TrajectoryPoints.Count)
            {
                // 到达终点
                vehicle.Pose = new Pose2D(targetPoint.X, targetPoint.Y, vehicle.Pose.ThetaRad);
                vehicle.CurrentSpeed = 0.0;
                vehicle.Status = Domain.Shared.Enums.VehicleStatus.Idle;
                vehicle.IsMovingBackward = false;
                return;
            }
            targetPoint = vehicle.TrajectoryPoints[vehicle.CurrentTrajectoryIndex];
            distToTarget = currentPos.DistanceTo(targetPoint);
        }

        // 速度控制与加速度积分 (支持跟驰模型限制)
        double targetSpeed = vehicle.Kinematics.MaxSpeed;
        if (vehicle.CarFollowingTargetSpeed.HasValue)
        {
            targetSpeed = Math.Min(targetSpeed, vehicle.CarFollowingTargetSpeed.Value);
        }

        int remainingPoints = vehicle.TrajectoryPoints.Count - vehicle.CurrentTrajectoryIndex;
        if (remainingPoints < 5) // 终点前降速平滑停车（稀疏路线按剩余距离判断，避免 2~3 个点的长直线全程爬行）
        {
            double distToEnd = distToTarget;
            for (int i = vehicle.CurrentTrajectoryIndex + 1; i < vehicle.TrajectoryPoints.Count; i++)
            {
                distToEnd += vehicle.TrajectoryPoints[i - 1].DistanceTo(vehicle.TrajectoryPoints[i]);
            }
            if (distToEnd < 1.5)
            {
                targetSpeed = Math.Max(0.2, targetSpeed * (remainingPoints / 5.0));
            }
        }

        if (vehicle.CurrentSpeed < targetSpeed)
        {
            vehicle.CurrentSpeed = Math.Min(targetSpeed, vehicle.CurrentSpeed + vehicle.Kinematics.Acceleration * dtSec);
        }
        else if (vehicle.CurrentSpeed > targetSpeed)
        {
            vehicle.CurrentSpeed = Math.Max(targetSpeed, vehicle.CurrentSpeed - vehicle.Kinematics.Deceleration * dtSec);
        }

        // 移动步长
        double stepDist = vehicle.CurrentSpeed * dtSec;
        double moveDist = Math.Min(stepDist, distToTarget);
        double t = distToTarget > 1e-6 ? moveDist / distToTarget : 1.0;

        var nextPos = Point2D.Lerp(currentPos, targetPoint, t);

        // 计算车身朝向角（考虑前进边与后退倒车边）
        double dx = targetPoint.X - currentPos.X;
        double dy = targetPoint.Y - currentPos.Y;
        double tangentTheta = Math.Atan2(dy, dx);

        bool isBackward = false;
        if (vehicle.TrajectoryBackwardFlags != null &&
            vehicle.CurrentTrajectoryIndex < vehicle.TrajectoryBackwardFlags.Count)
        {
            isBackward = vehicle.TrajectoryBackwardFlags[vehicle.CurrentTrajectoryIndex];
        }
        vehicle.IsMovingBackward = isBackward;

        // 后退边倒车运动学：车尾朝向切线向量，车头朝向与切线相反 (偏移 PI 弧度)，绝不可车头朝前倒退
        double targetTheta = isBackward
            ? Pose2D.NormalizeAngle(tangentTheta + Math.PI)
            : tangentTheta;

        // 平滑旋转
        double currentTheta = vehicle.Pose.ThetaRad;
        double deltaTheta = Pose2D.NormalizeAngle(targetTheta - currentTheta);
        double maxRot = vehicle.Kinematics.MaxAngularSpeed * dtSec;
        double newTheta = currentTheta + Math.Clamp(deltaTheta, -maxRot, maxRot);

        vehicle.Pose = new Pose2D(nextPos.X, nextPos.Y, newTheta);

        // 更新电量与里程
        vehicle.Bms.Consume(moveDist, dtSec);
    }
}
