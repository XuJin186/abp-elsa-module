﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Composition.Hosting;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Passingwind.Abp.ElsaModule.Scripting.CSharp.MonacoProviders.Roslyns;
public class CSharpCodeAnalysis
{
    public string Code { get; protected set; }

    private static readonly Assembly[] _assemblies = new[]
    {
        Assembly.Load("Microsoft.CodeAnalysis"),
        Assembly.Load("Microsoft.CodeAnalysis.CSharp"),
        Assembly.Load("Microsoft.CodeAnalysis.Features"),
        Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
    };

    private static readonly MetadataReference[] _defaultMetadataReferences = new MetadataReference[]
    {
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(int).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                MetadataReference.CreateFromFile(typeof(DescriptionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DataSet).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(XmlDocument).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location)
    };

    public AdhocWorkspace Workspace { get; private set; }
    public ProjectInfo Project { get; private set; }
    public Document Document { get; private set; }

    public static CSharpCodeAnalysis Create()
    {
        var analysis = new CSharpCodeAnalysis();
        analysis.Initial();
        return analysis;
    }

    protected CSharpCodeAnalysis()
    {
    }

    protected void Initial()
    {
        if (Workspace != null)
            return;

        var partTypes = MefHostServices.DefaultAssemblies.Concat(_assemblies)
                .Distinct()
                .SelectMany(x => x.GetTypes())
                .ToArray();

        var compositionContext = new ContainerConfiguration()
                   .WithParts(partTypes)
                   .CreateContainer();

        var host = MefHostServices.Create(compositionContext);

        Workspace = new AdhocWorkspace(host);

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        var name = Guid.NewGuid().ToString("N");

        Project = ProjectInfo
            .Create(ProjectId.CreateNewId(), VersionStamp.Create(), name, name, LanguageNames.CSharp)
            .WithCompilationOptions(compilationOptions)
            .WithMetadataReferences(_defaultMetadataReferences);

        Workspace.AddProject(Project);

        Document = Workspace.AddDocument(Project.Id, $"{name}.cs", SourceText.From(Code ?? ""));
    }

    internal void LoadCode(string code)
    {
        throw new NotImplementedException();
    }
}