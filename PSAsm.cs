using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;

using RegVal = int;


namespace PSASM
{
    /*
    ops:
    imm: 1 2 3
    regid: x0-x7 or ra,sp,s0-s5
    mem: [imm] or [regid] or [[[..]]]

    instructions:
    c[op] dst src1 src2     : dst = src1 op src2
    mv dst src1             : dst = src1
    push src1 src2 ... srcn : push src1, src2, ..., srcn to stack, sp -= n, srcn is on stack head
    pop dst1 dst2 ... dstn  : from stack pop to dstn, ..., dst2, dst1, sp +=n
    b[op] src1 src2 lable   : if src1 op src2 then jump lable
    j lable                 : jump lable
    apc dst offset          : dst = pc + offset
    in dst io (shift)         : dst = dst | (io << shift), shift is optional default is 0, io: 0, 1...
    out io src1 (shift)       : out = (src1 >> shift), , shift is optional default is 0
    sync                    : sync io
    */

    public class PSASMContext
    {
        readonly AsmParser asmParser = new();
        const int MaxInsts = 128;
        int numInsts;
        readonly IAsmInst[] rom;
        public RegVal pc;
        public RegVal[] rf;
        public RegVal[] ram;

        public RegVal input;
        public RegVal output;

        public bool finished = false;

        public delegate void IOSync(ref RegVal input, RegVal output);
        public bool sync = false;
        public IOSync? onSync;

        public PSASMContext()
        {
            rom = new IAsmInst[MaxInsts + 20];
            rf = new RegVal[16];
            ram = new RegVal[256];

            Reset();
        }

        public void Reset()
        {
            numInsts = 0;
            pc = 0;
            for (int i = 0; i < (int)AsmParser.RegId.NumRegs; i++) rf[i] = 0;
            rf[(RegVal)AsmParser.RegId.sp] = ram.Length - 1;
            input = 0;
            output = 0;
            finished = false;
            sync = false;
        }

        public void Programming(string program)
        {
            string[] lines = program.Split('\n');
            asmParser.RemoveComments(ref lines);
            asmParser.ScanLable(lines);
            foreach (string line in lines)
            {
                if (line.Length == 0 || line.EndsWith(":")) continue;
                IAsmInst inst = asmParser.ParseInst(line.ToLower());
                PushInst(inst);
            }
            PlaceHolder();
        }

        public bool Steps(int steps)
        {
            for (int i = 0; i < steps && !(finished || sync); i++)
            {
                rom[pc].Execute(this); ++pc;
            }
            if (sync) { sync = false; onSync?.Invoke(ref input, output); }
            return !finished;
        }

        public bool Step() // continue if true
        {
            rom[pc].Execute(this); ++pc;
            if (sync) { sync = false; onSync?.Invoke(ref input, output); }
            return !finished;
        }

        void PushInst(IAsmInst inst)
        {
            if (numInsts >= MaxInsts) throw new Exception("Too many instructions");
            rom[numInsts] = inst;
            numInsts++;
        }

        void PlaceHolder()
        {
            rom[numInsts] = new AsmInstEnd();
            numInsts++;
            for (int i = 0; i < 20; i++)
            {
                rom[numInsts] = new AsmInstNop();
                numInsts++;
            }
        }
    }

    class AsmParser
    {
        public enum RegId { ra, sp, s0, s1, s2, s3, s4, s5, NumRegs };

        readonly static Dictionary<string, int> regidmap = new(){
        // x0-x7   :  alias
        {"x0" , 0 }, {"ra", 0},
        {"x1" , 1 }, {"sp", 1},
        {"x2" , 2 }, {"s0", 2},
        {"x3" , 3 }, {"s1", 3},
        {"x4" , 4 }, {"s2", 4},
        {"x5" , 5 }, {"s3", 5},
        {"x6" , 6 }, {"s4", 6},
        {"x7" , 7 }, {"s5", 7},
    };

        readonly static Regex regexImm = new(@"^(0x)?(-?[0-9]+)");
        readonly static Regex regexMem = new(@"^\[(.*)\]");

