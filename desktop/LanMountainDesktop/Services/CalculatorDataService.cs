using System;
using System.Globalization;

namespace LanMountainDesktop.Services;

public sealed class CalculatorDataService : ICalculatorDataService
{
    private const int MaxInputLength = 18;

    public string ApplyInputToken(string currentInput, string token)
    {
        var normalized = NormalizeInput(currentInput);
        if (string.IsNullOrWhiteSpace(token))
        {
            return normalized;
        }

        if (string.Equals(token, CalculatorInputTokens.Clear, StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        if (string.Equals(token, CalculatorInputTokens.Backspace, StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.Length <= 1)
            {
                return "0";
            }

            var trimmed = normalized[..^1];
            if (trimmed is "-" or "" or "-0")
            {
                return "0";
            }

            return trimmed;
        }

        if (string.Equals(token, CalculatorInputTokens.DecimalPoint, StringComparison.Ordinal))
        {
            if (normalized.Contains('.', StringComparison.Ordinal))
            {
                return normalized;
            }

            if (normalized.Length >= MaxInputLength)
            {
                return normalized;
            }

            return $"{normalized}.";
        }

        if (token is "00")
        {
            if (normalized == "0")
            {
                return "0";
            }

            if (normalized.Length + 2 > MaxInputLength)
            {
                return normalized;
            }

            return normalized + "00";
        }

        if (token.Length == 1 && char.IsDigit(token[0]))
        {
            if (normalized == "0")
            {
                return token;
            }

            if (normalized.Length >= MaxInputLength)
            {
                return normalized;
            }

            return normalized + token;
        }

        return normalized;
    }

    public decimal ParseAmountOrZero(string? inputText)
    {
        var normalized = NormalizeInput(inputText);
        if (decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var amount))
        {
            return amount;
        }

        return 0m;
    }

    public string FormatAmount(decimal amount, int maxFractionDigits = 4)
    {
        var safeDigits = Math.Clamp(maxFractionDigits, 0, 8);
        var pattern = safeDigits == 0 ? "0" : $"0.{new string('#', safeDigits)}";
        return amount.ToString(pattern, CultureInfo.InvariantCulture);
    }

    private static string NormalizeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "0";
        }

        var trimmed = input.Trim();
        return trimmed switch
        {
            "-" or "-0" => "0",
            _ => trimmed
        };
    }
}
