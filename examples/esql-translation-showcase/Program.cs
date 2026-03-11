// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace EsqlTranslationShowcase;

public static class Program
{
	// Color scheme
	private const string CSharpBorder = "dodgerblue1";
	private const string EsqlBorder = "green3";
	private const string Accent = "yellow";
	private const string Muted = "grey";

	// Syntax highlighting colors
	private const string KeywordColor = "mediumpurple2";
	private const string StringColor = "lightsalmon3_1";
	private const string NumberColor = "aqua";
	private const string TypeColor = "green3";
	private const string CommentColor = "grey";
	private const string EsqlCommandColor = "bold dodgerblue1";
	private const string EsqlParamColor = "mediumpurple2";

	public static void Main()
	{
		Action[] slides =
		[
			RenderIntro,
			RenderScenario1,
			RenderScenario2,
			RenderScenario3,
			RenderScenario4,
			RenderScenario5,
			RenderOutro
		];

		NavigateSlides(slides);
	}

	private static void NavigateSlides(Action[] slides)
	{
		var index = 0;

		while (index < slides.Length)
		{
			Console.Clear();
			slides[index]();

			AnsiConsole.WriteLine();
			AnsiConsole.Markup(
				$"[{Muted}]  slide {index + 1}/{slides.Length}   <- prev  |  -> next  |  q quit[/]"
			);

			var key = Console.ReadKey(true).Key;
			switch (key)
			{
				case ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter:
					index++;
					break;
				case ConsoleKey.LeftArrow or ConsoleKey.Backspace:
					index = Math.Max(0, index - 1);
					break;
				case ConsoleKey.Q or ConsoleKey.Escape:
					Console.Clear();
					return;
			}
		}
	}

	private static void RenderIntro()
	{
		WriteHeader();

		var intro = new Panel(
			new Markup(string.Join('\n',
				$"[{Accent}]Introduction to LINQ:[/]",
				"",
				"Language-Integrated Query (LINQ) is the name for a set of technologies based on the integration of query capabilities directly into the C# language.",
				"",
				$"  * LINQ is C#'s idiomatic way to express data queries.",
				$"  * Query operators like [{CSharpBorder}]Where[/]/[{CSharpBorder}]Select[/]/[{CSharpBorder}]GroupBy[/] are standard language patterns."
			))
		)
		{
			Border = BoxBorder.Rounded,
			Expand = true,
			Padding = new Padding(2, 1)
		};

		AnsiConsole.Write(intro);
		AnsiConsole.WriteLine();

		AnsiConsole.Write(new Panel(
			new Markup($"LINQ queries come in two equivalent syntaxes in C#: [{CSharpBorder}]method syntax[/] and [{CSharpBorder}]query syntax[/]. Both compile to the same expression tree.")
		)
		{
			Border = BoxBorder.Rounded,
			Expand = true,
			Padding = new Padding(2, 0)
		});

		// Demonstrate method syntax vs query syntax equivalence
		WriteCSharpCode(
			"Method syntax",
			"""
			var result = numbers
				.Where(n => n > 5)
				.OrderBy(n => n)
				.Select(n => n * 2);
			"""
		);

		WriteCSharpCode(
			"Query syntax (compiles to the same expression tree)",
			"""
			var result =
				from n in numbers
				where n > 5
				orderby n
				select n * 2;
			"""
		);

		AnsiConsole.Write(new Panel(
			new Markup(string.Join('\n',
				$"These query operators work on all types implementing [{CSharpBorder}]IEnumerable<T>[/] ([{CSharpBorder}]T[[]][/], [{CSharpBorder}]List<T>[/], etc.) but also on all types implementing [{CSharpBorder}]IQueryable<T>[/].",
				"",
				$"  * [{CSharpBorder}]IEnumerable<T>[/].Where(...) compiles directly to IL code (executed in-process)",
				$"  * [{CSharpBorder}]IQueryable<T>[/].Where(...) instead produces an [{Accent}]expression tree[/] that can be inspected and translated at runtime (this is what makes LINQ providers possible)"
			))
		)
		{
			Border = BoxBorder.Rounded,
			Expand = true,
			Padding = new Padding(2, 1)
		});
	}

