# Code Style Guidelines

This project follows strict C# coding standards. All code must adhere to these guidelines.

## Formatting

- **Indentation**: Tabs (4-space width)
- **Max line length**: 160 characters
- **Brace style**: Allman (new line before all braces)
- **File-scoped namespaces**: Always use `namespace Foo;` not `namespace Foo { }`
- **Trailing whitespace**: Never
- **Final newline**: Always

### File Header (Required)

Every `.cs` file must start with:
```csharp
// Licensed to Elasticsearch B.V underaone or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
```

## Naming Conventions

| Element                 | Style                  | Example                               |
|-------------------------|------------------------|---------------------------------------|
| Private fields          | `_camelCase`           | `private readonly IService _service;` |
| Constants               | `PascalCase`           | `public const int MaxRetries = 3;`    |
| Public members          | `PascalCase`           | `public string Name { get; }`         |
| Locals/parameters       | `camelCase`            | `var userName = GetName();`           |
| Local functions         | `PascalCase`           | `void ProcessItem() { }`              |
| Async methods (public)  | `PascalCase` + `Async` | `public Task<T> GetDataAsync()`       |
| Async methods (private) | `PascalCase`           | `private Task<T> GetData()`           |

## Language Features (REQUIRED)

These are **error-level** requirements:

- **Always use `var`** — never write explicit types
- **No `this.` qualifier** — never prefix with `this`
- **Use language keywords** — `int` not `Int32`, `string` not `String`
- **Object/collection initializers** — always use when applicable
- **Null propagation** — use `?.` and `??` operators
- **Pattern matching** — prefer over `is`/`as` with cast/null checks
- **Inlined variable declarations** — use `out var x`
- **Throw expressions** — use when appropriate
- **Conditional delegate calls** — use `?.Invoke()`

## Expression Bodies

- **Prefer `=>` always** for all members when possible
- **Multi-line expressions**: Put newline after `=>`
- **Keep under 160 chars** for single-line

```csharp
// GOOD - single line
public string Name => _name;

// GOOD - multi-line with newline after =>
public string FormattedName =>
	$"{FirstName} {LastName}";

// BAD - too long on one line
public string VeryLongPropertyName => SomeMethod(argument1, argument2, argument3);
```

## Class Structure Order

1. **Fields** (private first)
2. **Constructors** (prefer primary constructors)
3. **Properties**
4. **Methods** (grouped by related functionality, NOT by visibility)

```csharp
public class UserService(IRepository repository, ILogger logger)
{
	private readonly Dictionary<string, User> _cache = [];

	public int CacheSize => _cache.Count;

	// Related methods grouped together
	public User GetUser(string id) => ...
	private User LoadFromCache(string id) => ...
	private void UpdateCache(User user) { ... }

	// Another logical group
	public void DeleteUser(string id) => ...
	private void InvalidateCache(string id) => ...
}
```

## Modern C# Preferences

