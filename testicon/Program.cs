using System; class Program { static void Main() { foreach (var name in Enum.GetNames(typeof(FluentIcons.Common.Symbol))) Console.WriteLine(name); } }
