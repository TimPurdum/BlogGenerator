using System;
using System.Text;

namespace TimPurdum.Dev.BlogGenerator.Shared;

public static class StringExtensions
{
    public static string KebabCaseToPascalCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        ReadOnlySpan<char> input = str;
        var result = new StringBuilder(str.Length);

        bool capitalize = true;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '-')
            {
                capitalize = true;
            }
            else
            {
                if (capitalize)
                {
                    result.Append(char.ToUpperInvariant(c));
                    capitalize = false;
                }
                else
                {
                    result.Append(c);
                }
            }
        }

        return result.ToString();
    }
    
    public static string PascalToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        ReadOnlySpan<char> input = str;
        var result = new StringBuilder(str.Length);

        bool capitalize = true;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c) && i > 0)
            {
                result.Append(' ');
            }
            if (capitalize)
            {
                result.Append(char.ToUpperInvariant(c));
                capitalize = false;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
    
    public static string KebabCaseToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        ReadOnlySpan<char> input = str;
        var result = new StringBuilder(str.Length);

        bool capitalize = true;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '-')
            {
                result.Append(' ');
                capitalize = true;
            }
            else
            {
                if (capitalize)
                {
                    result.Append(char.ToUpperInvariant(c));
                    capitalize = false;
                }
                else
                {
                    result.Append(char.ToLowerInvariant(c));
                }
            }
        }

        return result.ToString();
    }
    
    public static string ToUpperFirstChar(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        if (str.Length == 1)
        {
            return str.ToUpperInvariant();
        }

        return char.ToUpperInvariant(str[0]) + str.Substring(1);
    }
    
    public static string PascalToKebabCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        ReadOnlySpan<char> input = str;
        var result = new StringBuilder(str.Length);

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c) && i > 0)
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }
}