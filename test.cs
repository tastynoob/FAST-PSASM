using System;
using System.Diagnostics;
using PSASM;

string fibocode = @"
    j main
fibo:
    push ra s1 s2   ; save context
    b< s0 2 ret     ; if x < 2 return 2
    mv s2 s0        ; s2 = x
    c- s0 s2 1      ; fibo(x-1)
    apc ra 2        ; set return address
    j fibo          ; call fibo
    mv s1 s0        ; t = fibo(x-1)
    c- s0 s2 2      ; fibo(x-2)
    apc ra 2        ; set return address
    j fibo          ; call fibo
    c+ s0 s0 s1     ; return fibo(x-1) + fibo(x-2)
ret:
    pop ra s1 s2    ; restore context
    j ra ; return
main:
    mv s0 35        ; set x, use s0 as arg and result
    apc ra 2
    j fibo          ; call fibo fibo(20) should is 6765
";

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
    c+ s4 s4 1
    b< s4 s3 loop
    apc s0 0
";

string inout = @"
loop:
c+ s0 s0 1
sync
j loop
";

MyProgram myProgram = new(fibocode);
Stopwatch sw = Stopwatch.StartNew();
myProgram.input = 2;
myProgram.Run();
sw.Stop();
Console.WriteLine("res: " + myProgram.GetResult((int)AsmParser.RegId.s0));
Console.WriteLine("time: " + sw.Elapsed.TotalMilliseconds + "ms");


class MyProgram : PSASMContext
{
    public MyProgram(in string program)
    {
        Programming(program);
    }

    public void Run()
    {
        while (Steps(100)) ;
    }

    public int GetResult(int regid) => rf[regid];
}