using System.Globalization;

namespace BetterPad.Language;

internal sealed class Interpreter
{
    private const int MaxSteps = 5_000_000;
    private const int MaxCallDepth = 1_000;

    private readonly Action<string> _write;
    private readonly CancellationToken _cancellation;
    private readonly Environment _globals = new(null);
    private readonly Dictionary<string, FnDeclStmt> _functions = new(StringComparer.Ordinal);
    private int _steps;
    private int _depth;

    public Interpreter(Action<string> write, CancellationToken cancellation)
    {
        _write = write;
        _cancellation = cancellation;
    }

    public void Execute(List<Stmt> program)
    {
        // Register functions first so a program can call them in any order.
        foreach (Stmt statement in program)
        {
            if (statement is FnDeclStmt function)
            {
                if (_functions.ContainsKey(function.Name))
                {
                    throw Error($"Function '{function.Name}' is already declared.", function);
                }

                _functions.Add(function.Name, function);
            }
        }

        foreach (Stmt statement in program)
        {
            Execute(statement, _globals);
        }
    }

    private void Execute(Stmt statement, Environment environment)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (++_steps > MaxSteps)
        {
            throw Error($"Program stopped after {MaxSteps.ToString("N0", CultureInfo.InvariantCulture)} statements (possible infinite loop).", statement);
        }

        switch (statement)
        {
            case LetStmt let:
            {
                if (environment.ContainsLocal(let.Name))
                {
                    throw Error($"Variable '{let.Name}' is already declared in this block.", let);
                }

                environment.Declare(let.Name, Evaluate(let.Value, environment));
                break;
            }

            case AssignStmt assign:
            {
                object value = Evaluate(assign.Value, environment);
                if (!environment.TryAssign(assign.Name, value))
                {
                    throw Error($"Undefined variable '{assign.Name}'. Declare it with 'let' first.", assign);
                }

                break;
            }

            case ExprStmt exprStatement:
                Evaluate(exprStatement.Expression, environment);
                break;

            case IfStmt ifStatement:
            {
                foreach (IfBranch branch in ifStatement.Branches)
                {
                    object condition = Evaluate(branch.Condition, environment);
                    if (RequireBool(condition, branch.Condition, "The if condition"))
                    {
                        ExecuteBlock(branch.Body, environment);
                        return;
                    }
                }

                if (ifStatement.ElseBody != null)
                {
                    ExecuteBlock(ifStatement.ElseBody, environment);
                }

                break;
            }

            case WhileStmt whileStatement:
            {
                while (true)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    if (++_steps > MaxSteps)
                    {
                        throw Error($"Program stopped after {MaxSteps.ToString("N0", CultureInfo.InvariantCulture)} statements (possible infinite loop).", whileStatement);
                    }

                    object condition = Evaluate(whileStatement.Condition, environment);
                    if (!RequireBool(condition, whileStatement.Condition, "The while condition"))
                    {
                        break;
                    }

                    ExecuteBlock(whileStatement.Body, environment);
                }

                break;
            }

            case ReturnStmt { } returnStatement:
                throw new ReturnSignal(Evaluate(returnStatement.Value, environment));

            case FnDeclStmt:
                // Registered before execution; nothing to do here.
                break;