        #region
        readonly static Dictionary<string, ALUInstFactory> alInst = new(){
        {"+" , (dst, src1, src2) => (dst, src1, src2) switch
        {
            (RegOpParam p1, RegOpParam p2, ImmOpParam p3) => new AsmInstAddOptRRI(p1.regid, p2.regid, p3.imm),
            (RegOpParam p1, ImmOpParam p3, RegOpParam p2) => new AsmInstAddOptRRI(p1.regid, p2.regid, p3.imm),
            _ => new AsmInstAdd(dst, src1, src2)
        }},
        {"-", (dst, src1, src2) => new AsmInstSub(dst, src1, src2)},
        {"&", (dst, src1, src2) => new AsmInstAnd(dst, src1, src2)},
        {"|", (dst, src1, src2) => new AsmInstOr(dst, src1, src2)},
        {"^", (dst, src1, src2) => new AsmInstXor(dst, src1, src2)},
        {"<<", (dst, src1, src2) => new AsmInstSll(dst, src1, src2)},
        {">>>", (dst, src1, src2) => new AsmInstSrl(dst, src1, src2)},
        {">>", (dst, src1, src2) => new AsmInstSra(dst, src1, src2)},
        {"==", (dst, src1, src2) => new AsmInstEq(dst, src1, src2)},
        {"!=", (dst, src1, src2) => new AsmInstNe(dst, src1, src2)},
        {"<", (dst, src1, src2) => new AsmInstLt(dst, src1, src2)},
        {">=", (dst, src1, src2) => new AsmInstGte(dst, src1, src2)},
        {">", (dst, src1, src2) => new AsmInstLt(dst, src2, src1)},
        {"<=", (dst, src1, src2) => new AsmInstGte(dst, src2, src1)},
    };
        readonly static Dictionary<string, BRUInstFactory> brInst = new(){
        {"==" , (src1, src2, target) => new AsmInstBeq(src1, src2, target)},
        {"!=" , (src1, src2, target) => new AsmInstBne(src1, src2, target)},
        {"<" , (src1, src2, target) => new AsmInstBlt(src1, src2, target)},
        {">=" , (src1, src2, target) => new AsmInstBgte(src1, src2, target)},
        {">" , (src1, src2, target) => new AsmInstBlt(src2, src1, target)},
        {"<=" , (src1, src2, target) => new AsmInstBgte(src2, src1, target)},
    };
        readonly static Dictionary<string, BRUInstFactory> brInstOptRR = new(){
        {"==" , (src1, src2, target) => new AsmInstBeqOptRR(((RegOpParam)src1).regid, ((RegOpParam)src2).regid, target)},
        {"!=" , (src1, src2, target) => new AsmInstBneOptRR(((RegOpParam)src1).regid, ((RegOpParam)src2).regid, target)},
        {"<" , (src1, src2, target) => new AsmInstBltOptRR(((RegOpParam)src1).regid, ((RegOpParam)src2).regid, target)},
        {">=" , (src1, src2, target) => new AsmInstBgteOptRR(((RegOpParam)src1).regid, ((RegOpParam)src2).regid, target)},
        {">" , (src1, src2, target) => new AsmInstBltOptRR(((RegOpParam)src2).regid, ((RegOpParam)src1).regid, target)},
        {"<=" , (src1, src2, target) => new AsmInstBgteOptRR(((RegOpParam)src2).regid, ((RegOpParam)src1).regid, target)},
    };
        readonly static Dictionary<string, BRUInstFactory> brInstOptRI = new(){
        {"==" , (src1, src2, target) => new AsmInstBeqOptRI(((RegOpParam)src1).regid, ((ImmOpParam)src2).imm, target)},
        {"!=" , (src1, src2, target) => new AsmInstBneOptRI(((RegOpParam)src1).regid, ((ImmOpParam)src2).imm, target)},
        {"<" , (src1, src2, target) => new AsmInstBltOptRI(((RegOpParam)src1).regid, ((ImmOpParam)src2).imm, target)},
        {">=" , (src1, src2, target) => new AsmInstBgteOptRI(((RegOpParam)src1).regid, ((ImmOpParam)src2).imm, target)},
        {">" , (src1, src2, target) => new AsmInstBltOptRI(((RegOpParam)src2).regid, ((ImmOpParam)src1).imm, target)},
        {"<=" , (src1, src2, target) => new AsmInstBgteOptRI(((RegOpParam)src2).regid, ((ImmOpParam)src1).imm, target)},
    };

