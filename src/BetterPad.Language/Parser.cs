namespace BetterPad.Language;

internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _current;
    private int _functionDepth;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public List<Stmt> ParseProgram()
    {
        var statements = new List<Stmt>();
        while (!AtEnd)
        {
            statements.Add(ParseStatement(topLevel: true));
        }

        return statements;
    }

    private Stmt ParseStatement(bool topLevel)
    {
        if (Match(TokenType.Let))
        {
            return ParseLet();
        }

        if (Match(TokenType.Fn))
        {
            if (!topLevel)
            {
                throw Error(Previous, "Functions can only be declared at the top level.");
            }

            return ParseFunction();
        }

        if (Match(TokenType.If))
        {
            return ParseIf(Previous);
        }

        if (Match(TokenType.While))
        {
            return ParseWhile(Previous);
        }

        if (Match(TokenType.Return))
        {
            Token keyword = Previous;
            if (_functionDepth == 0)
            {
                throw Error(keyword, "'return' is only allowed inside a function.");
            }

            Expr value = ParseExpression();
            Expect(TokenType.Semicolon, "Expected ';' after return value.");
            return new ReturnStmt(value, keyword.Line, keyword.Column);
        }

        if (Check(TokenType.LeftBrace))
        {
            throw Error(Peek, "A '{' block is only allowed after if, else, while, or fn.");
        }

        if (Check(TokenType.Identifier))
        {
            Token name = Advance();
            if (Match(TokenType.Assign))
            {
                Expr value = ParseExpression();
                Expect(TokenType.Semicolon, "Expected ';' after assignment.");
                return new AssignStmt(name.Lexeme, value, name.Line, name.Column);
            }

            _current--; // let the expression parser consume the identifier
            Expr expression = ParseExpression();
            if (expression is not CallExpr)
            {
                throw Error(name, "Only assignments and function calls can be used as statements.");
            }

            Expect(TokenType.Semicolon, "Expected ';' after expression.");
            return new ExprStmt(expression, name.Line, name.Column);
        }

        throw Error(Peek, "Expected a statement (let, fn, if, while, return, assignment, or call).");
    }

    private LetStmt ParseLet()
    {
        Token keyword = Previous;
        Token name = Expect(TokenType.Identifier, "Expected a variable name after 'let'.");
        Expect(TokenType.Assign, "Expected '=' after the variable name.");
        Expr value = ParseExpression();
        Expect(TokenType.Semicolon, "Expected ';' after the value.");
        return new LetStmt(name.Lexeme, value, keyword.Line, keyword.Column);
    }

    private FnDeclStmt ParseFunction()
    {
        Token keyword = Previous;
        Token name = Expect(TokenType.Identifier, "Expected a function name after 'fn'.");
        Expect(TokenType.LeftParen, "Expected '(' after the function name.");

        var parameters = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!Check(TokenType.RightParen))
        {
            do
            {
                Token parameter = Expect(TokenType.Identifier, "Expected a parameter name.");
                if (!seen.Add(parameter.Lexeme))
                {
                    throw Error(parameter, $"Duplicate parameter '{parameter.Lexeme}'.");
                }

                parameters.Add(parameter.Lexeme);
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RightParen, "Expected ')' after the parameters.");
        _functionDepth++;
        List<Stmt> body;
        try
        {
            body = ParseBlock(topLevel: false);
        }
        finally
        {
            _functionDepth--;
        }

        return new FnDeclStmt(name.Lexeme, parameters, body, keyword.Line, keyword.Column);
    }

    private IfStmt ParseIf(Token keyword)
    {
        Expr condition = ParseCondition();
        var branches = new List<IfBranch> { new(condition, ParseBlock(topLevel: false)) };
        List<Stmt>? elseBody = null;

        while (Match(TokenType.Else))
        {
            if (Match(TokenType.If))
            {
                branches.Add(new IfBranch(ParseCondition(), ParseBlock(topLevel: false)));
            }
            else
            {
                elseBody = ParseBlock(topLevel: false);
                break;
            }
        }

        return new IfStmt(branches, elseBody, keyword.Line, keyword.Column);
    }

    private WhileStmt ParseWhile(Token keyword)
    {
        Expr condition = ParseCondition();
        List<Stmt> body = ParseBlock(topLevel: false);
        return new WhileStmt(condition, body, keyword.Line, keyword.Column);
    }

    private Expr ParseCondition()
    {
        Expect(TokenType.LeftParen, "Expected '(' before the condition.");
        Expr condition = ParseExpression();
        Expect(TokenType.RightParen, "Expected ')' after the condition.");
        return condition;
    }

    private List<Stmt> ParseBlock(bool topLevel)
    {
        Expect(TokenType.LeftBrace, "Expected '{' to start a block.");
        var statements = new List<Stmt>();
        while (!Check(TokenType.RightBrace) && !AtEnd)
        {
            statements.Add(ParseStatement(topLevel));
        }

        Expect(TokenType.RightBrace, "Expected '}' to close the block.");
        return statements;
    }

    private Expr ParseExpression() => ParseOr();

    private Expr ParseOr()
    {
        Expr expression = ParseAnd();
        while (Match(TokenType.OrOr))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseAnd(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseAnd()
    {
        Expr expression = ParseEquality();
        while (Match(TokenType.AndAnd))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseEquality(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseEquality()
    {
        Expr expression = ParseComparison();
        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseComparison(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseComparison()
    {
        Expr expression = ParseTerm();
        while (Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseTerm(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseTerm()
    {
        Expr expression = ParseFactor();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseFactor(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseFactor()
    {
        Expr expression = ParseUnary();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            Token op = Previous;
            expression = new BinaryExpr(expression, op.Type, ParseUnary(), op.Line, op.Column);
        }

        return expression;
    }

    private Expr ParseUnary()
    {
        if (Match(TokenType.Minus, TokenType.Bang))
        {
            Token op = Previous;
            return new UnaryExpr(op.Type, ParseUnary(), op.Line, op.Column);
        }

        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        if (Match(TokenType.Number, TokenType.String, TokenType.True, TokenType.False))
        {
            Token token = Previous;
            object value = token.Type switch
            {
                TokenType.True => true,
                TokenType.False => false,
                _ => token.Literal
            };
            return new LiteralExpr(value, token.Line, token.Column);
        }

        if (Match(TokenType.LeftParen))
        {
            Expr inner = ParseExpression();
            Expect(TokenType.RightParen, "Expected ')' after the expression.");
            return inner;
        }

        if (Match(TokenType.Identifier))
        {
            Token name = Previous;
            if (!Match(TokenType.LeftParen))
            {
                return new VariableExpr(name.Lexeme, name.Line, name.Column);
            }

            var arguments = new List<Expr>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    arguments.Add(ParseExpression());
                }
                while (Match(TokenType.Comma));
            }

            Expect(TokenType.RightParen, "Expected ')' after the arguments.");
            return new CallExpr(name.Lexeme, arguments, name.Line, name.Column);
        }

        throw Error(Peek, "Expected an expression.");
    }

    private bool AtEnd => Peek.Type == TokenType.EndOfFile;

    private Token Peek => _tokens[_current];

    private Token Previous => _tokens[_current - 1];

    private bool Check(TokenType type) => Peek.Type == type;

    private Token Advance()
    {
        if (!AtEnd)
        {
            _current++;
        }

        return Previous;
    }

    private bool Match(params TokenType[] types)
    {
        foreach (TokenType type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }

        return false;
    }

    private Token Expect(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw Error(Peek, message);
    }

    private static TinyThrownException Error(Token token, string message)
    {
        string found = token.Type switch
        {
            TokenType.EndOfFile => "end of file",
            TokenType.String => "text",
            TokenType.Number => "number",
            _ => $"'{token.Lexeme}'"
        };
        return new TinyThrownException($"{message} Found {found}.", token.Line, token.Column);
    }
}
