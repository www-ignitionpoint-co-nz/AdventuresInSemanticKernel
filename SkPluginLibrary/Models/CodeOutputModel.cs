﻿using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SkPluginLibrary.Services;
using System.ComponentModel;

namespace SkPluginLibrary.Models;

[TypeConverter(typeof(GenericTypeConverter<CodeOutputModel>))]
public class CodeOutputModel
{
    public string? Code { get; set; }
    public string? Output { get; set; }
    //public override string ToString()
    //{
    //    return $"Code:\n{Code}\n\nOutput:\n{Output}";
    //}
}

public class CodeElementsDescriptionsModel
{
    public List<CodeElementDescription> MethodDescription { get; set; } = new();
    public List<CodeElementDescription> ConstructorDescription { get; set; } = new();
    public List<CodeElementDescription> PropertyDescription { get; set; } = new();
    public List<CodeElementDescription> FieldDescription { get; set; } = new();

    public IEnumerable<CodeElementDescription> GetAllSyntaxDescriptions()
    {
        return MethodDescription.Concat(ConstructorDescription).Concat(PropertyDescription).Concat(FieldDescription);

    }
    public static CodeElementsDescriptionsModel ExtractCodeElements(string codeText)
    {
        var tree = CSharpSyntaxTree.ParseText(codeText);
        var root = tree.GetRoot();
        var model = new CodeElementsDescriptionsModel();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            model.MethodDescription.Add(new CodeElementDescription(method.ToFullString(), CodeSyntaxElement.Method));
        }
        foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            model.ConstructorDescription.Add(new CodeElementDescription(constructor.ToFullString(), CodeSyntaxElement.Constructor));
        }

        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            model.PropertyDescription.Add(new CodeElementDescription(prop.ToFullString(), CodeSyntaxElement.Property));
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            model.FieldDescription.Add(new CodeElementDescription(field.ToFullString(), CodeSyntaxElement.Field));
        }

        return model;
    }

}

public enum CodeSyntaxElement { Method, Property, Field, Constructor }

public record CodeElementDescription(string Code, CodeSyntaxElement SyntaxElement)
{
    public string? GeneratedDescription { get; set; }
}

