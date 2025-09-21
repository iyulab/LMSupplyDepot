using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace LMSupplyDepots.External.LLamaEngine.Templates;

/// <summary>
/// Complete Jinja2 template parser with advanced features
/// </summary>
public interface IJinja2Parser
{
    /// <summary>
    /// Parse and render a Jinja2 template with context data
    /// </summary>
    string Render(string template, object context);

    /// <summary>
    /// Parse and render a Jinja2 template with dictionary context
    /// </summary>
    string Render(string template, Dictionary<string, object?> context);

    /// <summary>
    /// Register a custom filter
    /// </summary>
    void RegisterFilter(string name, Func<object?, object[], object?> filter);

    /// <summary>
    /// Register a custom function
    /// </summary>
    void RegisterFunction(string name, Func<object[], object?> function);

    /// <summary>
    /// Validate template syntax
    /// </summary>
    bool ValidateTemplate(string template, out List<string> errors);
}

/// <summary>
/// Advanced Jinja2 template parser implementation
/// </summary>
public class Jinja2Parser : IJinja2Parser
{
    private readonly ILogger<Jinja2Parser> _logger;
    private readonly Dictionary<string, Func<object?, object[], object?>> _filters;
    private readonly Dictionary<string, Func<object[], object?>> _functions;

    public Jinja2Parser(ILogger<Jinja2Parser> logger)
    {
        _logger = logger;
        _filters = new Dictionary<string, Func<object?, object[], object?>>();
        _functions = new Dictionary<string, Func<object[], object?>>();
        InitializeBuiltinFilters();
        InitializeBuiltinFunctions();
    }

    public string Render(string template, object context)
    {
        return Render(template, ObjectToDictionary(context));
    }

    public string Render(string template, Dictionary<string, object?> context)
    {
        try
        {
            var tokens = Tokenize(template);
            var ast = Parse(tokens);
            return Evaluate(ast, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering Jinja2 template");
            throw new InvalidOperationException($"Template rendering failed: {ex.Message}", ex);
        }
    }

    public void RegisterFilter(string name, Func<object?, object[], object?> filter)
    {
        _filters[name] = filter;
        _logger.LogDebug("Registered custom filter: {FilterName}", name);
    }

    public void RegisterFunction(string name, Func<object[], object?> function)
    {
        _functions[name] = function;
        _logger.LogDebug("Registered custom function: {FunctionName}", name);
    }

    public bool ValidateTemplate(string template, out List<string> errors)
    {
        errors = new List<string>();

        try
        {
            var tokens = Tokenize(template);
            var ast = Parse(tokens);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return false;
        }
    }

    private void InitializeBuiltinFilters()
    {
        // String filters
        _filters["upper"] = (value, args) => value?.ToString()?.ToUpperInvariant();
        _filters["lower"] = (value, args) => value?.ToString()?.ToLowerInvariant();
        _filters["title"] = (value, args) =>
        {
            var str = value?.ToString();
            return string.IsNullOrEmpty(str) ? str :
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.ToLowerInvariant());
        };
        _filters["capitalize"] = (value, args) =>
        {
            var str = value?.ToString();
            return string.IsNullOrEmpty(str) ? str :
                char.ToUpperInvariant(str[0]) + (str.Length > 1 ? str[1..].ToLowerInvariant() : "");
        };
        _filters["trim"] = (value, args) => value?.ToString()?.Trim();
        _filters["strip"] = (value, args) => value?.ToString()?.Trim();
        _filters["replace"] = (value, args) =>
        {
            var str = value?.ToString();
            if (string.IsNullOrEmpty(str) || args.Length < 2) return str;
            return str.Replace(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "");
        };

        // Length and format filters
        _filters["length"] = (value, args) =>
        {
            return value switch
            {
                string s => s.Length,
                Array a => a.Length,
                System.Collections.ICollection c => c.Count,
                _ => 0
            };
        };
        _filters["default"] = (value, args) => value ?? (args.Length > 0 ? args[0] : "");
        _filters["join"] = (value, args) =>
        {
            if (value is not System.Collections.IEnumerable enumerable) return value?.ToString();
            var separator = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
            return string.Join(separator, enumerable.Cast<object?>().Select(x => x?.ToString()));
        };

        // Type filters
        _filters["string"] = (value, args) => value?.ToString();
        _filters["int"] = (value, args) =>
        {
            if (int.TryParse(value?.ToString(), out var intVal)) return intVal;
            return 0;
        };
        _filters["float"] = (value, args) =>
        {
            if (double.TryParse(value?.ToString(), out var doubleVal)) return doubleVal;
            return 0.0;
        };
        _filters["bool"] = (value, args) =>
        {
            return value switch
            {
                null => false,
                bool b => b,
                string s => !string.IsNullOrEmpty(s) && s != "0" && s.ToLowerInvariant() != "false",
                int i => i != 0,
                double d => d != 0.0,
                _ => true
            };
        };

        _logger.LogDebug("Initialized {Count} builtin filters", _filters.Count);
    }

