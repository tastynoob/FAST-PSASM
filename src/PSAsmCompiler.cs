using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PSASM
{
    public enum TokenType
    {
        // 0-127 are reserved
        // 128-255 used for bnf tokens
        KEY_if = 256,
        KEY_end,
        KEY_else,
        KEY_while,
        OPERLV0,
        OPERLV5, // * /
        OPERLV6, // + -
        OPERLV7, // << >> >>>
        OPERLV8, // < > <= >= == !=
        OPERLV9, // == !=
        OPERLV10, // bit &
        OPERLV11, // bit ^
        OPERLV12, // bit |
        NUM_OPERS,
        ASSIGN, // =
        LPAREN, // (
        RPAREN, // )
        SEMICOLON, // ;
        COMMA, // ,
        SYMBOL,
        NUMBER,
        FIELD,
        ENDL,
        EOF,
    }

    public class AstNode(int tt, int row, int col)
    {
        public readonly int tt = tt;
        public readonly int row = row, col = col;
        public virtual JsonObject ToJson()
        {
            throw new NotImplementedException();
        }
    }

    public class AstToken(in string value, int tt, int row, int col) : AstNode(tt, row, col)
    {
        public string value = value;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "token" },
                { "value", value },
            };
            return obj;
        }
    }

    class AstNumber(in AstNode token) : AstNode(token.tt, token.row, token.col)
    {
        public int value = int.Parse((token as AstToken ?? throw new Exception("Unknown Object")).value);
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "number" },
                { "value", value }
            };
            return obj;
        }
    }

    class AstField(in AstNode token) : AstNode(token.tt, token.row, token.col)
    {
        public string field = (token as AstToken ?? throw new Exception("Unknown Object")).value;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "field" },
                { "field", field }
            };
            return obj;
        }
    }

    class AstBinOper(in AstNode left, in AstNode op, in AstNode right) : AstNode(op.tt, op.row, op.col)
    {
        public AstNode left = left, right = right;
        public string op = (op as AstToken ?? throw new Exception("Unknown Object")).value;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "binaryOper" },
                { "left", left.ToJson() },
                { "op", op },
                { "right", right.ToJson() },
            };
            return obj;
        }
    }

    class AstAssign(in AstNode dst, in AstNode src) : AstNode(dst.tt, dst.row, dst.col)
    {
        public AstNode dst = dst, src = src;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "assign"
                },
                { "dst", dst.ToJson()
                },
                { "src", src.ToJson()
                },
            };
            return obj;
        }
    }

    class AstIf(in AstNode condition, in AstNode body, in AstNode? elsebody) : AstNode(condition.tt, condition.row, condition.col)
    {
        public AstNode condition = condition, body = body;
        public AstNode? elsebody = elsebody;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "if"
                },
                { "condition", condition.ToJson()
                },
                { "body", body.ToJson()
                },
                { "elsebody", elsebody?.ToJson()
                },
            };
            return obj;
        }
    }

    class AstWhile(in AstNode condition, in AstNode body) : AstNode(condition.tt, condition.row, condition.col)
    {
        public AstNode condition = condition, body = body;
        public override JsonObject ToJson()
        {
            JsonObject obj = new()
            {
                { "type", "while" },
                { "condition", condition.ToJson() },
                { "body", body.ToJson() },
            };
            return obj;
        }
    }

    class AstArgsLst(in AstNode first, in AstNode arg) : AstNode(arg.tt, arg.row, arg.col)
    {
        public AstNode[] lst = first is AstArgsLst alst ? alst.lst.Concat([arg]).ToArray() : [first, arg];
    }

    class AstFuncCall(in AstToken func, in AstNode[] args) : AstNode(func.tt, func.row, func.col)
    {
        public string name = func.value;
        public AstNode[] args = args;
        public override JsonObject ToJson()
        {
            JsonArray jsonArray = [];
            for (int i = 0; i < args.Length; i++)
            {
                jsonArray.Add(args[i].ToJson());
            }
            JsonObject obj = new()
            {
                { "type", "funcCall" },
                { "func", name },
                { "args", jsonArray }
            };
            return obj;
        }
    }

    class AstEnd(in AstNode node) : AstNode(node.tt, node.row, node.col) { }
    class AstElse() { }

    class AstStatList(in AstNode first, in AstNode second) : AstNode(second.tt, second.row, second.col)
    {
        public AstNode[] lst = first is AstStatList alst ? alst.lst.Concat([second]).ToArray() : [first, second];
        public override JsonObject ToJson()
        {
            JsonArray jsonArray = [];
            for (int i = 0; i < lst.Length; i++)
            {
                jsonArray.Add(lst[i].ToJson());
            }
            JsonObject obj = new(){
                { "type", "statList" },
                { "lst", jsonArray }
            };
            return obj;
        }
    }

    class PSLexer
    {
        int row = 0, col = 0;
        readonly string code;
        int pos;

        public PSLexer(string program)
        {
            code = program;
            pos = 0;
        }

        public AstToken GetToken()
        {
            if (pos >= code.Length)
            {
                return new AstToken("\0", (int)TokenType.EOF, row, col);
            }
            if (code[pos] == ' ' || code[pos] == '\t')
            {
                pos++; col++;
                return GetToken();
            }
            if (code[pos] == '\n')
            {
                pos++; row++; col = 0;
                return new AstToken("\n", (int)TokenType.ENDL, row, col);
            }
            if (char.IsLetterOrDigit(code[pos]))
            {
                var sb = new StringBuilder();
                sb.Append(code[pos]); pos++; col++;
                while (pos < code.Length && char.IsLetterOrDigit(code[pos])) { sb.Append(code[pos]); pos++; col++; }
                string s = sb.ToString();
                if (IsKeyWord(s, out var tt)) return new AstToken(s, (int)tt, row, col);
                if (int.TryParse(s, out var _)) return new AstToken(s, (int)TokenType.NUMBER, row, col);
                return new AstToken(s, (int)TokenType.FIELD, row, col);
            }
            if (char.IsSymbol(code[pos]) || char.IsPunctuation(code[pos]))
            {
                var sb = new StringBuilder();
                sb.Append(code[pos]); pos++; col++;
                while (pos < code.Length && char.IsSymbol(code[pos])) { sb.Append(code[pos]); pos++; col++; }
                string s = sb.ToString();
                if (s == "*" || s == "/") throw Error("Invalid operator: " + s);
                if (s == "+" || s == "-") return new AstToken(s, (int)TokenType.OPERLV6, row, col);
                if (s == "<<" || s == ">>" || s == ">>>") return new AstToken(s, (int)TokenType.OPERLV7, row, col);
                if (s == "<" || s == ">" || s == "<=" || s == ">=") return new AstToken(s, (int)TokenType.OPERLV8, row, col);
                if (s == "==" || s == "!=") return new AstToken(s, (int)TokenType.OPERLV9, row, col);
                if (s == "&") return new AstToken(s, (int)TokenType.OPERLV10, row, col);
                if (s == "^") return new AstToken(s, (int)TokenType.OPERLV11, row, col);
                if (s == "=") return new AstToken(s, (int)TokenType.ASSIGN, row, col);
                if (s == "(") return new AstToken(s, (int)TokenType.LPAREN, row, col);
                if (s == ")") return new AstToken(s, (int)TokenType.RPAREN, row, col);
                if (s == ";") return new AstToken(s, (int)TokenType.SEMICOLON, row, col);
                if (s == ",") return new AstToken(s, (int)TokenType.COMMA, row, col);
                throw new Exception("Invalid symbol: " + s + " at " + row + ":" + col);
                // return new AstToken(s, (int)TokenType.SYMBOL, row, col);
            }
            throw Error("Invalid character: " + code[pos]);
        }
        Exception Error(string msg) => new("Error: " + msg + " at " + row + ":" + col);
        static bool IsKeyWord(string word, out TokenType tt) => Enum.TryParse<TokenType>("KEY_" + word, out tt);
    }

    public class PSParser
    {
        enum NodeType
        {
            // 128-255
            ROOT = 128,
            EXPR,
            ARGSLST,
            STAT,
        }

        delegate LRState? LRParse(in PSParser p);

        public struct LRState { public AstNode node; public int tt; }
        List<AstToken> tokens = [];
        List<LRState> statelist = [];
        // shift/reduce alogorithm
        readonly LRParse[] bnfPattern = [
            (in PSParser p) => {
                // number
                if (!p.Require(1)) return null;
                var token = p.Get(0);
                if (p.Match([(int)TokenType.NUMBER])) {
                    NodeAssert(token, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstNumber(token.node), tt = (int)NodeType.EXPR };
                }
                return null;
            },
            (in PSParser p) => {
                // field
                if (!p.Require(1)) return null;
                var token = p.Get(0);
                if (p.Match([(int)TokenType.FIELD]) && p.Peek(0).tt != (int)TokenType.LPAREN) {
                    NodeAssert(token, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstField(token.node), tt = (int)NodeType.EXPR };
                }
                return null;
            },
            (in PSParser p) => {
                // argslist: argslist , expr | expr , expr
                if (!p.Require(3)) return null;
                if (p.Match([(int)NodeType.EXPR, (int)TokenType.COMMA, (int)NodeType.EXPR])
                    || p.Match([(int)NodeType.ARGSLST, (int)TokenType.COMMA, (int)NodeType.EXPR])) {
                    var first = p.Get(0);
                    var comma = p.Get(1);
                    var second = p.Get(2);
                    p.Release();
                    return new LRState { node = new AstArgsLst(first.node, second.node), tt = (int)NodeType.ARGSLST };
                }
                return null;
            },
            (in PSParser p) => {
                // funcCall: field ( argslst ) | field ()
                if (!p.Require(3)) return null;
                if (p.Match([(int)TokenType.FIELD, (int)TokenType.LPAREN, (int)TokenType.RPAREN])) {
                    var func = p.Get(0).node as AstToken ?? throw new Exception("Unknown Object");
                    p.Release();
                    return new LRState { node = new AstFuncCall(func, []), tt = (int)NodeType.EXPR };
                }
                if (!p.Require(4)) return null;
                if (p.Match([(int)TokenType.FIELD, (int)TokenType.LPAREN, (int)NodeType.EXPR, (int)TokenType.RPAREN])) {
                    var func = p.Get(0).node as AstToken ?? throw new Exception("Unknown Object");
                    var args = p.Get(2).node;
                    p.Release();
                    return new LRState { node = new AstFuncCall(func, [args]), tt = (int)NodeType.EXPR };
                }
                if (p.Match([(int)TokenType.FIELD, (int)TokenType.LPAREN, (int)NodeType.ARGSLST, (int)TokenType.RPAREN])) {
                    var func = p.Get(0).node as AstToken ?? throw new Exception("Unknown Object");
                    var args = p.Get(2).node as AstArgsLst ?? throw new Exception("Unknown Object");
                    p.Release();
                    return new LRState { node = new AstFuncCall(func, args.lst), tt = (int)NodeType.EXPR };
                }
                return null;
            },
            (in PSParser p) => {
                // expr : ( expr )
                if (!p.Require(3)) return null;
                var lparen = p.Get(0);
                var expr = p.Get(1);
                var rparen = p.Get(2);
                if (p.Match([(int)TokenType.LPAREN, (int)NodeType.EXPR, (int)TokenType.RPAREN])) {
                    NodeAssert(lparen, [typeof(AstToken)]);
                    NodeAssert(expr, IsComputable);
                    NodeAssert(rparen, [typeof(AstToken)]);
                    if (rparen.tt != (int)TokenType.RPAREN) throw new Exception("Syntax error, it should be ')' at " + rparen.node.row + ":" + rparen.node.col);
                    p.Release();
                    return expr;
                }
                return null;
            },
            (in PSParser p) => {
                // expr : expr op expr
                if (!p.Require(3)) return null;
                if ((p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV5, (int)NodeType.EXPR])
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV6, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV6))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV7, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV7))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV8, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV8))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV9, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV9))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV10, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV10))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV11, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV11))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV12, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV5 && p.Peek(0).tt < (int)TokenType.OPERLV12)))
                    && (p.Peek(0).tt >= (int)TokenType.OPERLV0 && p.Peek(0).tt < (int)TokenType.NUM_OPERS || p.Peek(0).tt == (int)TokenType.ENDL)) {
                    var left = p.Get(0);
                    var op = p.Get(1);
                    var right = p.Get(2);
                    NodeAssert(left, IsComputable);
                    NodeAssert(op, [typeof(AstToken)]);
                    NodeAssert(right, IsComputable);
                    p.Release();
                    return new LRState { node = new AstBinOper(left.node, op.node, right.node), tt = (int)NodeType.EXPR };
                }
                return null;
            },
            (in PSParser p) => {
                // stat_assign : field = expr end
                if (!p.Require(4)) return null;
                if (p.Match([(int)NodeType.EXPR, (int)TokenType.ASSIGN, (int)NodeType.EXPR, (int)TokenType.ENDL]))
                {
                    var dst = p.Get(0);
                    var assign = p.Get(1);
                    var src = p.Get(2);
                    var end = p.Get(3);
                    NodeAssert(dst, [typeof(AstField)]);
                    NodeAssert(assign, [typeof(AstToken)]);
                    NodeAssert(src, IsComputable);
                    NodeAssert(end, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstAssign(dst.node, src.node), tt = (int)NodeType.STAT };
                }
                return null;
            },
            (in PSParser p) => {
                // stat_if : if condition end body endif end
                if (!p.Require(6)) return null;
                if (p.Match([(int)TokenType.KEY_if, (int)NodeType.EXPR, (int)TokenType.ENDL, (int)NodeType.STAT, (int)TokenType.KEY_end, (int)TokenType.ENDL]))
                {
                    var _if = p.Get(0);
                    var condi = p.Get(1);
                    var end0 = p.Get(2);
                    var body = p.Get(3);
                    var endif = p.Get(4);
                    var end1 = p.Get(5);
                    NodeAssert(_if, [typeof(AstToken)]);
                    NodeAssert(condi, IsComputable);
                    NodeAssert(end0, [typeof(AstToken)]);
                    NodeAssert(body, IsState);
                    NodeAssert(endif, [typeof(AstToken)]);
                    NodeAssert(end1, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstIf(condi.node, body.node, null), tt = (int)NodeType.STAT };
                }
                return null;
            },
            (in PSParser p) => {
                // stat_while : while expr end body endwhile end
                if (!p.Require(6)) return null;
                if (p.Match([(int)TokenType.KEY_while, (int)NodeType.EXPR, (int)TokenType.ENDL, (int)NodeType.STAT, (int)TokenType.KEY_end, (int)TokenType.ENDL]))
                {
                    var _while = p.Get(0);
                    var condi = p.Get(1);
                    var end0 = p.Get(2);
                    var body = p.Get(3);
                    var endif = p.Get(4);
                    var end1 = p.Get(5);
                    NodeAssert(_while, [typeof(AstToken)]);
                    NodeAssert(condi, IsComputable);
                    NodeAssert(end0, [typeof(AstToken)]);
                    NodeAssert(body, IsState);
                    NodeAssert(endif, [typeof(AstToken)]);
                    NodeAssert(end1, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstWhile(condi.node, body.node), tt = (int)NodeType.STAT };
                }
                return null;
            },
            (in PSParser p) => {
                // stat : stat stat
                if (!p.Require(2)) return null;
                var first = p.Get(0);
                var second = p.Get(1);
                if (p.Match([(int)NodeType.STAT, (int)NodeType.STAT]))
                {
                    NodeAssert(first, IsState);
                    NodeAssert(second, IsState);
                    p.Release();
                    return new LRState { node = new AstStatList(first.node, second.node), tt = (int)NodeType.STAT };
                }
                return null;
            },
        ];

        int required = 0;

        public AstNode Parse(string program)
        {
            PSLexer lexer = new(program);
            while (true)
            {
                var token = lexer.GetToken();
                if (token.tt == (int)TokenType.EOF)
                {
                    tokens.Add(new AstToken("\0", (int)TokenType.ENDL, token.row, token.col));
                    break;
                }
                tokens.Add(token);
            }

            do
            {
                if (tokens.Count == 0)
                {
                    break;
                }

                var state = new LRState
                {
                    node = Peek(0),
                    tt = Peek(0).tt,
                };
                tokens.RemoveAt(0);
                statelist.Insert(0, state);

                while (true)
                {
                    bool found = false;
                    for (int i = 0; i < bnfPattern.Length; i++)
                    {
                        var result = bnfPattern[i](this);
                        if (result != null)
                        {
                            found = true;
                            statelist.Insert(0, result.Value);
                        }
                    }
                    if (!found) break;
                }

            } while (statelist.Count > 0);
            while (statelist.Count > 0 && statelist.First().node.tt == (int)TokenType.ENDL) statelist.RemoveAt(0);
            while (statelist.Count > 0 && statelist.Last().node.tt == (int)TokenType.ENDL) statelist.RemoveAt(statelist.Count - 1);
            if (statelist.Count != 1)
            {
                for (int i = statelist.Count - 1; i >= 0; i--)
                {
                    Console.WriteLine(statelist[i].node.ToJson());
                }
                var enode = statelist.First();
                throw new Exception("Synatx error at " + enode.node.row + ":" + enode.node.col);
            }
            return statelist.First().node;
        }

        bool Require(int n)
        {
            required = n;
            return statelist.Count >= n;
        }
        // get the i's element from the stack top 
        bool Match(int[] types)
        {
            // getEnumerator from last 4
            var rit = statelist.GetEnumerator();
            for (int i = types.Length - 1; i >= 0; i--)
            {
                rit.MoveNext();
                if (types[i] != rit.Current.tt) return false;
            }
            return true;
        }

        LRState Get(int i) => statelist[required - 1 - i];
        void Release() => statelist.RemoveRange(0, required);
        //  peek token
        AstToken Peek(int i) => tokens[i];

        static void NodeAssert(in LRState state, in Type[] types)
        {
            if (state.node == null) throw new Exception("Node is null");
            foreach (var t in types)
            {
                if (state.node.GetType() == t) return;
            }
            string[] shouldbe = new string[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                shouldbe[i] += types[i].Name;
            }
            throw new Exception("State Should be " + string.Join(" or ", shouldbe) + "\nbut it is " + state.node.GetType().Name + " at " + state.node.row + ":" + state.node.col);
        }

        static readonly Type[] IsComputable = [typeof(AstBinOper), typeof(AstNumber), typeof(AstField), typeof(AstFuncCall)];
        static readonly Type[] IsState = [typeof(AstAssign), typeof(AstIf), typeof(AstWhile), typeof(AstStatList)];
    }

    public class PSCompiler
    {
        PSParser psParser = new();
        public AstNode CompileToAst(string program)
        {
            return psParser.Parse(program);
        }

        public string CompileToAsm(AstNode tree)
        {
            Visit(tree);
            var it = instQueue.GetEnumerator();
            while (it.MoveNext())
            {
                it.Current.GenerateCode(this);
            }
            return string.Join("\n", codeStack);
        }

        public string Compile(string program)
        {
            AstNode tree = CompileToAst(program);
            return CompileToAsm(tree);
        }

        readonly Dictionary<string, IValue> parseVar = [];
        readonly Stack<IValue> varStack = [];
        readonly Queue<IValue> instQueue = [];
        void Visit(in AstNode node)
        {
            var astName = node.GetType().Name;
            Type type = typeof(PSCompiler);
            MethodInfo method = type.GetMethod("Visit_" + astName, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new Exception("No visit method for " + astName);
            method.Invoke(this, [node]);
        }

        void Visit_AstNumber(in AstNumber node)
        {
            varStack.Push(new ValueNumber(node.value));
        }

        void Visit_AstField(in AstField node)
        {
            if (parseVar.TryGetValue(node.field, out IValue? value))
            {
                varStack.Push(value);
            }
            else throw new Exception("Undefined variable: " + node.field + " at" + node.row + ":" + node.col);
        }

        void Visit_AstFuncCall(in AstFuncCall node)
        {
            if (node.name == "read")
            {
                if (node.args.Length != 1) throw new Exception("read() should have 1 argument");
                if (node.args[0] is not AstNumber num) throw new Exception("read() should have const argument");
                var instIn = new InstIn(num.value.ToString());
                varStack.Push(instIn);
                instQueue.Enqueue(instIn);
                return;
            }
            throw new Exception("Undefined function: " + node.name + " at" + node.row + ":" + node.col);
        }

        void Visit_AstBinOper(in AstBinOper node)
        {
            Visit(node.left);
            var left = varStack.Pop();
            Visit(node.right);
            var right = varStack.Pop();
            var inst = new InstC(node.op, left, right);
            varStack.Push(inst);
            instQueue.Enqueue(inst);
        }

        void Visit_AstAssign(in AstAssign node)
        {
            if (node.dst is not AstField field) throw new Exception("Should be AstField");
            Visit(node.src);
            var src = varStack.Peek();

            if (parseVar.TryGetValue(field.field, out IValue? value))
            {
                var inst = new InstMv(value, src);
                instQueue.Enqueue(inst);
            }
            else
            {
                var inst = new InstVar(src);
                parseVar.Add(field.field, inst);
                instQueue.Enqueue(inst);
            }
        }

        void Visit_AstStatList(in AstStatList node)
        {
            foreach (var stat in node.lst)
            {
                Visit(stat);
            }
        }

        void Visit_AstIf(AstIf node)
        {
            Visit(node.condition);
            var condition = varStack.Pop();
            var labelElse = new InstLabel(AllocLabel("ifend"));
            var instbeq0 = new InstBeq0(condition, labelElse);
            instQueue.Enqueue(instbeq0);
            Visit(node.body);
            instQueue.Enqueue(labelElse);
        }

        void Visit_AstWhile(AstWhile node)
        {
            var labelLoop = new InstLabel(AllocLabel("loop"));
            var labelbr = new InstLabel(AllocLabel("condi"));
            var instJ = new InstJ(labelbr);
            instQueue.Enqueue(instJ);
            instQueue.Enqueue(labelLoop);
            Visit(node.body);
            instQueue.Enqueue(labelbr);
            Visit(node.condition);
            var condition = varStack.Pop();
            var instbne0 = new InstBne0(condition, labelLoop);
            instQueue.Enqueue(instbne0);
        }

        int labelCount = 0;
        int dstCount = -1;
        int tempCount = 0;
        // readonly Stack<string> valueStack = [];
        readonly Queue<string> codeStack = [];
        void PushCode(in string code) => codeStack.Enqueue(code);

        string AllocDst()
        {
            dstCount++;
            tempCount++;
            return $"({dstCount})";
        }

        string AllocTemp()
        {
            tempCount++;
            return $"({tempCount})";
        }

        string AllocLabel(in string suffix = "")
        {
            labelCount++;
            return $"L{labelCount}_" + suffix;
        }

        interface IValue
        {
            string GetDst(in PSCompiler compiler);
            void GenerateCode(in PSCompiler compiler);
        }

        class ValueNumber(in int value) : IValue
        {
            public int value = value;
            public string GetDst(in PSCompiler compiler)
            {
                return value.ToString();
            }
            public void GenerateCode(in PSCompiler compiler) { }
        }

        class InstC(in string op, in IValue src1, in IValue src2) : IValue
        {
            public string op = op;
            public IValue src1 = src1, src2 = src2;
            string? dst;
            public string GetDst(in PSCompiler compiler)
            {
                dst ??= compiler.AllocTemp();
                return dst;
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var src1Value = src1.GetDst(compiler);
                var src2Value = src2.GetDst(compiler);
                var dstValue = GetDst(compiler);
                var inst = $"c{op} {dstValue} {src1Value} {src2Value}";
                compiler.PushCode(inst);
            }
        }

        class InstIn(in string id) : IValue
        {
            public string id = id;
            string? dst;
            public string GetDst(in PSCompiler compiler)
            {
                dst ??= compiler.AllocDst();
                return dst;
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var dstValue = GetDst(compiler);
                var inst = $"in {dstValue} {id}";
                compiler.PushCode(inst);
            }
        }

        // define new variable
        class InstVar(in IValue src) : IValue
        {
            public IValue src = src;
            string? dst;
            public string GetDst(in PSCompiler compiler)
            {
                dst ??= compiler.AllocDst();
                return dst;
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var srcValue = src.GetDst(compiler);
                var dstValue = GetDst(compiler);
                var inst = $"mv {dstValue} {srcValue}";
                compiler.PushCode(inst);
                compiler.tempCount = compiler.dstCount;
            }
        }

        class InstMv(in IValue dst, in IValue src) : IValue
        {
            public IValue dst = dst;
            public IValue src = src;
            public string GetDst(in PSCompiler compiler)
            {
                throw new Exception("Should not be called");
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var srcValue = src.GetDst(compiler);
                var dstValue = dst.GetDst(compiler);
                var inst = $"mv {dstValue} {srcValue}";
                compiler.PushCode(inst);
                compiler.tempCount = compiler.dstCount;
            }
        }

        class InstBeq0(in IValue src, in IValue label) : IValue
        {
            public IValue src = src;
            public IValue label = label;
            public string GetDst(in PSCompiler compiler)
            {
                throw new Exception("Should not be called");
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var srcValue = src.GetDst(compiler);
                var lable = label.GetDst(compiler);
                var inst = $"b== {srcValue} 0 {lable}";
                compiler.PushCode(inst);
            }
        }

        class InstBne0(in IValue src, in IValue label) : IValue
        {
            public IValue src = src;
            public IValue label = label;
            public string GetDst(in PSCompiler compiler)
            {
                throw new Exception("Should not be called");
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var srcValue = src.GetDst(compiler);
                var lable = label.GetDst(compiler);
                var inst = $"b!= {srcValue} 0 {lable}";
                compiler.PushCode(inst);
            }
        }

        class InstJ(in IValue label) : IValue
        {
            public IValue label = label;
            public string GetDst(in PSCompiler compiler)
            {
                throw new Exception("Should not be called");
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                var lable = label.GetDst(compiler);
                var inst = $"j {lable}";
                compiler.PushCode(inst);
            }
        }

        class InstLabel(in string label) : IValue
        {
            public string label = label;
            public string GetDst(in PSCompiler compiler)
            {
                return label;
            }
            public void GenerateCode(in PSCompiler compiler)
            {
                compiler.PushCode($"{label}:");
            }
        }
    }
}