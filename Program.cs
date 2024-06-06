using System.Text.RegularExpressions;
using System.Diagnostics;

using RegVal = int;
/*

ops:
imm: 1 2 3
regid: x0-x7 or ra,sp,s0-s5
mem: [imm] or [regid] or [[[..]]]

instructions:

cal[op] rd rs1 rs2 : rd = rs1 op rs2
mv rd rs1 : rd = rs1
push rs1 rs2 ... rsn : push rs1, rs2, ..., rsn to stack, sp -= n, rsn is on stack head
pop rd1 rd2 ... rdn : from stack pop to rdn, ..., rd2, rd1, sp +=n
b[op] rs1 rs2 lable : if rs1 op rs2 then jump lable
j lable : jump lable
apc rd offset : rd = pc + offset

*/

string testcode = @"
    ; here sp is default set to 255
    mv s3 100000000
    mv s4 0
loop:
    mv s0 1
    mv s1 2
    mv s2 3
    push s0 s1 s2
    mv s0 0
    mv s1 0 
    mv s2 0
    pop s0 s1 s2
    cal+ s4 s4 1
    b< s4 s3 loop
    apc s0 0
";

Stopwatch sw = Stopwatch.StartNew();

MyProgram myProgram = new(testcode);
myProgram.Run();
sw.Stop();
Console.WriteLine("res: " + myProgram.GetResult((int)AsmParser.RegId.s0));
Console.WriteLine("time: " + sw.Elapsed.TotalMilliseconds + "ms");



class MyProgram
{
    readonly string program;
    readonly RunTimeContext context = new();
    readonly AsmParser parser = new();

    public MyProgram(in string program)
    {
        this.program = program;
        parser.ParseProgram(context, program);
    }

    public void Run()
    {
        while (!context.finished) context.Step();
    }

    public int GetResult(int regid) => context.rf[regid];
}

partial class AsmParser
{
    public enum RegId { ra, sp, s0, s1, s2, s3, s4, s5 };

    readonly static Dictionary<string, int> Regid = new(){
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

    [GeneratedRegex(@"^(0x)?([0-9]+)")]
    private static partial Regex ImmPattern();
    [GeneratedRegex(@"^\[(.*)\]")]
    private static partial Regex MemPattern();

    readonly static Regex regexImm = ImmPattern();
    readonly static Regex regexMem = MemPattern();
    readonly static Dictionary<string, ALUInstFactory> alInst = new(){
        {"+" , (rd, rs1, rs2) => new AsmInstAdd(rd, rs1, rs2)},
        {"-", (rd, rs1, rs2) => new AsmInstSub(rd, rs1, rs2)},
        {"&", (rd, rs1, rs2) => new AsmInstAnd(rd, rs1, rs2)},
        {"|", (rd, rs1, rs2) => new AsmInstOr(rd, rs1, rs2)},
        {"^", (rd, rs1, rs2) => new AsmInstXor(rd, rs1, rs2)},
        {"<<", (rd, rs1, rs2) => new AsmInstSll(rd, rs1, rs2)},
        {">>>", (rd, rs1, rs2) => new AsmInstSrl(rd, rs1, rs2)},
        {">>", (rd, rs1, rs2) => new AsmInstSra(rd, rs1, rs2)},
        {"==", (rd, rs1, rs2) => new AsmInstEq(rd, rs1, rs2)},
        {"!=", (rd, rs1, rs2) => new AsmInstNe(rd, rs1, rs2)},
        {"<", (rd, rs1, rs2) => new AsmInstLt(rd, rs1, rs2)},
        {">", (rd, rs1, rs2) => new AsmInstGt(rd, rs1, rs2)},
        {"<=", (rd, rs1, rs2) => new AsmInstLte(rd, rs1, rs2)},
        {">=", (rd, rs1, rs2) => new AsmInstGte(rd, rs1, rs2)},

    };
    readonly static Dictionary<string, BRUInstFactory> brInst = new(){
        {"==" , (rs1, rs2, target) => new AsmInstBeq(rs1, rs2, target)},
        {"!=" , (rs1, rs2, target) => new AsmInstBne(rs1, rs2, target)},
        {"<" , (rs1, rs2, target) => new AsmInstBlt(rs1, rs2, target)},
        {">" , (rs1, rs2, target) => new AsmInstBgt(rs1, rs2, target)},
        {"<=" , (rs1, rs2, target) => new AsmInstBlte(rs1, rs2, target)},
        {">=" , (rs1, rs2, target) => new AsmInstBgte(rs1, rs2, target)},
    };

    // label point to the previous instruction
    Dictionary<string, int> labelTable = [];
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
        if (Regid.TryGetValue(token, out int value)) // regop
        {
            int regid = value;
            return new RegOpParam(regid);
        }
        throw new Exception("Invalid operand:" + token);
    }