    private void InitializeBuiltinFunctions()
    {
        _functions["range"] = (args) =>
        {
            if (args.Length == 0) return Enumerable.Empty<int>();
            if (args.Length == 1 && int.TryParse(args[0]?.ToString(), out var end))
                return Enumerable.Range(0, end);
            if (args.Length >= 2 &&
                int.TryParse(args[0]?.ToString(), out var start) &&
                int.TryParse(args[1]?.ToString(), out var stop))
            {
                var step = 1;
                if (args.Length >= 3 && int.TryParse(args[2]?.ToString(), out var stepArg))
                    step = stepArg;

                if (step > 0)
                    return Enumerable.Range(start, Math.Max(0, (stop - start + step - 1) / step))
                                   .Select(i => start + i * step);
            }
            return Enumerable.Empty<int>();
        };

        _functions["now"] = (args) => DateTime.Now;
        _functions["utcnow"] = (args) => DateTime.UtcNow;

        _logger.LogDebug("Initialized {Count} builtin functions", _functions.Count);
    }

    private List<Token> Tokenize(string template)
    {
        var tokens = new List<Token>();
        var pos = 0;

        while (pos < template.Length)
        {
            // Find next template tag
            var nextTag = FindNextTag(template, pos);

            if (nextTag.Position > pos)
            {
                // Add text before tag
                tokens.Add(new Token(TokenType.Text, template[pos..nextTag.Position], pos));
            }

            if (nextTag.Position < template.Length)
            {
                tokens.Add(nextTag);
                pos = nextTag.Position + nextTag.Content.Length;
            }
            else
            {
                break;
            }
        }

        return tokens;
    }

    private Token FindNextTag(string template, int startPos)
    {
        var expressions = new[]
        {
            (@"\{\{(.+?)\}\}", TokenType.Expression),
            (@"\{%(.+?)%\}", TokenType.Statement),
            (@"\{#(.+?)#\}", TokenType.Comment)
        };

        var earliestPos = template.Length;
        Token? earliestToken = null;

        foreach (var (pattern, tokenType) in expressions)
        {
            var match = Regex.Match(template[startPos..], pattern, RegexOptions.Singleline);
            if (match.Success)
            {
                var pos = startPos + match.Index;
                if (pos < earliestPos)
                {
                    earliestPos = pos;
                    earliestToken = new Token(tokenType, match.Value, pos, match.Groups[1].Value.Trim());
                }
            }
        }

        return earliestToken ?? new Token(TokenType.Text, "", template.Length);
    }

