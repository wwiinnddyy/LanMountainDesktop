using LanMountainDesktop.AirAppSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirApp.ComponentTemplate;

/// <summary>
/// AirApp entry point.
/// </summary>
[AirAppEntrance]
public sealed class MyAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Register the desktop component
        services.AddAirAppComponent<MyWidget>(
            "my-widget",
            "My Widget",
            options =>
            {
                options.Description = "A sample desktop component";
                options.DefaultWidth = 2;
                options.DefaultHeight = 2;
                options.ResizeMode = AirAppComponentResizeMode.Both;
                options.Category = "Custom";
                options.IconKey = "AppGeneric";
            });
    }

    public override Task OnStartedAsync(IAirAppRuntimeContext context)
    {
        context.Logger.Info("My AirApp started successfully!");
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync()
    {
        return Task.CompletedTask;
    }
}
