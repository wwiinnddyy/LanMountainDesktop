using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

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

            var privacyConfigPath = Path.Combine(launcherDataPath, "privacy-config.json");
            var privacyJson = JsonSerializer.Serialize(draft.PrivacyConfig, AppJsonContext.Default.PrivacyConfig);
            File.WriteAllText(privacyConfigPath, privacyJson);

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