        #endregion

        // label point to the next instruction
        readonly Dictionary<string, int> labelTable = [];

        static IOpParam ParseOpParam(in string token)
        {
            MatchCollection match;
            match = regexImm.Matches(token);
            if (match.Count > 0) // imm op
            {
                int imm = 0;
                if (match[0].Groups[1].Value == "0x") imm = int.Parse(match[0].Groups[2].Value, System.Globalization.NumberStyles.HexNumber);
                else imm = int.Parse(match[0].Groups[0].Value);
                return new ImmOpParam(imm);
            }
            match = regexMem.Matches(token);
            if (match.Count > 0) // mem op
            {
                string addr = match[0].Groups[1].Value;
                IOpParam addrOp = ParseOpParam(addr);
                return new MemOpParam(addrOp);
            }
            if (regidmap.TryGetValue(token, out int value)) // regop
            {
                int regid = value;
                return new RegOpParam(regid);
            }
            throw new Exception("Invalid operand:" + token);
        }

        public IAsmInst ParseInst(in string inst)
        {
            string[] token = inst.Split(' ');
            string name = token[0];
            if (name.StartsWith("c"))
            {
                if (token.Length != 4) throw new Exception("Invalid cal instruction");
                string op = name.Substring(1);
                if (!alInst.TryGetValue(op, out ALUInstFactory? factory)) throw new Exception("Invalid ALU operation:" + op);
                return factory(ParseOpParam(token[1]), ParseOpParam(token[2]), ParseOpParam(token[3]));
            }
            else if (name.StartsWith("mv"))
            {
                if (token.Length != 3) throw new Exception("Invalid mv instruction");
                IOpParam dst = ParseOpParam(token[1]), src1 = ParseOpParam(token[2]);
                return (dst, src1) switch
                {
                    (RegOpParam p1, ImmOpParam p2) => new AsmInstMvOptRI(p1.regid, p2.imm),
                    _ => new AsmInstMv(dst, src1)
                };
            }
            else if (name.StartsWith("b"))
            {
                if (token.Length != 4) throw new Exception("Invalid branch instruction");
                string op = name.Substring(1);
                if (!brInst.TryGetValue(op, out BRUInstFactory? factory)) throw new Exception("Invalid BRU operation:" + op);
                if (!labelTable.TryGetValue(token[3], out int target)) throw new Exception("Undefined label:" + token[3]);
                IOpParam src1 = ParseOpParam(token[1]), src2 = ParseOpParam(token[2]);
                return (src1, src2) switch
                {
                    (RegOpParam p1, RegOpParam p2) => brInstOptRR[op](p1, p2, target),
                    (RegOpParam p1, ImmOpParam p2) => brInstOptRI[op](p1, p2, target),
                    (ImmOpParam p1, RegOpParam p2) => brInstOptRI[op](p2, p1, target),
                    _ => factory(src1, src2, target)
                };
            }
            else if (name.StartsWith("j"))
            {
                if (token.Length != 2) throw new Exception("Invalid jump instruction");
                if (regidmap.ContainsKey(token[1]))
                {
                    // indirect jump
                    return new AsmInstJr(ParseOpParam(token[1]));
                }
                // direct jump
                if (!labelTable.TryGetValue(token[1], out int target)) throw new Exception("Undefined label:" + token[1]);
                return new AsmInstJ(target);
            }
            else if (name.StartsWith("apc"))
            {
                if (token.Length != 3) throw new Exception("Invalid apc instruction");
                return new AsmInstApc(ParseOpParam(token[1]), ParseOpParam(token[2]));
            }
            else if (name.StartsWith("push"))
            {
                if (token.Length == 1) throw new Exception("Invalid push instruction");
                List<IOpParam> ops = [];
                for (int i = 1; i < token.Length; i++) ops.Add(ParseOpParam(token[i]));

                return new AsmInstPush(ops);
            }
            else if (name.StartsWith("pop"))
            {
                if (token.Length == 1) throw new Exception("Invalid pop instruction");
                List<IOpParam> ops = [];
                for (int i = token.Length - 1; i >= 1; i--) ops.Add(ParseOpParam(token[i]));
                return new AsmInstPop(ops);
            }
            else if (name.StartsWith("in"))
            {
                if (token.Length < 3) throw new Exception("Invalid in instruction");
                var port = ParseOpParam(token[2]);
                if (port is not ImmOpParam ioid) throw new Exception("Invalid port operand(it must be const) :" + token[2]);
                if (token.Length == 3) return new AsmInstIn(ParseOpParam(token[1]), new IOOpParam(ioid.imm), new ImmOpParam(0));
                return new AsmInstIn(ParseOpParam(token[1]), ParseOpParam(token[2]), ParseOpParam(token[3]));
            }
            else if (name.StartsWith("out"))
            {
                if (token.Length < 3) throw new Exception("Invalid out instruction");
                var port = ParseOpParam(token[1]);
                if (port is not ImmOpParam ioid) throw new Exception("Invalid port operand(it must be const) :" + token[1]);
                if (token.Length == 3) return new AsmInstOut(new IOOpParam(ioid.imm), ParseOpParam(token[2]), new ImmOpParam(0));
                return new AsmInstOut(ParseOpParam(token[1]), ParseOpParam(token[2]), ParseOpParam(token[3]));
            }
            else if (name.StartsWith("sync"))
            {
                if (token.Length != 1) throw new Exception("Invalid sync instruction");
                return new AsmInstSync();
            }
            throw new Exception("Invalid instruction:" + inst);
        }

