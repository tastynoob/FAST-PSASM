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
        KEY_endif,
        KEY_else,
        KEY_for,
        OPERLV0, // * /
        OPERLV1, // + -
        OPERLV2, // << >> >>>
        OPERLV3, // < > <= >= == !=
        NUM_OPERS,
        ASSIGN, // =
        LPAREN, // (
        RPAREN, // )
        SYMBOL,
        NUMBER,
        FIELD,
        END,
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
    class AstEndIf(in AstNode node) : AstNode(node.tt, node.row, node.col) { }
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
                return new AstToken("\n", (int)TokenType.END, row, col);
            }
            if (char.IsLetterOrDigit(code[pos]))
            {
                var sb = new StringBuilder();
                sb.Append(code[pos]); pos++; col++;
                while (pos < code.Length && char.IsLetterOrDigit(code[pos])) { sb.Append(code[pos]); pos++; col++; }
                string s = sb.ToString();
                if (IsKeyWord(s, out var tt)) return new AstToken(s, (int)tt, row, col);
                if (int.TryParse(s, out var temp)) return new AstToken(s, (int)TokenType.NUMBER, row, col);
                return new AstToken(s, (int)TokenType.FIELD, row, col);
            }
            if (char.IsSymbol(code[pos]) || char.IsPunctuation(code[pos]))
            {
                var sb = new StringBuilder();
                sb.Append(code[pos]); pos++; col++;
                while (pos < code.Length && char.IsSymbol(code[pos])) { sb.Append(code[pos]); pos++; col++; }
                string s = sb.ToString();
                if (s == "*" || s == "/") throw Error("Invalid operator: " + s);
                if (s == "+" || s == "-") return new AstToken(s, (int)TokenType.OPERLV1, row, col);
                if (s == "<<" || s == ">>" || s == ">>>") return new AstToken(s, (int)TokenType.OPERLV2, row, col);
                if (s == "<" || s == ">" || s == "<=" || s == ">=" || s == "==" || s == "!=") return new AstToken(s, (int)TokenType.OPERLV3, row, col);
                if (s == "=") return new AstToken(s, (int)TokenType.ASSIGN, row, col);
                if (s == "(") return new AstToken(s, (int)TokenType.LPAREN, row, col);
                if (s == ")") return new AstToken(s, (int)TokenType.RPAREN, row, col);
                return new AstToken(s, (int)TokenType.SYMBOL, row, col);
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
                if (p.Match([(int)TokenType.FIELD])) {
                    NodeAssert(token, [typeof(AstToken)]);
                    p.Release();
                    return new LRState { node = new AstField(token.node), tt = (int)NodeType.EXPR };
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
                var left = p.Get(0);
                var op = p.Get(1);
                var right = p.Get(2);
                if (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV0, (int)NodeType.EXPR])
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV1, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV0 && p.Peek(0).tt < (int)TokenType.OPERLV1))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV2, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV0 && p.Peek(0).tt < (int)TokenType.OPERLV2))
                    || (p.Match([(int)NodeType.EXPR, (int)TokenType.OPERLV3, (int)NodeType.EXPR]) && !(p.Peek(0).tt >= (int)TokenType.OPERLV0 && p.Peek(0).tt < (int)TokenType.OPERLV3))) {
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
                var dst = p.Get(0);
                var assign = p.Get(1);
                var src = p.Get(2);
                var end = p.Get(3);
                if (p.Match([(int)NodeType.EXPR, (int)TokenType.ASSIGN, (int)NodeType.EXPR, (int)TokenType.END]))
                {
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
                var _if = p.Get(0);
                var condi = p.Get(1);
                var end0 = p.Get(2);
                var body = p.Get(3);
                var endif = p.Get(4);
                var end1 = p.Get(5);
                if (p.Match([(int)TokenType.KEY_if, (int)NodeType.EXPR, (int)TokenType.END, (int)NodeType.STAT, (int)TokenType.KEY_endif, (int)TokenType.END]))
                {
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
                    tokens.Add(new AstToken("\0", (int)TokenType.END, token.row, token.col));
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
            while (statelist.Count > 0 && statelist.First().node.tt == (int)TokenType.END) statelist.RemoveAt(0);
            while (statelist.Count > 0 && statelist.Last().node.tt == (int)TokenType.END) statelist.RemoveAt(statelist.Count - 1);
            if (statelist.Count != 1)
            {
                for (int i = 0; i < statelist.Count; i++)
                {
                    Console.WriteLine(statelist[i].node.ToJson());
                }
                throw new Exception("Syntax error because unable to parse the program");
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

        static readonly Type[] IsComputable = [typeof(AstBinOper), typeof(AstNumber), typeof(AstField)];
        static readonly Type[] IsState = [typeof(AstAssign), typeof(AstIf), typeof(AstStatList)];
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
            var labelElse = new InstLabel(AllocLabel());
            var instbeq0 = new InstBeq0(condition, labelElse);
            instQueue.Enqueue(instbeq0);
            Visit(node.body);
            instQueue.Enqueue(labelElse);
        }

        int labelCount = 0;
        int dstCount = -1;
        // readonly Stack<string> valueStack = [];
        readonly Queue<string> codeStack = [];
        // void PushValue(in string value) => valueStack.Push(value);
        // string PopValue() => valueStack.Pop();
        void PushCode(in string code) => codeStack.Enqueue(code);

        string AllocDst()
        {
            dstCount++;
            return $"[{dstCount}]";
        }

        string AllocLabel()
        {
            labelCount++;
            return $"L{labelCount}";
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
                dst ??= compiler.AllocDst();
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