﻿using System.ComponentModel;
using Microsoft.SemanticKernel;
using SkPluginLibrary.Services;

namespace SkPluginLibrary.Plugins;

public class CodeExecuterPlugin
{
    private readonly CompilerService _compilerService = new();
    private readonly ScriptService _scriptService = new();
    [KernelFunction, Description("Execute provided c# code. Returns the console output")]
    [return: Description("Console output after execution")]
    public async Task<string> ExecuteCode([Description("C# code to execute")] string input)
    {
        input = input.Replace("```csharp", "").Replace("```", "").TrimStart('\n');
        var result = await _compilerService.SubmitCode(input, CompileResources.PortableExecutableReferences);
        return result;
    }
    [KernelFunction, Description("Execute provided c# code. Returns the script output")]
    public async Task<string> ExecuteScript([Description("C# code to execute")] string input)
    {
        input = input.Replace("```csharp", "").Replace("```", "").TrimStart('\n');
        var result = await _scriptService.EvaluateAsync(input);
        return result;
    }
}