        public void RemoveComments(ref string[] program)
        {
            List<string> before = [];
            for (int i = 0; i < program.Length; i++)
            {
                string t = program[i].Split(';')[0].Trim();
                if (!string.IsNullOrEmpty(t)) before.Add(t);
            }
            program = [.. before];
        }

        public void ScanLable(in string[] program)
        {
            int numInsts = 0;
            for (int i = 0; i < program.Length; i++)
            {
                ref string lable = ref program[i];
                if (lable.EndsWith(":"))
                {
                    // remove ":"
                    string label = lable.Substring(0, lable.Length - 1);
                    if (label.Contains(" ")) throw new Exception("Invalid label:" + label);
                    labelTable[label] = numInsts;
                }
                else numInsts++;
            }
        }
    }

    interface IOpParam
    {
        RegVal Get(in PSASMContext context);
        void Set(in PSASMContext context, RegVal value);
    }

    class RegOpParam(int regid) : IOpParam
    {
        public readonly int regid = regid;
        public RegVal Get(in PSASMContext context) => context.rf[regid];
        public void Set(in PSASMContext context, RegVal value) => context.rf[regid] = value;
    }

    class ImmOpParam(RegVal imm) : IOpParam
    {
        public readonly RegVal imm = imm;
        public RegVal Get(in PSASMContext context) => imm;
        public void Set(in PSASMContext context, RegVal value) { throw new Exception("Cannot set value of immediate operand"); }
    }

    class MemOpParam(IOpParam aop) : IOpParam
    {
        public readonly IOpParam aop = aop;
        public RegVal Get(in PSASMContext context)
        {
            uint addr = (uint)aop.Get(context);
            if (addr < context.ram.Length) return context.ram[addr];
            throw new Exception("Invalid memory read: " + (int)addr);
        }
        public void Set(in PSASMContext context, RegVal value)
        {
            uint addr = (uint)aop.Get(context);
            if (addr < context.ram.Length) context.ram[addr] = value;
            throw new Exception("Invalid memory write: " + (int)addr);
        }
    }

    class IOOpParam(int portid) : IOpParam
    {
        readonly int portid = portid;// no used
        public RegVal Get(in PSASMContext context) => context.input;
        public void Set(in PSASMContext context, RegVal value) => context.output = value;
    }

    interface IAsmInst
    {
        void Execute(in PSASMContext context);
    }