            default:
                throw Error("Unknown statement.", statement);
        }
    }

    private void ExecuteBlock(List<Stmt> statements, Environment parent)
    {
        var environment = new Environment(parent);
        foreach (Stmt statement in statements)
        {
            Execute(statement, environment);
        }
    }

    private object Evaluate(Expr expression, Environment environment)
    {
        _cancellation.ThrowIfCancellationRequested();

        switch (expression)
        {
            case LiteralExpr literal:
                return literal.Value;

            case VariableExpr variable:
            {
                if (!environment.TryGet(variable.Name, out object value))
                {
                    throw Error($"Undefined variable '{variable.Name}'.", variable);
                }

                return value;
            }

            case UnaryExpr unary:
            {
                object operand = Evaluate(unary.Operand, environment);
                return unary.Operator switch
                {
                    TokenType.Minus => -RequireNumber(operand, unary.Operand),
                    TokenType.Bang => !RequireBool(operand, unary.Operand, "The '!' operand"),
                    _ => throw Error("Unknown unary operator.", unary)
                };
            }

            case BinaryExpr binary:
                return EvaluateBinary(binary, environment);

            case CallExpr call:
                return Call(call, environment);

            default:
                throw Error("Unknown expression.", expression);
        }
    }

    private object EvaluateBinary(BinaryExpr expression, Environment environment)
    {
        // && and || short-circuit before evaluating the right side.
        if (expression.Operator == TokenType.AndAnd || expression.Operator == TokenType.OrOr)
        {
            bool left = RequireBool(Evaluate(expression.Left, environment), expression.Left, "The left operand");
            if (expression.Operator == TokenType.AndAnd)
            {
                return left && RequireBool(Evaluate(expression.Right, environment), expression.Right, "The right operand");
            }

            return left || RequireBool(Evaluate(expression.Right, environment), expression.Right, "The right operand");
        }

        object lhs = Evaluate(expression.Left, environment);
        object rhs = Evaluate(expression.Right, environment);

        switch (expression.Operator)
        {
            case TokenType.Plus:
                if (lhs is string || rhs is string)
                {
                    return Stringify(lhs) + Stringify(rhs);
                }

                return Arithmetic(RequireNumber(lhs, expression.Left), RequireNumber(rhs, expression.Right),
                    expression, static (a, b) => a + b);

            case TokenType.Minus:
                return Arithmetic(RequireNumber(lhs, expression.Left), RequireNumber(rhs, expression.Right),
                    expression, static (a, b) => a - b);

            case TokenType.Star:
                return Arithmetic(RequireNumber(lhs, expression.Left), RequireNumber(rhs, expression.Right),
                    expression, static (a, b) => a * b);

            case TokenType.Slash:
            {
                double divisor = RequireNumber(rhs, expression.Right);
                if (divisor == 0)
                {
                    throw Error("Division by zero.", expression);
                }

                return Arithmetic(RequireNumber(lhs, expression.Left), divisor,
                    expression, static (a, b) => a / b);
            }

            case TokenType.Percent:
            {
                double divisor = RequireNumber(rhs, expression.Right);
                if (divisor == 0)
                {
                    throw Error("Division by zero.", expression);
                }

                return Arithmetic(RequireNumber(lhs, expression.Left), divisor,
                    expression, static (a, b) => a % b);
            }

            case TokenType.EqualEqual:
                return ValuesEqual(lhs, rhs);

            case TokenType.BangEqual:
                return !ValuesEqual(lhs, rhs);

            case TokenType.Less:
            case TokenType.LessEqual:
            case TokenType.Greater:
            case TokenType.GreaterEqual:
                return Compare(lhs, rhs, expression);

            default:
                throw Error("Unknown binary operator.", expression);
        }
    }

    private object Arithmetic(double left, double right, Expr expression, Func<double, double, double> operation)
    {
        double result = operation(left, right);
        if (!double.IsFinite(result))
        {
            throw Error("Number overflow.", expression);
        }

        return result;
    }

    private bool Compare(object lhs, object rhs, BinaryExpr expression)
    {
        int comparison;
        if (lhs is double left && rhs is double right)
        {
            comparison = left.CompareTo(right);
        }
        else if (lhs is string leftText && rhs is string rightText)
        {
            comparison = string.CompareOrdinal(leftText, rightText);
        }
        else
        {
            throw Error($"Cannot compare {TypeName(lhs)} and {TypeName(rhs)}.", expression);
        }

        return expression.Operator switch
        {
            TokenType.Less => comparison < 0,
            TokenType.LessEqual => comparison <= 0,
            TokenType.Greater => comparison > 0,
            _ => comparison >= 0
        };
    }

    private object Call(CallExpr call, Environment environment)
    {
        if (!_functions.TryGetValue(call.Name, out FnDeclStmt? function))
        {
            if (call.Name == "print")
            {
                if (call.Arguments.Count != 1)
                {
                    throw Error("print expects exactly one value.", call);
                }

                _write(Stringify(Evaluate(call.Arguments[0], environment)) + System.Environment.NewLine);
                return 0d;
            }

            throw Error($"Unknown function '{call.Name}'.", call);
        }

        if (call.Arguments.Count != function.Parameters.Count)
        {
            throw Error(
                $"Function '{call.Name}' expects {function.Parameters.Count} argument(s), got {call.Arguments.Count}.",
                call);
        }

        var values = new object[call.Arguments.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Evaluate(call.Arguments[i], environment);
        }

        if (++_depth > MaxCallDepth)
        {
            _depth--;
            throw Error($"Call stack overflow while calling '{call.Name}'.", call);
        }

        var callEnvironment = new Environment(_globals);
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            callEnvironment.Declare(function.Parameters[i], values[i]);
        }

        try
        {
            ExecuteBlock(function.Body, callEnvironment);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
        finally
        {
            _depth--;
        }

        throw Error($"Function '{call.Name}' finished without returning a value.", call);
    }

    private static bool ValuesEqual(object lhs, object rhs)
    {
        if (lhs is double left && rhs is double right)
        {
            return left == right;
        }

        if (lhs is string leftText && rhs is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (lhs is bool leftBool && rhs is bool rightBool)
        {
            return leftBool == rightBool;
        }

        return false;
    }

    private static double RequireNumber(object value, Expr expression)
    {
        if (value is double number)
        {
            return number;
        }

        throw new TinyThrownException($"Expected a number, found {TypeName(value)}.", expression.Line, expression.Column);
    }

    private static bool RequireBool(object value, Expr expression, string context)
    {
        if (value is bool flag)
        {
            return flag;
        }

        throw new TinyThrownException($"{context} must be a boolean, found {TypeName(value)}.", expression.Line, expression.Column);
    }

    private static string TypeName(object value) => value switch
    {
        double => "a number",
        string => "text",
        bool => "a boolean",
        _ => "an unknown value"
    };

    internal static string Stringify(object value) => value switch
    {
        double number => FormatNumber(number),
        string text => text,
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatNumber(double number)
    {
        if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
        {
            return ((long)number).ToString(CultureInfo.InvariantCulture);
        }

        return number.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static TinyThrownException Error(string message, Expr expression) =>
        new(message, expression.Line, expression.Column);

    private static TinyThrownException Error(string message, Stmt statement) =>
        new(message, statement.Line, statement.Column);

    private sealed class ReturnSignal : Exception
    {
        public ReturnSignal(object value)
        {
            Value = value;
        }

        public object Value { get; }
    }
}
