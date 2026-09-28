using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Launcher.Oobe;

internal sealed class OobeSessionCommitService
{
    private readonly DataLocationResolver _dataLocationResolver;
    private readonly OobeStateService _oobeStateService;
    private readonly CommandContext _context;
    private readonly Func<bool, bool>? _setWindowsStartup;

    public OobeSessionCommitService(
        DataLocationResolver dataLocationResolver,
        OobeStateService oobeStateService,
        CommandContext context,
        Func<bool, bool>? setWindowsStartup = null)
    {
        _dataLocationResolver = dataLocationResolver;
        _oobeStateService = oobeStateService;
        _context = context;
        _setWindowsStartup = setWindowsStartup;
    }

    public OobeCompletionResult Commit(OobeSessionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!_dataLocationResolver.ApplyLocationChoice(
                draft.DataLocationMode,
                customPath: null,
                draft.MigrateExistingData))
        {
            return Failure("data_location_save_failed", "Failed to save the selected data location.");
        }

        var dataRoot = _dataLocationResolver.ResolveDataRoot();

        try
        {
            var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(dataRoot);
            HostAppSettingsOobeMerger.MergeStartupPresentation(settingsPath, draft.StartupChoices);
        }
        catch (Exception ex)
        {
            return Failure("startup_settings_save_failed", ex.Message);
        }

        var setWindowsStartup = _setWindowsStartup ?? new LauncherWindowsStartupService().SetEnabled;
        if (OperatingSystem.IsWindows() &&
            !setWindowsStartup(draft.StartupChoices.AutoStartWithWindows))
        {
            return Failure("windows_startup_save_failed", "Failed to save Windows startup preference.");
        }

        try
        {
            var launcherDataPath = _dataLocationResolver.ResolveLauncherDataPath();
            Directory.CreateDirectory(launcherDataPath);

            // 两个遥测开关写进宿主自己读的那份 settings.json。此前它们只落在本目录的
            // privacy-config.json，而全仓没有读者——用户在向导里选了"不开"，宿主照旧上报。
            HostAppSettingsOobeMerger.MergePrivacyChoices(
                HostAppSettingsOobeMerger.GetSettingsFilePath(dataRoot),
                draft.PrivacyConfig.CrashTelemetryEnabled,
                draft.PrivacyConfig.UsageTelemetryEnabled);

            // 老版本把这两个开关写在 launcher 数据目录下的 privacy-config.json 里，而那份文件从来没有读者。
            // 答案现在落在 settings.json，这里把遗留文件尽力清掉，免得日后有人以为它还是真源。
            FileOperationRetryHelper.TryDeleteFile(
                Path.Combine(launcherDataPath, "privacy-config.json"),
                "OOBE");

            var agreementService = new PrivacyAgreementService(launcherDataPath);
            if (!agreementService.SaveAgreement(
                    draft.PrivacyAgreementAccepted,
                    draft.PrivacyUserId,
                    draft.PrivacyDeviceId))
            {
                return Failure("privacy_agreement_save_failed", "Failed to save privacy agreement state.");
            }
        }
        catch (Exception ex)
        {
            return Failure("privacy_settings_save_failed", ex.Message);
        }

        var completion = _oobeStateService.MarkCompleted(_context, dataRoot);
        return completion.Success
            ? completion
            : Failure(completion.ResultCode, completion.ErrorMessage);
    }

    private static OobeCompletionResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            ResultCode = code,
            ErrorMessage = message
        };
}