    class AsmInstArith(in IOpParam dst, in IOpParam src1, in IOpParam src2)
    {
        protected readonly IOpParam dst = dst, src1 = src1, src2 = src2;
    }

    class AsmInstAdd(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) + src2.Get(context));
    }

    class AsmInstAddOptRRI(int rdidx, int rs1idx, RegVal imm) : IAsmInst
    {
        readonly int rdidx = rdidx, rs1idx = rs1idx;
        readonly RegVal imm = imm;
        public void Execute(in PSASMContext context) => context.rf[rdidx] = context.rf[rs1idx] + imm;
    }

    class AsmInstSub(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) - src2.Get(context));
    }

    class AsmInstAnd(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) & src2.Get(context));
    }

    class AsmInstOr(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) | src2.Get(context));
    }

    class AsmInstXor(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) ^ src2.Get(context));
    }

    class AsmInstSll(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) << src2.Get(context));
    }

    class AsmInstSrl(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) >>> src2.Get(context));
    }

    class AsmInstSra(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) >> src2.Get(context));
    }

    class AsmInstEq(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) == src2.Get(context) ? 1 : 0);
    }

    class AsmInstNe(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) != src2.Get(context) ? 1 : 0);
    }

    class AsmInstLt(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) < src2.Get(context) ? 1 : 0);
    }

    class AsmInstGte(in IOpParam dst, in IOpParam src1, in IOpParam src2) : AsmInstArith(dst, src1, src2), IAsmInst
    {
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context) >= src2.Get(context) ? 1 : 0);
    }

    class AsmInstPush(List<IOpParam> srcs) : IAsmInst
    {
        readonly IOpParam[] srcs = [.. srcs];
        public void Execute(in PSASMContext context)
        {
            ref int sp = ref context.rf[(RegVal)AsmParser.RegId.sp];
            for (int i = 0; i < srcs.Length; i++)
            {
                if ((uint)sp < context.ram.Length)
                {
                    context.ram[sp] = srcs[i].Get(context);
                    sp--; // stack pointer decrement
                }
                else throw new Exception("Push stack overflow");
            }
        }
    }

    class AsmInstPop(List<IOpParam> dsts) : IAsmInst
    {
        readonly IOpParam[] dsts = [.. dsts];//NOTE: must be reverse
        public void Execute(in PSASMContext context)
        {
            ref int sp = ref context.rf[(RegVal)AsmParser.RegId.sp];
            for (int i = 0; i < dsts.Length; i++)
            {
                sp++;
                if ((uint)sp < context.ram.Length)
                {
                    dsts[i].Set(context, context.ram[sp]);
                }
                else throw new Exception("Pop stack underflow");
            }
        }
    }

    class AsmInstMv(in IOpParam dst, in IOpParam src1) : IAsmInst
    {
        readonly IOpParam dst = dst, src1 = src1;
        public void Execute(in PSASMContext context) => dst.Set(context, src1.Get(context));
    }

    class AsmInstMvOptRI(int rdidx, RegVal imm) : IAsmInst
    {
        readonly int rdidx = rdidx;
        readonly RegVal imm = imm;
        public void Execute(in PSASMContext context) => context.rf[rdidx] = imm; // omit twice virtual call
    }

    class AsmInstApc(IOpParam dst, IOpParam offset) : IAsmInst
    {
        readonly IOpParam dst = dst;
        readonly IOpParam offste = offset;
        public void Execute(in PSASMContext context) => dst.Set(context, context.pc + offste.Get(context));
    }

    class AsmInstBJ(int target) { protected readonly int target = target - 1; }

    class AsmInstJr(IOpParam src1) : IAsmInst
    {
        readonly IOpParam src1 = src1;
        public void Execute(in PSASMContext context) => context.pc = src1.Get(context) - 1;
    }

    class AsmInstJ(int target) : AsmInstBJ(target), IAsmInst
    {
        public void Execute(in PSASMContext context) => context.pc = target;
    }

    class AsmInstBr(in IOpParam src1, in IOpParam src2, int target) : AsmInstBJ(target)
    {
        protected readonly IOpParam src1 = src1, src2 = src2;
    }

    class AsmInstBrOptRR(int rs1idx, int rs2idx, int target) : AsmInstBJ(target)
    {
        protected readonly int rs1idx = rs1idx, rs2idx = rs2idx;
    }

    class AsmInstBrOptRI(int rs1idx, RegVal imm, int target) : AsmInstBJ(target)
    {
        protected readonly int rs1idx = rs1idx;
        protected readonly RegVal imm = imm;
    }

    class AsmInstBeq(in IOpParam src1, in IOpParam src2, int target) : AsmInstBr(src1, src2, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (src1.Get(context) == src2.Get(context)) context.pc = target; }
    }

    class AsmInstBne(in IOpParam src1, in IOpParam src2, int target) : AsmInstBr(src1, src2, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (src1.Get(context) != src2.Get(context)) context.pc = target; }
    }

    class AsmInstBlt(in IOpParam src1, in IOpParam src2, int target) : AsmInstBr(src1, src2, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (src1.Get(context) < src2.Get(context)) context.pc = target; }
    }

    class AsmInstBgte(in IOpParam src1, in IOpParam src2, int target) : AsmInstBr(src1, src2, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (src1.Get(context) >= src2.Get(context)) context.pc = target; }
    }

    class AsmInstBeqOptRR(int rs1idx, int rs2idx, int target) : AsmInstBrOptRR(rs1idx, rs2idx, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] == context.rf[rs2idx]) context.pc = target; }
    }

    class AsmInstBneOptRR(int rs1idx, int rs2idx, int target) : AsmInstBrOptRR(rs1idx, rs2idx, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] != context.rf[rs2idx]) context.pc = target; }
    }

    class AsmInstBltOptRR(int rs1idx, int rs2idx, int target) : AsmInstBrOptRR(rs1idx, rs2idx, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] < context.rf[rs2idx]) context.pc = target; }
    }

    class AsmInstBgteOptRR(int rs1idx, int rs2idx, int target) : AsmInstBrOptRR(rs1idx, rs2idx, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] >= context.rf[rs2idx]) context.pc = target; }
    }

    class AsmInstBeqOptRI(int rs1idx, RegVal imm, int target) : AsmInstBrOptRI(rs1idx, imm, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] == imm) context.pc = target; }
    }

    class AsmInstBneOptRI(int rs1idx, RegVal imm, int target) : AsmInstBrOptRI(rs1idx, imm, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] != imm) context.pc = target; }
    }

    class AsmInstBltOptRI(int rs1idx, RegVal imm, int target) : AsmInstBrOptRI(rs1idx, imm, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] < imm) context.pc = target; }
    }

    class AsmInstBgteOptRI(int rs1idx, RegVal imm, int target) : AsmInstBrOptRI(rs1idx, imm, target), IAsmInst
    {
        public void Execute(in PSASMContext context) { if (context.rf[rs1idx] >= imm) context.pc = target; }
    }

    class AsmInstIn(in IOpParam dst, in IOpParam io, in IOpParam offset) : IAsmInst
    {
        protected readonly IOpParam dst = dst, io = io, offset = offset;
        public void Execute(in PSASMContext context) => dst.Set(context, dst.Get(context) | (io.Get(context) << offset.Get(context)));
    }

    class AsmInstOut(in IOpParam io, in IOpParam src1, in IOpParam offset) : IAsmInst
    {
        protected readonly IOpParam io = io, src1 = src1, offset = offset;
        public void Execute(in PSASMContext context) => io.Set(context, src1.Get(context) >> offset.Get(context));
    }

    class AsmInstSync : IAsmInst
    {
        public void Execute(in PSASMContext context) { context.sync = true; }
    }

    class AsmInstNop : IAsmInst { public void Execute(in PSASMContext context) { } }

    class AsmInstEnd : IAsmInst
    {
        public void Execute(in PSASMContext context) => context.finished = true;
    }

    delegate IAsmInst ALUInstFactory(IOpParam dst, IOpParam src1, IOpParam src2);
    delegate IAsmInst BRUInstFactory(IOpParam src1, IOpParam src2, int target);

}