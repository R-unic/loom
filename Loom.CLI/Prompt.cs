using System.Text;
using Loom.Core.Diagnostics;

namespace Loom.CLI;

internal readonly record struct PromptOption<T>(T Value, string Label, string? Description = null);

internal static class Prompt
{
    public static T Select<T>(string question, IReadOnlyList<PromptOption<T>> options)
    {
        Console.WriteLine($"{Colors.Bold}{Colors.Cyan}?{Colors.Reset} {Colors.Bold}{question}{Colors.Reset}");
        if (Console.IsInputRedirected)
            return SelectFromRedirectedInput(options);

        var index = 0;
        var firstRender = true;
        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                Render(options, index, firstRender);
                firstRender = false;

                switch (Console.ReadKey(intercept: true).Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K:
                        index = (index - 1 + options.Count) % options.Count;
                        break;

                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J:
                        index = (index + 1) % options.Count;
                        break;

                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        return options[index].Value;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    private static void Render<T>(
        IReadOnlyList<PromptOption<T>> options,
        int selectedIndex,
        bool firstRender)
    {
        if (!firstRender)
            Console.Write($"\e[{options.Count + 1}A");

        for (var i = 0; i < options.Count; i++)
        {
            Console.Write("\r\e[2K");

            var option = options[i];
            var selected = i == selectedIndex;
            var pointer = selected
                ? $"{Colors.Bold}{Colors.Pink}❯{Colors.Reset}"
                : " ";

            var label = selected
                ? $"{Colors.Bold}{Colors.White}{option.Label}{Colors.Reset}"
                : $"{Colors.Gray}{option.Label}{Colors.Reset}";

            Console.Write(pointer);
            Console.Write(' ');
            Console.Write(label);
            if (!string.IsNullOrEmpty(option.Description))
            {
                Console.Write("  ");
                Console.Write($"{Colors.Dim}{option.Description}{Colors.Reset}");
            }

            Console.WriteLine();
        }

        Console.Write("\r\e[2K");
        Console.WriteLine($"{Colors.Dim}(use ↑/↓ then Enter){Colors.Reset}");
    }

    /// <summary>
    ///     Reads an answer nobody else should see. Nothing is echoed at all, not even a placeholder per character:
    ///     a token is pasted rather than typed, and the length of one is not something a shoulder needs to know.
    /// </summary>
    /// <remarks>
    ///     Redirected input is read as a line, so a script can pipe a token in; that is the same path
    ///     <see cref="SelectFromRedirectedInput{T}" /> takes for the same reason, since a terminal that is not a
    ///     terminal has no keys to read.
    /// </remarks>
    public static string? Secret(string question)
    {
        Console.Write($"{Colors.Bold}{Colors.Cyan}?{Colors.Reset} {Colors.Bold}{question}{Colors.Reset} ");
        if (Console.IsInputRedirected)
            return Console.ReadLine()?.Trim();

        var answer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return answer.ToString().Trim();

                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return null;

                case ConsoleKey.Backspace:
                    if (answer.Length > 0)
                        answer.Length--;

                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                        answer.Append(key.KeyChar);

                    break;
            }
        }
    }

    public static bool Confirm(string question, bool defaultValue = true)
    {
        var hint = defaultValue ? "Y/n" : "y/N";
        while (true)
        {
            Console.Write($"{Colors.Bold}{Colors.Cyan}?{Colors.Reset} {Colors.Bold}{question}{Colors.Reset} {Colors.Dim}({hint}){Colors.Reset} ");
            var answer = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer))
                return defaultValue;

            switch (answer.ToLowerInvariant())
            {
                case "y" or "yes":
                    return true;
                case "n" or "no":
                    return false;
                default:
                    Console.WriteLine($"{Colors.Dim}Please answer 'y' or 'n'.{Colors.Reset}");
                    break;
            }
        }
    }

    private static T SelectFromRedirectedInput<T>(IReadOnlyList<PromptOption<T>> options)
    {
        for (var i = 0; i < options.Count; i++)
            Console.WriteLine($"  {Colors.Dim}{i + 1}.{Colors.Reset} {options[i].Label}");

        Console.Write($"{Colors.Dim}Enter a number (default 1):{Colors.Reset} ");
        var answer = Console.ReadLine()?.Trim();
        if (int.TryParse(answer, out var choice) && choice >= 1 && choice <= options.Count)
            return options[choice - 1].Value;

        return options[0].Value;
    }
}