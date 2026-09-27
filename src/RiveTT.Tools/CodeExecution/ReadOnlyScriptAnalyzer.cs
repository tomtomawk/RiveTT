using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// The compile-time guard of transactionMode "readonly" — the one form of send_code_to_revit
/// the router lets through while the ribbon lock is closed.
///
/// The rolled-back TransactionGroup protects the active document during the run. The review
/// of 0.6.0 found four ways around it that a text filter cannot see, and this analyzer works on
/// the compiled syntax tree and its SYMBOLS instead:
///   - a callback that outlives the rollback: an Idling handler, an updater, an ExternalEvent,
///     a timer or a thread, any of which can write after the run, lock still closed;
///   - another document: Application.Documents reaches every open project and family;
///   - code hidden in an interpolation hole, or a method group (document.Close without
///     parentheses) that no "\.Close\(" pattern matches;
///   - a type or member declared by closing the Run body with "}}".
/// It runs inside the isolated Roslyn load context (RoslynCompilerWorker), like every other
/// Microsoft.CodeAnalysis use of the tool.
/// </summary>
public static class ReadOnlyScriptAnalyzer
{
    /// <summary>Methods, on any Autodesk.Revit type, whose effect is outside the model.</summary>
    private static readonly HashSet<string> ForbiddenMethods = new(StringComparer.Ordinal)
    {
        "Save", "SaveAs", "SaveCloudModel", "SaveAsCloudModel", "SaveLocalSharedModel", "SaveToProjectAsImage",
        "Close", "Export", "ExportImage", "Print", "SubmitPrint", "SynchronizeWithCentral",
        "ReloadLatest", "Reload", "ReloadFrom", "LoadFrom", "UnloadLocally", "RelinquishOwnership",
        "CheckoutElements", "CheckoutWorksets", "CreateNewLocal", "CopyModel", "WriteTransmissionData",
        "EditFamily", "OpenDocumentFile", "OpenAndActivateDocument", "OpenIFCDocument",
        "NewProjectDocument", "NewFamilyDocument", "NewProjectTemplateDocument", "PostCommand", "RequestViewChange",
        // A processor registered on the application keeps running after the script.
        "RegisterFailuresProcessor"
    };

    /// <summary>Type-qualified members forbidden in readonly (too generic by name alone).</summary>
    private static readonly HashSet<string> ForbiddenQualifiedMembers = new(StringComparer.Ordinal)
    {
        // Writes a definition to the shared parameter FILE, which no rollback undoes.
        "Autodesk.Revit.DB.Definitions.Create",
        // Every open document — rollback only covers the active one.
        "Autodesk.Revit.ApplicationServices.Application.Documents"
    };

    /// <summary>Properties whose SETTER acts outside the model.</summary>
    private static readonly HashSet<string> ForbiddenSetters = new(StringComparer.Ordinal)
    {
        "ActiveView", "ActiveGraphicalView", "SharedParametersFilename"
    };

    /// <summary>Types that run code later, after the rollback, or on another thread.</summary>
    private static readonly HashSet<string> ForbiddenTypes = new(StringComparer.Ordinal)
    {
        "Autodesk.Revit.UI.ExternalEvent", "Autodesk.Revit.UI.IExternalEventHandler",
        "Autodesk.Revit.DB.UpdaterRegistry", "Autodesk.Revit.DB.IUpdater",
        "Autodesk.Revit.DB.ExternalService.ExternalServiceRegistry", "Autodesk.Revit.DB.ExternalService.IExternalServer",
        "System.Threading.Thread", "System.Threading.ThreadPool", "System.Threading.Timer",
        "System.Timers.Timer", "System.Threading.Tasks.Task", "System.Threading.Tasks.Task<TResult>",
        "System.Threading.Tasks.TaskFactory", "System.Threading.Tasks.Parallel"
    };

    /// <summary>Violations, each with the caller's line; empty when the script may run.</summary>
    public static List<string> Analyze(CSharpCompilation compilation, SyntaxTree tree, int prefixLines)
    {
        var violations = new List<string>();
        var root = (CompilationUnitSyntax)tree.GetRoot();

        void Add(SyntaxNode node, string what)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 - prefixLines;
            var text = line >= 1 ? $"line {line}: {what}" : what;
            if (!violations.Contains(text)) violations.Add(text);
        }

        // ── structure: the caller's code must stay inside Run ────────────────
        var runner = root.Members.Count == 1 && root.Members[0] is BaseNamespaceDeclarationSyntax ns
                     && ns.Members.Count == 1 && ns.Members[0] is ClassDeclarationSyntax cls
                     && cls.Identifier.Text == "ScriptRunner" && cls.Members.Count == 1
                     && cls.Members[0] is MethodDeclarationSyntax run && run.Identifier.Text == "Run"
            ? run
            : null;
        if (runner == null)
        {
            Add(root, "code outside the script body: readonly scripts cannot declare types or members " +
                      "(closing the method with '}' is refused)");
            return violations;
        }

        var model = compilation.GetSemanticModel(tree);
        foreach (var node in runner.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment:
                {
                    var target = Symbol(model, assignment.Left);
                    if ((assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
                         assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)) && target is IEventSymbol evt)
                        Add(assignment, $"event subscription {evt.ContainingType?.Name}.{evt.Name}: a handler would run after the rollback");
                    if (target is IPropertySymbol property && ForbiddenSetters.Contains(property.Name) && IsRevit(property.ContainingType))
                        Add(assignment, $"{property.ContainingType?.Name}.{property.Name} = ... acts outside the model");
                    break;
                }
                case SimpleNameSyntax name:
                {
                    var symbol = Symbol(model, name);
                    if (symbol == null) break;
                    var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
                    if (type != null && ForbiddenTypes.Contains(type.OriginalDefinition.ToDisplayString()))
                    {
                        Add(name, $"{type.Name}: code that runs later or on another thread is refused in readonly");
                        break;
                    }
                    if (symbol is IMethodSymbol method && ForbiddenMethods.Contains(method.Name) && IsRevit(method.ContainingType))
                    {
                        Add(name, $"{method.ContainingType?.Name}.{method.Name} acts outside the model");
                        break;
                    }
                    if (symbol.ContainingType != null &&
                        ForbiddenQualifiedMembers.Contains($"{symbol.ContainingType.ToDisplayString()}.{symbol.Name}"))
                        Add(name, $"{symbol.ContainingType.Name}.{symbol.Name} is refused in readonly");
                    break;
                }
                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                    Add(node, "type declarations are refused in readonly");
                    break;
            }
        }
        return violations;
    }

    /// <summary>The symbol a node binds to, including a method group or an unresolved overload.</summary>
    private static ISymbol? Symbol(SemanticModel model, SyntaxNode node)
    {
        var info = model.GetSymbolInfo(node);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static bool IsRevit(INamedTypeSymbol? type)
    {
        for (var ns = type?.ContainingNamespace; ns != null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
            if (ns.ContainingNamespace?.IsGlobalNamespace == true)
                return ns.Name == "Autodesk";
        return false;
    }
}
