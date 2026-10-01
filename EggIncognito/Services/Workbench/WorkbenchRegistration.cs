using EggIdentity.UI;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Api;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Events;
using EggIncognito.Services.Notifications;
using EggIncognito.Services.Protos;

namespace EggIncognito.Services.Workbench;

public static class WorkbenchRegistration {
    public static IServiceCollection AddWorkbenchStates(this IServiceCollection services) {
        services.AddEggIdentityToasts();
        services.AddScoped<ProtoWorkbenchState>();
        services.AddScoped<DeviceWorkbenchState>();
        services.AddScoped<NotificationsWorkbenchState>();
        services.AddScoped<ApiWorkbenchState>();
        services.AddScoped<EventsWorkbenchState>();
        services.AddScoped<ContractsWorkbenchState>();
        services.AddScoped<AdminWorkbenchState>();
        services.AddSingleton<AdminNotifier>();
        return services;
    }
}