    static void RemoveComments(ref string[] program)
    {
        List<string> before = [];
        for (int i = 0; i < program.Length; i++)
        {
            string t = program[i].Split(";")[0].Trim().ToLower();
            if (!string.IsNullOrEmpty(t)) before.Add(t);
        }
        program = [.. before];
    }

    void ScanLable(in string[] program)
    {
        int numInsts = -1;
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].EndsWith(':'))
            {
                string label = program[i][..^1];
                if (label.Contains(' ')) throw new Exception("Invalid label:" + label);
                labelTable[label] = numInsts;
            }
            else
            {
                numInsts++;
            }
        }
    }

    IAsmInst ParseInst(in string inst)
    {
        string[] token = inst.Split(" ");
        string name = token[0];
        if (name.StartsWith("mv"))
        {
            if (token.Length != 3) throw new Exception("Invalid mv instruction");
            return new AsmInstMv(ParseOpParam(token[1]), ParseOpParam(token[2]));
        }
        else if (name.StartsWith("cal"))
        {
            if (token.Length != 4) throw new Exception("Invalid cal instruction");
            string op = name[3..];
            if (!alInst.TryGetValue(op, out ALUInstFactory? factory)) throw new Exception("Invalid ALU operation:" + op);
            return factory(ParseOpParam(token[1]), ParseOpParam(token[2]), ParseOpParam(token[3]));
        }
        else if (name.StartsWith('b'))
        {
            if (token.Length != 4) throw new Exception("Invalid branch instruction");
            string op = name[1..];
            if (!brInst.TryGetValue(op, out BRUInstFactory? factory)) throw new Exception("Invalid BRU operation:" + op);
            if (!labelTable.TryGetValue(token[3], out int target)) throw new Exception("Undefined label:" + token[3]);
            return factory(ParseOpParam(token[1]), ParseOpParam(token[2]), target);
        }
        else if (name.StartsWith('j'))
        {
            if (token.Length != 2) throw new Exception("Invalid jump instruction");
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
            List<IOpParam> ops = [];
            for (int i = 1; i < token.Length; i++) ops.Add(ParseOpParam(token[i]));

            return new AsmInstPush(ops);
        }
        else if (name.StartsWith("pop"))
        {
            List<IOpParam> ops = [];
            for (int i = token.Length - 1; i >= 1; i--) ops.Add(ParseOpParam(token[i]));
            return new AsmInstPop(ops);
        }
        throw new Exception("Invalid instruction:" + inst);
    }

    public void ParseProgram(in RunTimeContext context, in string program)
    {
        string[] lines = program.Split("\n");
        RemoveComments(ref lines);
        ScanLable(lines);
        foreach (string line in lines)
        {
            if (line.Length == 0 || line.EndsWith(':')) continue;
            IAsmInst inst = ParseInst(line);
            context.PushInst(inst);
        }
        context.PushInst(new AsmInstEnd());
    }
}

class RunTimeContext
{
    const int MaxInsts = 128;
    int numInsts = 0;
    IAsmInst[] rom;

    public RegVal pc = 0;
    public RegVal[] rf;
    public RegVal[] ram;

    public bool finished = false;

    public RunTimeContext()
    {
        rom = new IAsmInst[MaxInsts + 1];
        rf = new RegVal[16];
        ram = new RegVal[256];
        rf[(RegVal)AsmParser.RegId.sp] = ram.Length - 1;
    }

    public void Step()
    {
        rom[pc].Execute(this);
        pc++;
    }

    public void Steps(int n)
    {
        for (int i = 0; i < n && !finished; i++) Step();
    }

    public void PushInst(IAsmInst inst)
    {
        if (numInsts >= MaxInsts) throw new Exception("Too many instructions");
        rom[numInsts] = inst;
        numInsts++;
    }
}

interface IOpParam
{
    RegVal Get(in RunTimeContext context);
    void Set(in RunTimeContext context, RegVal value);
}

class RegOpParam(int regid) : IOpParam
{
    readonly int regid = regid;
    public RegVal Get(in RunTimeContext context) => context.rf[regid];
    public void Set(in RunTimeContext context, RegVal value) => context.rf[regid] = value;
}

class ImmOpParam(RegVal imm) : IOpParam
{
    readonly RegVal imm = imm;
    public RegVal Get(in RunTimeContext context) => imm;
    public void Set(in RunTimeContext context, RegVal value) { throw new Exception("Cannot set value of immediate operand"); }
}

