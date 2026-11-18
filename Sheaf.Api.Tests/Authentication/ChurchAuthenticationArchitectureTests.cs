using System.Reflection;
using System.Reflection.Emit;

using Microsoft.EntityFrameworkCore;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Api.Tests;

public class ChurchAuthenticationArchitectureTests
{
    private static readonly Assembly[] ProductionAssemblies =
        [typeof(TenantContext).Assembly, typeof(IChurchAccessService).Assembly, typeof(Program).Assembly];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => field.GetValue(null)).OfType<OpCode>()
        .ToDictionary(opCode => unchecked((ushort)opCode.Value));

    [Test]
    public async Task IgnoreQueryFilters_ProductionCallSites_OnlyDirectoryUsesNamedBypass()
    {
        CallSite[] bypasses = GetCallSites().Where(site =>
            site.Callee.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
            && site.Callee.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)).ToArray();

        await Assert.That(bypasses).Count().IsEqualTo(1);
        await Assert.That(RootType(bypasses.Single().Caller.DeclaringType)?.FullName)
            .IsEqualTo("Sheaf.Core.Authentication.ChurchMembershipDirectory");
        await Assert.That(bypasses.Single().Callee.GetParameters().Length).IsEqualTo(2);
    }

    [Test]
    public async Task GetChurchesForUserAsync_DirectoryCallSites_OnlyChurchAccessServiceCallsDirectory()
    {
        string[] callers = GetCallSites().Where(site =>
                site.Callee.DeclaringType?.FullName == "Sheaf.Core.Authentication.ChurchMembershipDirectory"
                && site.Callee.Name == nameof(IChurchAccessService.GetChurchesForUserAsync))
            .Select(site => RootType(site.Caller.DeclaringType)?.FullName ?? "<missing caller>").Distinct().ToArray();

        await Assert.That(callers).IsEquivalentTo(["Sheaf.Core.Authentication.ChurchAccessService"]);
    }

    [Test]
    public async Task Initialize_ProductionCallSites_OnlyAuthenticationBoundaryMutatesContext()
    {
        string[] callers = GetCallSites().Where(site =>
                site.Callee.DeclaringType == typeof(ITenantContextInitializer)
                && site.Callee.Name == nameof(ITenantContextInitializer.Initialize))
            .Select(site => RootType(site.Caller.DeclaringType)?.FullName ?? "<missing caller>").Distinct().ToArray();

        await Assert.That(callers).IsEquivalentTo(
            ["Sheaf.Core.Authentication.ChurchAccessService", "Sheaf.Authentication.ChurchCookieEvents"]);
        await Assert.That(typeof(TenantContext).GetProperties().Where(property => property.SetMethod?.IsPublic == true)).IsEmpty();
        await Assert.That(typeof(ICurrentChurch).GetProperties().Where(property => property.SetMethod is not null)).IsEmpty();
        await Assert.That(ProductionAssemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ITenantContextInitializer).IsAssignableFrom(type))
            .Select(type => type.FullName ?? type.Name)).IsEquivalentTo(["Sheaf.Data.TenantContext+Initializer"]);
    }

    private static IEnumerable<CallSite> GetCallSites()
    {
        foreach (Type type in ProductionAssemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            IEnumerable<MethodBase> methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static));
            foreach (MethodBase caller in methods)
            {
                if (caller.GetMethodBody()?.GetILAsByteArray() is not { } body)
                {
                    continue;
                }

                int offset = 0;
                while (offset < body.Length)
                {
                    ushort value = body[offset++];
                    if (value == 0xfe)
                    {
                        value = (ushort)(0xfe00 | body[offset++]);
                    }
                    OpCode opCode = OpCodesByValue[value];
                    if (opCode.OperandType == OperandType.InlineMethod)
                    {
                        int token = BitConverter.ToInt32(body, offset);
                        MethodBase? callee = caller.Module.ResolveMethod(token, type.GetGenericArguments(),
                            caller.IsGenericMethod ? caller.GetGenericArguments() : null);
                        if (callee is not null)
                        {
                            yield return new CallSite(caller, callee);
                        }
                    }

                    offset += opCode.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, offset),
                        _ => throw new InvalidOperationException($"Unexpected IL operand {opCode.OperandType}.")
                    };
                }
            }
        }
    }

    private static Type? RootType(Type? type)
    {
        while (type?.DeclaringType is { } parent)
        {
            type = parent;
        }
        return type;
    }

    private sealed record CallSite(MethodBase Caller, MethodBase Callee);
}
