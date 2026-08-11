namespace LanMountainDesktop.Services.Update;

internal static class ApplyUpdateResults
{
    public static ApplyUpdateResult Failed(string stage, string code, string message)
    {
        return new ApplyUpdateResult
        {
            Success = false,
            Stage = stage,
            Code = code,
            Message = message,
            ErrorMessage = message
        };
    }
}