class MemOpParam(IOpParam addr) : IOpParam
{
    readonly IOpParam addr = addr;
    public RegVal Get(in RunTimeContext context) => context.ram[addr.Get(context)];
    public void Set(in RunTimeContext context, RegVal value) => context.ram[addr.Get(context)] = value;
}

interface IAsmInst
{
    void Execute(in RunTimeContext context);
}

class AsmInstArith(in IOpParam rd, in IOpParam rs1, in IOpParam rs2)
{
    protected readonly IOpParam rd = rd, rs1 = rs1, rs2 = rs2;
}

class AsmInstAdd(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) + rs2.Get(context));
}

class AsmInstSub(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) - rs2.Get(context));
}

class AsmInstAnd(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) & rs2.Get(context));
}

class AsmInstOr(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) | rs2.Get(context));
}

class AsmInstXor(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) ^ rs2.Get(context));
}

class AsmInstSll(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) << rs2.Get(context));
}

class AsmInstSrl(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) >>> rs2.Get(context));
}

class AsmInstSra(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) >> rs2.Get(context));
}

class AsmInstEq(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) == rs2.Get(context) ? 1 : 0);
}

class AsmInstNe(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) != rs2.Get(context) ? 1 : 0);
}

class AsmInstGt(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) > rs2.Get(context) ? 1 : 0);
}

class AsmInstLt(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) < rs2.Get(context) ? 1 : 0);
}

class AsmInstGte(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) >= rs2.Get(context) ? 1 : 0);
}

class AsmInstLte(in IOpParam rd, in IOpParam rs1, in IOpParam rs2) : AsmInstArith(rd, rs1, rs2), IAsmInst
{
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context) <= rs2.Get(context) ? 1 : 0);
}

class AsmInstPush(List<IOpParam> rslist) : IAsmInst
{
    readonly IOpParam[] rslist = [.. rslist];
    public void Execute(in RunTimeContext context)
    {
        for(int i=0;i<rslist.Length;i++) {
            RegVal SP = context.rf[(RegVal)AsmParser.RegId.sp];
            context.ram[SP] = rslist[i].Get(context);
            context.rf[(RegVal)AsmParser.RegId.sp]--; // stack pointer decrement
        }
    }
}

class AsmInstPop(List<IOpParam> rslist) : IAsmInst
{
    readonly IOpParam[] rslist = [.. rslist];//NOTE: must be reverse
    public void Execute(in RunTimeContext context)
    {
        for(int i=0;i<rslist.Length;i++) {
            context.rf[(RegVal)AsmParser.RegId.sp]++;
            RegVal SP = context.rf[(RegVal)AsmParser.RegId.sp];
            rslist[i].Set(context, context.ram[SP]);
        }
    }
}

class AsmInstMv(in IOpParam rd, in IOpParam rs1) : IAsmInst
{
    readonly IOpParam rd = rd, rs1 = rs1;
    public void Execute(in RunTimeContext context) => rd.Set(context, rs1.Get(context));
}

class AsmInstBr(in IOpParam rs1, in IOpParam rs2, int target)
{
    protected readonly IOpParam rs1 = rs1, rs2 = rs2;
    public int target = target;
}

class AsmInstBeq(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) == rs2.Get(context)) { context.pc = target; } }
}

class AsmInstBne(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) != rs2.Get(context)) { context.pc = target; } }
}

class AsmInstBlt(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) < rs2.Get(context)) { context.pc = target; } }
}

class AsmInstBgt(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) > rs2.Get(context)) { context.pc = target; } }
}

class AsmInstBlte(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) <= rs2.Get(context)) { context.pc = target; } }
}

class AsmInstBgte(in IOpParam rs1, in IOpParam rs2, int target) : AsmInstBr(rs1, rs2, target), IAsmInst
{
    public void Execute(in RunTimeContext context) { if (rs1.Get(context) >= rs2.Get(context)) { context.pc = target; } }
}

class AsmInstJ(int target) : IAsmInst
{
    int target = target;
    public void Execute(in RunTimeContext context) => context.pc = target;
}

class AsmInstApc(IOpParam rd, IOpParam offset) : IAsmInst
{
    readonly IOpParam rd = rd;
    readonly IOpParam offste = offset;
    public void Execute(in RunTimeContext context) => rd.Set(context, context.pc + offste.Get(context));
}

class AsmInstEnd : IAsmInst
{
    public void Execute(in RunTimeContext context)
    {
        context.finished = true;
    }
}


delegate IAsmInst ALUInstFactory(IOpParam rd, IOpParam rs1, IOpParam rs2);
delegate IAsmInst BRUInstFactory(IOpParam rs1, IOpParam rs2, int target);
