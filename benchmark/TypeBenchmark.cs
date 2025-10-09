using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;

namespace CommandBenchmark;

public class MyClass { }

[MemoryDiagnoser]
public class TypeBenchmarks
{
    private readonly Dictionary<string, Type> cache = new();

    MyClass obj = new MyClass(); // create new instance


    public TypeBenchmarks()
    {
        // Pre-cache type
        cache["MyNamespace.MyClass"] = typeof(MyClass);
    }

    [Benchmark]
    public Type UsingTypeof()
    {
        return typeof(MyClass);
    }

    [Benchmark]
    public Type UsingObjGetType()
    {
       return obj.GetType();
    }

    [Benchmark]
    public Type UsingTypeGetType()
    {
        return Type.GetType("MyClass") ?? typeof(MyClass);
    }

    [Benchmark]
    public Type UsingDictionaryLookup()
    {      
        return cache["MyNamespace.MyClass"];
    }
}