    private AstNode Parse(List<Token> tokens)
    {
        var root = new AstNode(NodeType.Root);
        var nodeStack = new Stack<AstNode>();
        nodeStack.Push(root);

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var current = nodeStack.Peek();

            switch (token.Type)
            {
                case TokenType.Text:
                    current.Children.Add(new AstNode(NodeType.Text, token.Content));
                    break;

                case TokenType.Expression:
                    current.Children.Add(ParseExpression(token.Inner));
                    break;

                case TokenType.Statement:
                    var statement = ParseStatement(token.Inner);
                    if (IsBlockStart(statement))
                    {
                        current.Children.Add(statement);
                        nodeStack.Push(statement);
                    }
                    else if (IsBlockEnd(statement))
                    {
                        if (nodeStack.Count > 1)
                            nodeStack.Pop();
                    }
                    else
                    {
                        current.Children.Add(statement);
                    }
                    break;

                case TokenType.Comment:
                    // Skip comments
                    break;
            }
        }

        return root;
    }

    private AstNode ParseExpression(string expression)
    {
        // Handle filters: variable|filter:arg1:arg2
        var parts = expression.Split('|');
        var variable = parts[0].Trim();

        var node = new AstNode(NodeType.Expression, variable);

        if (parts.Length > 1)
        {
            for (int i = 1; i < parts.Length; i++)
            {
                var filterParts = parts[i].Split(':');
                var filterName = filterParts[0].Trim();
                var filterArgs = filterParts.Skip(1).Select(arg => arg.Trim().Trim('\'')).ToArray();

                var filterNode = new AstNode(NodeType.Filter, filterName);
                foreach (var arg in filterArgs)
                {
                    filterNode.Children.Add(new AstNode(NodeType.Literal, arg));
                }
                node.Children.Add(filterNode);
            }
        }

        return node;
    }

    private AstNode ParseStatement(string statement)
    {
        var trimmed = statement.Trim();

        if (trimmed.StartsWith("for "))
        {
            var match = Regex.Match(trimmed, @"for\s+(\w+)\s+in\s+(.+)");
            if (match.Success)
            {
                var node = new AstNode(NodeType.ForLoop, match.Groups[1].Value);
                node.Children.Add(new AstNode(NodeType.Expression, match.Groups[2].Value));
                return node;
            }
        }
        else if (trimmed.StartsWith("if "))
        {
            var condition = trimmed[3..].Trim();
            var node = new AstNode(NodeType.IfStatement, condition);
            return node;
        }
        else if (trimmed == "endif" || trimmed == "endfor" || trimmed.StartsWith("elif ") || trimmed == "else")
        {
            return new AstNode(NodeType.BlockEnd, trimmed);
        }

        return new AstNode(NodeType.Statement, trimmed);
    }

    private bool IsBlockStart(AstNode node)
    {
        return node.Type == NodeType.ForLoop || node.Type == NodeType.IfStatement;
    }

    private bool IsBlockEnd(AstNode node)
    {
        return node.Type == NodeType.BlockEnd;
    }

    private string Evaluate(AstNode node, Dictionary<string, object?> context)
    {
        var result = new StringBuilder();

        switch (node.Type)
        {
            case NodeType.Root:
                foreach (var child in node.Children)
                {
                    result.Append(Evaluate(child, context));
                }
                break;

            case NodeType.Text:
                result.Append(node.Value);
                break;

            case NodeType.Expression:
                var value = EvaluateExpression(node, context);

                // Apply filters
                foreach (var filterNode in node.Children.Where(c => c.Type == NodeType.Filter))
                {
                    value = ApplyFilter(filterNode.Value, value, filterNode.Children, context);
                }

                result.Append(value?.ToString() ?? "");
                break;

            case NodeType.ForLoop:
                var items = EvaluateExpression(node.Children[0], context);
                if (items is System.Collections.IEnumerable enumerable)
                {
                    var loopVar = node.Value;
                    var originalValue = context.TryGetValue(loopVar, out var orig) ? orig : null;
                    var hasOriginal = context.ContainsKey(loopVar);

                    foreach (var item in enumerable)
                    {
                        context[loopVar] = item;
                        foreach (var child in node.Children.Skip(1))
                        {
                            result.Append(Evaluate(child, context));
                        }
                    }

                    // Restore original value
                    if (hasOriginal)
                        context[loopVar] = originalValue;
                    else
                        context.Remove(loopVar);
                }
                break;

            case NodeType.IfStatement:
                var condition = EvaluateCondition(node.Value, context);
                if (condition)
                {
                    foreach (var child in node.Children)
                    {
                        result.Append(Evaluate(child, context));
                    }
                }
                break;
        }

        return result.ToString();
    }

    private object? EvaluateExpression(AstNode node, Dictionary<string, object?> context)
    {
        var expression = node.Value;

        // Handle literals
        if (expression.StartsWith("'") && expression.EndsWith("'"))
        {
            return expression[1..^1];
        }

        if (int.TryParse(expression, out var intVal))
            return intVal;

        if (double.TryParse(expression, out var doubleVal))
            return doubleVal;

        // Handle variables with dot notation
        var parts = expression.Split('.');
        var value = context.TryGetValue(parts[0], out var baseValue) ? baseValue : null;

        for (int i = 1; i < parts.Length && value != null; i++)
        {
            var property = parts[i];
            value = GetProperty(value, property);
        }

        return value;
    }

    private object? GetProperty(object obj, string propertyName)
    {
        if (obj == null) return null;

        var type = obj.GetType();
        var property = type.GetProperty(propertyName);
        if (property != null)
        {
            return property.GetValue(obj);
        }

        var field = type.GetField(propertyName);
        if (field != null)
        {
            return field.GetValue(obj);
        }

        // Handle dictionary access
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.TryGetValue(propertyName, out var dictValue) ? dictValue : null;
        }

        return null;
    }

    private object? ApplyFilter(string filterName, object? value, List<AstNode> args, Dictionary<string, object?> context)
    {
        if (!_filters.TryGetValue(filterName, out var filter))
        {
            _logger.LogWarning("Unknown filter: {FilterName}", filterName);
            return value;
        }

        var filterArgs = args.Select(arg => EvaluateExpression(arg, context) ?? string.Empty).ToArray();
        return filter(value, filterArgs);
    }

    private bool EvaluateCondition(string condition, Dictionary<string, object?> context)
    {
        // Simple condition evaluation - can be extended
        var parts = condition.Split(new[] { "==", "!=", ">" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var left = EvaluateExpression(new AstNode(NodeType.Expression, parts[0].Trim()), context);
            var right = EvaluateExpression(new AstNode(NodeType.Expression, parts[1].Trim()), context);

            if (condition.Contains("=="))
                return Equals(left, right);
            if (condition.Contains("!="))
                return !Equals(left, right);
        }

        // Default: check truthiness
        var value = EvaluateExpression(new AstNode(NodeType.Expression, condition), context);
        return IsTruthy(value);
    }

    private bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            double d => d != 0.0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
    }

    private Dictionary<string, object?> ObjectToDictionary(object obj)
    {
        if (obj is Dictionary<string, object?> dict)
            return dict;

        var result = new Dictionary<string, object?>();
        var properties = obj.GetType().GetProperties();

        foreach (var prop in properties)
        {
            result[prop.Name] = prop.GetValue(obj);
        }

        return result;
    }
}

// Token and AST node definitions
public enum TokenType { Text, Expression, Statement, Comment }
public enum NodeType { Root, Text, Expression, Statement, ForLoop, IfStatement, Filter, Literal, BlockEnd }

public record Token(TokenType Type, string Content, int Position = 0, string Inner = "");
public class AstNode
{
    public NodeType Type { get; }
    public string Value { get; }
    public List<AstNode> Children { get; } = new();

    public AstNode(NodeType type, string value = "")
    {
        Type = type;
        Value = value;
    }
}