	private static void RenderScenario1()
	{
		WriteScenarioTitle("Scenario 1", "Basic method-syntax LINQ -> ES|QL");

		var query = new EsqlQueryable<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR" && l.Duration > 1000)
			.OrderByDescending(l => l.Timestamp)
			.Take(50);

		WriteCSharpCode(
			"LINQ expression",
			"""
			var query = new EsqlQueryable<LogEntry>()
				.From("logs-*")
				.Where(l => l.Level == "ERROR" && l.Duration > 1000)
				.OrderByDescending(l => l.Timestamp)
				.Take(50);
			"""
		);

		var tree = new Tree("");
		BuildExpressionTree(tree, query.Expression);
		AnsiConsole.Write(new Panel(tree)
		{
			Header = new PanelHeader(" Full expression tree (query.Expression) "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.White),
			Expand = true,
			Padding = new Padding(2, 1)
		});

		WriteEsqlOutput("Translated ES|QL", query.ToString());
	}

	private static void RenderScenario2()
	{
		WriteScenarioTitle("Scenario 2", "Captured closures: inline literals vs named parameters");

		var minStatus = 500;
		var level = "ERROR";

		var query = new EsqlQueryable<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= minStatus && l.Level == level)
			.Take(25);

		WriteCSharpCode(
			"C# with captured variables",
			"""
			var minStatus = 500;
			var level = "ERROR";

			var query = new EsqlQueryable<LogEntry>()
				.From("logs-*")
				.Where(l => l.StatusCode >= minStatus && l.Level == level)
				.Take(25);
			"""
		);

