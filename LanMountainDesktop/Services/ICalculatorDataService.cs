namespace LanMountainDesktop.Services;

public interface ICalculatorDataService
{
    string ApplyInputToken(string currentInput, string token);

    decimal ParseAmountOrZero(string? inputText);

    string FormatAmount(decimal amount, int maxFractionDigits = 4);
}

public static class CalculatorInputTokens
{
    public const string Clear = "AC";
    public const string Backspace = "BACK";
    public const string DecimalPoint = ".";
}
