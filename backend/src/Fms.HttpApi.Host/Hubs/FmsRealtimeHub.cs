using Fms.Application.Identity;
using Fms.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fms.HttpApi.Host.Hubs;

public interface IFmsClient
{
    Task ReceiveTelemetryBatch(object batch);
    Task ReceiveConflictAlert(string message);
    Task ReceiveOrderStateChanged(object orderInfo);
    Task ReceiveOrderSnapshot(object orders);
}

[Authorize]
public class FmsRealtimeHub : Hub<IFmsClient>
{
    private readonly IPermissionService _permissions;

    public FmsRealtimeHub(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    public override async Task OnConnectedAsync()
    {
        if (!await _permissions.HasAsync(Context.User, FmsPermissions.Dashboard))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