- **Primary constructors** for dependency injection
- **Records liberally** for immutable data types
- **Collection expressions**: Use `[]` syntax (C# 12+)
- **Switch expressions** over if/else chains
- **Early returns** with guard clauses
- **Using declarations** (not using statements with braces)
- **Empty returns**: Use `[]` not `Array.Empty<T>()`

```csharp
// GOOD
public record UserDto(string Id, string Name, string Email);

// GOOD
var items = [];
return [];

// GOOD
using var stream = File.OpenRead(path);

// BAD
using (var stream = File.OpenRead(path)) { }
```

## Object & Collection Initializers

- **Single line** when it fits under 160 chars
- **Multi-line** only when exceeding line length

```csharp
// GOOD - fits on one line
var point = new { X = 10, Y = 20 };
var user = new UserDto { Id = "123", Name = "John" };

// GOOD - exceeds line length, so multi-line
var config = new AppConfiguration
{
	ConnectionString = "Server=localhost;Database=mydb",
	MaxRetries = 3,
	TimeoutSeconds = 30
};
```

## Multi-line Invocations

When a method call spans multiple lines, the closing parenthesis goes on its own line, indented to the method level:

```csharp
// CORRECT
var result = DoSomething(
	argument1,
	argument2,
	argument3
);

// CORRECT - chained
var query = items
	.Where(x => x.IsActive)
	.Select(x => new { x.Id, x.Name })
	.ToList();

// WRONG - closing paren not on own line
var result = DoSomething(argument1,
	argument2);

// WRONG - closing paren over-indented
var result = DoSomething(
	argument1
);
```

## LINQ Style

- **Method syntax** preferred over query syntax
- **Each method on new line** for readability
- **Query syntax** only for complex joins/multiple sources

```csharp
// GOOD
var activeUsers = users
	.Where(u => u.IsActive)
	.OrderBy(u => u.Name)
	.Select(u => u.Email)
	.ToList();

// ACCEPTABLE - complex join
var result =
	from order in orders
	join customer in customers on order.CustomerId equals customer.Id
	where customer.IsActive
	select new { order.Id, customer.Name };
```

## Strings

- **Interpolation** for single-line: `$"Hello {name}"`
- **Raw string literals** for multi-line

```csharp
// GOOD
var message = $"User {user.Name} logged in at {DateTime.Now}";

// GOOD
var json = """
	{
		"name": "test",
		"value": 123
	}
	""";
```

## Error Handling

- **Minimal try-catch** — catch at boundaries only
- **Let exceptions propagate** through the stack
- **No empty catch blocks**

## Dispose Pattern

- **GC.SuppressFinalize only when needed** — only if the class has a finalizer or manages unmanaged resources
- **Most IDisposable implementations don't need it**

```csharp
// GOOD - simple disposable, no finalizer
public class SimpleResource : IDisposable
{
	private readonly Stream _stream;

	public void Dispose() => _stream.Dispose();
}

// GOOD - has finalizer/unmanaged resources
public class UnmanagedResource : IDisposable
{
	private IntPtr _handle;

	~UnmanagedResource() => Dispose(false);

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);  // Only needed here
	}

	protected virtual void Dispose(bool disposing) { ... }
}
```

## Comments & XML Documentation

- **Self-documenting code** — comments explain "why" not "what"
- **XML docs** for public API members
- **Explain complex logic** and business rules
- **Single-line summary** when it fits — only `<summary>` is needed

```csharp
/// <summary>Retrieves user by ID with caching.</summary>
public User GetUser(string id)
{
	// Check cache first to avoid expensive DB call
	if (_cache.TryGetValue(id, out var cached))
		return cached;

	return LoadFromDatabase(id);
}

// GOOD - single line summary
/// <summary>Gets the user's display name.</summary>
public string DisplayName => $"{FirstName} {LastName}";

// GOOD - multi-line only when needed
/// <summary>
/// Performs a complex calculation that requires detailed explanation
/// across multiple lines because the logic is non-obvious.
/// </summary>
public decimal CalculateScore() => ...
```

## Testing

- **Framework**: TUnit (for new projects)
- **Assertions**: AwesomeAssertions (fluent style)
- **Method naming**: `MethodName_Scenario_Expected`
- **Structure**: Keep tests minimal and focused

```csharp
[Test]
public void GetUser_WithValidId_ReturnsUser()
{
	var user = _service.GetUser("123");

	user.Should().NotBeNull();
	user.Id.Should().Be("123");
}

[Test]
public void GetUser_WithInvalidId_ThrowsNotFoundException()
{
	var act = () => _service.GetUser("invalid");

	act.Should().Throw<NotFoundException>();
}
```

## Anti-Patterns to AVOID

| Never Do This                        | Do This Instead                           |
|--------------------------------------|-------------------------------------------|
| `#region` directives                 | Organize with partial classes or refactor |
| Multiple statements per line         | One statement per line                    |
| `new List<string>()` when type known | `new()` or `[]`                           |
| `this.field`                         | `_field` or just `field`                  |
| `String`, `Int32`                    | `string`, `int`                           |
| `if (x != null) x.Method()`          | `x?.Method()`                             |

## Modifier Order

Always order modifiers as:
```
public, private, protected, internal, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, volatile, async
```

## Nullable Reference Types

- **Strict nullable annotations** — always annotate with `?`
- **Use null-forgiving `!` sparingly** — only when obviously safe
- **Prefer null checks** and pattern matching