		var inlined = query.ToEsqlString();
		var parameterized = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		WriteEsqlOutput("Inline parameters (default)", inlined);
		WriteEsqlOutput("Named parameters", parameterized);
		RenderParameters(parameters);
	}

	private static void RenderScenario3()
	{
		WriteScenarioTitle("Scenario 3", "Select merging & nested projection flattening");

		var mergedProjection = new EsqlQueryable<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, Adjusted = l.StatusCode - 100 })
			.Select(x => new { x.Message, Doubled = x.Adjusted * 2 });

		var nestedProjection = new EsqlQueryable<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.Message } });

		WriteCSharpCode(
			"Chained Select",
			"""
			var query = new EsqlQueryable<LogEntry>()
				.From("logs-*")
				.Select(l => new { l.Message, Adjusted = l.StatusCode - 100 })
				.Select(x => new { x.Message, Doubled = x.Adjusted * 2 });
			"""
		);

		WriteEsqlOutput("Single optimized translation", mergedProjection.ToString());

		WriteCSharpCode(
			"Nested anonymous projection",
			"""
			var query = new EsqlQueryable<LogEntry>()
				.From("logs-*")
				.Select(l => new { A = new { B = l.Message } });
			"""
		);

		WriteEsqlOutput("Flattened", nestedProjection.ToString());
	}

	private static void RenderScenario4()
	{
		WriteScenarioTitle("Scenario 4", "GroupBy -> STATS ... BY");

		var grouped = new EsqlQueryable<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				Count = g.Count(),
				AvgDuration = g.Average(l => l.Duration)
			});

		WriteCSharpCode(
			"GroupBy",
			"""
			var grouped = new EsqlQueryable<LogEntry>()
				.From("logs-*")
				.GroupBy(l => l.Level)
				.Select(g => new
				{
					Level = g.Key,
					Count = g.Count(),
					AvgDuration = g.Average(l => l.Duration)
				});
			"""
		);

		WriteEsqlOutput("STATS ... BY", grouped.ToString());
	}

	private static void RenderScenario5()
	{
		WriteScenarioTitle("Scenario 5", "LOOKUP JOIN");

		var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

		var joined = new EsqlQueryable<LogEntry>()
			.From("employees")
			.LeftJoin(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			);

		WriteCSharpCode(
			"LeftJoin with IQueryable lookup source",
			"""
			var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

			var joined = new EsqlQueryable<LogEntry>()
				.From("employees")
				.LeftJoin(
					lookup,
					outer => outer.StatusCode,
					inner => inner.LanguageCode,
					(outer, inner) => new { outer.Message, inner!.LanguageName }
				);
			"""
		);

		WriteEsqlOutput("LOOKUP JOIN", joined.ToString());
	}

	private static void RenderOutro()
	{
		WriteHeader();

		AnsiConsole.Write(new Panel(
			new Markup(string.Join('\n',
				$"[{Accent}]Thanks for listening![/]",
				"",
				$"[{CSharpBorder}]Elastic.Esql[/] brings the full power of [{EsqlBorder}]ES[/]|[{EsqlBorder}]QL[/] to idiomatic C# through LINQ.",
				"",
				$"Find the full project on GitHub: [{CSharpBorder}]https://github.com/elastic/esql-dotnet[/]"
			))
		)
		{
			Border = BoxBorder.Double,
			BorderStyle = new Style(Color.Yellow),
			Expand = true,
			Padding = new Padding(2, 1)
		});
	}

	// ── Helpers: Panels & Rendering ─────────────────────────────────────

	private static void WriteHeader()
	{
		AnsiConsole.Write(new FigletText("Elastic.Esql").Color(Color.DodgerBlue1));
		AnsiConsole.Write(new Rule($"[{Accent}]LINQ to [{EsqlBorder}]ES[/]|[{EsqlBorder}]QL[/] showcase[/]").RuleStyle(Accent));
		AnsiConsole.WriteLine();
	}

	private static void WriteScenarioTitle(string number, string title)
	{
		WriteHeader();
		AnsiConsole.Write(new Rule($"[{Accent}]{number}[/] - {title}").RuleStyle(Accent).LeftJustified());
		AnsiConsole.WriteLine();
	}

	private static void WriteCSharpCode(string title, string code)
	{
		var panel = new Panel(new Markup(HighlightCSharp(code)))
		{
			Header = new PanelHeader($" {title} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.DodgerBlue1),
			Expand = true,
			Padding = new Padding(2, 1)
		};

		AnsiConsole.Write(panel);
	}

	private static void WriteEsqlOutput(string title, string? esql)
	{
		var text = esql ?? string.Empty;
		var panel = new Panel(new Markup(HighlightEsql(text)))
		{
			Header = new PanelHeader($" {title} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Green3),
			Expand = true,
			Padding = new Padding(2, 1)
		};

		AnsiConsole.Write(panel);
	}

	private static void RenderParameters(EsqlParameters? parameters)
	{
		var table = new Table()
			.Border(TableBorder.Rounded)
			.BorderStyle(new Style(Color.Green3))
			.Expand()
			.AddColumn(new TableColumn($"[{Accent}]Name[/]"))
			.AddColumn(new TableColumn($"[{Accent}]Value[/]"));

		if (parameters is not null)
		{
			foreach (var (name, value) in parameters.Parameters)
				_ = table.AddRow(
					$"[{EsqlParamColor}]?{Markup.Escape(name)}[/]",
					$"[{NumberColor}]{Markup.Escape(value.GetRawText())}[/]"
				);
		}

		AnsiConsole.Write(new Padder(table, new Padding(0, 0, 0, 1)));
	}

	// ── Helpers: Expression Tree ────────────────────────────────────────

	private static void BuildExpressionTree(IHasTreeNodes parent, Expression node)
	{
		// For method call chains, flatten: recurse into the source (arg[0] for extension methods) first,
		// then render the current call at the same tree level with only the non-source args as children.
		if (node is MethodCallExpression call && call.Object is null && call.Arguments.Count > 0)
		{
			// Extension method: arg[0] is the source chain — render it first at the same parent level
			BuildExpressionTree(parent, call.Arguments[0]);

			var callLabel = $"[{KeywordColor}]Call[/] [{CSharpBorder}].{Markup.Escape(call.Method.Name)}()[/]";
			var callNode = parent.AddNode(callLabel);

			// Render remaining args (lambdas, constants) as children
			foreach (var arg in call.Arguments.Skip(1))
				BuildExpressionSubTree(callNode, arg);

			return;
		}

		// Non-chain root (e.g. ConstantExpression holding the IQueryable)
		if (node is ConstantExpression ce && ce.Value is IQueryable q)
		{
			_ = parent.AddNode($"[{KeywordColor}]Constant[/] [{TypeColor}]EsqlQueryable<{Markup.Escape(q.ElementType.Name)}>[/]");
			return;
		}

		// Fallback
		var label = FormatNodeLabel(node);
		_ = parent.AddNode(label);
	}

	private static void BuildExpressionSubTree(IHasTreeNodes parent, Expression node)
	{
		var label = FormatNodeLabel(node);
		var treeNode = parent.AddNode(label);

		switch (node)
		{
			case BinaryExpression binary:
				BuildExpressionSubTree(treeNode, binary.Left);
				BuildExpressionSubTree(treeNode, binary.Right);
				break;

			case MethodCallExpression call:
				if (call.Object is not null)
					BuildExpressionSubTree(treeNode, call.Object);
				foreach (var arg in call.Arguments)
					BuildExpressionSubTree(treeNode, arg);
				break;

			case MemberExpression member:
				if (member.Expression is not null)
					BuildExpressionSubTree(treeNode, member.Expression);
				break;

			case UnaryExpression unary:
				BuildExpressionSubTree(treeNode, unary.Operand);
				break;

			case LambdaExpression lambda:
				BuildExpressionSubTree(treeNode, lambda.Body);
				break;
		}
	}

	private static string FormatNodeLabel(Expression node) =>
		node switch
		{
			MethodCallExpression mc =>
				$"[{KeywordColor}]Call[/] [{CSharpBorder}].{Markup.Escape(mc.Method.Name)}()[/]",
			ConstantExpression ce =>
				$"[{KeywordColor}]Constant[/] [{NumberColor}]{Markup.Escape(ce.Value?.ToString() ?? "null")}[/]",
			MemberExpression me =>
				$"[{KeywordColor}]MemberAccess[/] [{CSharpBorder}].{Markup.Escape(me.Member.Name)}[/]",
			ParameterExpression pe =>
				$"[{KeywordColor}]Parameter[/] [{CSharpBorder}]{Markup.Escape(pe.Name ?? "?")}[/]",
			LambdaExpression =>
				$"[{KeywordColor}]Lambda[/] [{Muted}](=>)[/]",
			_ =>
				$"[{KeywordColor}]{Markup.Escape(node.NodeType.ToString())}[/] [{Muted}]({Markup.Escape(node.GetType().Name)})[/]"
		};

	private static List<string> GetMethodChain(Expression expression)
	{
		var methodNames = new List<string>();
		var current = expression;

		while (current is MethodCallExpression call)
		{
			methodNames.Add(call.Method.Name);
			current = call.Arguments[0];
		}

		methodNames.Reverse();
		return methodNames;
	}

	// ── Helpers: Syntax Highlighting ────────────────────────────────────

	private static string HighlightCSharp(string code)
	{
		var escaped = Markup.Escape(code.Replace("\t", "    ").TrimEnd());

		// String literals (double-quoted)
		escaped = Regex.Replace(escaped, @"""[^""]*""", $"[{StringColor}]$0[/]");

		// Numbers
		escaped = Regex.Replace(escaped, @"\b(\d+)\b", $"[{NumberColor}]$1[/]");

		// Keywords
		string[] keywords =
		[
			"var", "new", "from", "in", "where", "orderby", "descending", "ascending",
			"select", "group", "by", "join", "on", "equals", "into", "let",
			"true", "false", "null", "string", "int", "double", "bool", "object"
		];

		foreach (var kw in keywords)
			escaped = Regex.Replace(escaped, $@"\b{kw}\b", $"[{KeywordColor}]{kw}[/]");

		// Type names (PascalCase after new or generic)
		escaped = Regex.Replace(
			escaped,
			@"(?<=new\s)([A-Z][A-Za-z0-9]+)",
			$"[{TypeColor}]$1[/]"
		);

		// LINQ methods
		string[] linqMethods = ["From", "Where", "Select", "OrderBy", "OrderByDescending", "Take", "GroupBy", "LookupJoin", "MultiField"];
		foreach (var method in linqMethods)
			escaped = Regex.Replace(escaped, $@"\.{method}\b", $".[{CSharpBorder}]{method}[/]");

		// Lambda arrows
		escaped = escaped.Replace("=>", $"[{KeywordColor}]=>[/]");

		return escaped;
	}

	private static string HighlightEsql(string esql)
	{
		var escaped = Markup.Escape(esql);

		// String literals
		escaped = Regex.Replace(escaped, @"""[^""]*""", $"[{StringColor}]$0[/]");

		// Numbers
		escaped = Regex.Replace(escaped, @"\b(\d+)\b", $"[{NumberColor}]$1[/]");

		// ES|QL commands (at line start, after pipe, or first word)
		string[] commands = ["FROM", "WHERE", "SORT", "LIMIT", "KEEP", "EVAL", "STATS", "RENAME", "LOOKUP JOIN", "BY", "AND", "OR", "NOT", "ASC", "DESC", "AS", "ON"];
		foreach (var cmd in commands)
			escaped = Regex.Replace(escaped, $@"\b{cmd}\b", $"[{EsqlCommandColor}]{cmd}[/]");

		// Named parameters (?name)
		escaped = Regex.Replace(escaped, @"\?(\w+)", $"[{EsqlParamColor}]?$1[/]");

		// Pipe operator — dim the pipe prefix
		escaped = Regex.Replace(escaped, @"^\| ", $"[{Muted}]|[/] ", RegexOptions.Multiline);

		// Aggregation functions
		string[] funcs = ["COUNT", "AVG", "SUM", "MIN", "MAX"];
		foreach (var func in funcs)
			escaped = Regex.Replace(escaped, $@"\b{func}\b", $"[{CSharpBorder}]{func}[/]");

		return escaped;
	}

}

public sealed class LogEntry
{
	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	public double Duration { get; set; }
}

public sealed class LanguageLookup
{
	public int LanguageCode { get; set; }

	public string LanguageName { get; set; } = string.Empty;
}
