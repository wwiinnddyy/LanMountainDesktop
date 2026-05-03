using System;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Update;
using SettingsUpdateSettingsState = LanMountainDesktop.Services.Settings.UpdateSettingsState;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateStateStore
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly object _sync = new();

    private const int AutoDowngradeThreshold = 3;
    private int _consecutiveFailCount;

    public UpdateStateStore(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        CurrentPhase = UpdatePhase.Idle;
    }

    public UpdatePhase CurrentPhase { get; private set; }

    public event Action<UpdatePhase>? PhaseChanged;
    public event Action<UpdateProgressReport>? ProgressChanged;

    public void TransitionTo(UpdatePhase newPhase)
    {
        lock (_sync)
        {
            if (CurrentPhase == newPhase)
            {
                return;
            }

            CurrentPhase = newPhase;
        }

        PhaseChanged?.Invoke(newPhase);
        ProgressChanged?.Invoke(new UpdateProgressReport(
            newPhase,
            $"Phase changed to {newPhase}",
            0,
            null,
            null));
    }

    public SettingsUpdateSettingsState GetSettings()
    {
        return _settingsFacade.Update.Get();
    }

    public void SaveSettings(SettingsUpdateSettingsState state)
    {
        _settingsFacade.Update.Save(state);
    }

    public UpdateManifest? PendingManifest { get; set; }

    public void RecordFailure(string errorMessage)
    {
        Interlocked.Increment(ref _consecutiveFailCount);
        AppLogger.Warn("UpdateStateStore", $"Update failure recorded (consecutive: {_consecutiveFailCount}): {errorMessage}");
    }

    public void RecordSuccess(string appliedVersion)
    {
        Interlocked.Exchange(ref _consecutiveFailCount, 0);

        var state = GetSettings();
        SaveSettings(state with
        {
            PendingUpdateVersion = appliedVersion,
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    public bool ShouldAutoDowngrade => Volatile.Read(ref _consecutiveFailCount) >= AutoDowngradeThreshold;
}
