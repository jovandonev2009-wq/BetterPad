namespace BetterPad.Language;

internal abstract record Expr(int Line, int Column);

internal sealed record LiteralExpr(object Value, int Line, int Column) : Expr(Line, Column);

internal sealed record VariableExpr(string Name, int Line, int Column) : Expr(Line, Column);

internal sealed record UnaryExpr(TokenType Operator, Expr Operand, int Line, int Column) : Expr(Line, Column);

internal sealed record BinaryExpr(Expr Left, TokenType Operator, Expr Right, int Line, int Column) : Expr(Line, Column);

internal sealed record CallExpr(string Name, List<Expr> Arguments, int Line, int Column) : Expr(Line, Column);

internal abstract record Stmt(int Line, int Column);

internal sealed record LetStmt(string Name, Expr Value, int Line, int Column) : Stmt(Line, Column);

internal sealed record AssignStmt(string Name, Expr Value, int Line, int Column) : Stmt(Line, Column);

internal sealed record ExprStmt(Expr Expression, int Line, int Column) : Stmt(Line, Column);

internal sealed record IfBranch(Expr Condition, List<Stmt> Body);

internal sealed record IfStmt(
    List<IfBranch> Branches,
    List<Stmt>? ElseBody,
    int Line,
    int Column) : Stmt(Line, Column);

internal sealed record WhileStmt(Expr Condition, List<Stmt> Body, int Line, int Column) : Stmt(Line, Column);

internal sealed record ReturnStmt(Expr Value, int Line, int Column) : Stmt(Line, Column);

internal sealed record FnDeclStmt(string Name, List<string> Parameters, List<Stmt> Body, int Line, int Column)
    : Stmt(Line, Column);
