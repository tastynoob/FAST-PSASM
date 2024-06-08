using System;
using System.Collections.Generic;
using System.Diagnostics;
using PSASM;

// SerializeTest();
DeserializeTest();


static void SerializeTest()
{
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


    MyProgram myProgram = new(fibocode)
    {
        input = 2
    };
    myProgram.Steps(1000000);
    Console.WriteLine("res: " + myProgram.GetResult((int)AsmParser.RegId.s0));
    Console.WriteLine("Serialize Test");
    AsmSerializer.Serialize(myProgram, out byte[] bytes);
    System.IO.File.WriteAllBytes("test.psa", bytes);
}

static void DeserializeTest()
{
    Console.WriteLine("Deserialize Test");
    MyProgram myProgram = new();
    Stopwatch sw = new();
    sw.Start();
    AsmSerializer.Deserialize(System.IO.File.ReadAllBytes("test.psa"), myProgram);
    sw.Stop();
    Console.WriteLine("deserialization time: " + sw.Elapsed.TotalMilliseconds + "ms");
    myProgram.Run();
    Console.WriteLine("res: " + myProgram.GetResult((int)AsmParser.RegId.s0));
}



class MyProgram : PSASMContext
{
    public MyProgram() { }
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