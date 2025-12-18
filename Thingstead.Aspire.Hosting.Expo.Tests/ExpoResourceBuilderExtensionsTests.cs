using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Aspire.Hosting;
using Xunit;
using Aspire.Hosting.ApplicationModel;

namespace Thingstead.Aspire.Hosting.Expo.Tests;

public class ExpoResourceBuilderExtensionsTests
{
    private static bool IsExecuteResultSuccess(object result)
    {
        if (result is null) return false;

        // Try common boolean property names
        var t = result.GetType();
        foreach (var name in new[] { "Succeeded", "Success", "IsSuccess", "IsSuccessful" })
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null && p.PropertyType == typeof(bool))
            {
                return (bool)p.GetValue(result)!;
            }
        }

        // fallback: compare ToString() vs CommandResults.Success().ToString() if available
        try
        {
            var cmdResultsType = typeof(ExpoResourceBuilderExtensions).Assembly.GetType("Aspire.Hosting.CommandResults") ?? Type.GetType("Aspire.Hosting.CommandResults, Aspire.Hosting");
            if (cmdResultsType != null)
            {
                var success = cmdResultsType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                if (success != null)
                {
                    return result.ToString() == success.ToString();
                }
            }
        }
        catch { }

        return false;
    }

    [Fact]
    public async Task OnRunGenerateQrCommandAsync_PublicUrlTaskNull_ReturnsSuccess()
    {
        var type = typeof(ExpoResourceBuilderExtensions);
        var method = type.GetMethod("OnRunGenerateQrCommandAsync", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("method not found");

        var task = (Task)method.Invoke(null, [null, null])!;
        await task;

        // get result via dynamic await
        var genericTask = (dynamic)method.Invoke(null, [null, null])!;
        var result = await genericTask;

        Assert.True(IsExecuteResultSuccess(result));
    }

    [Fact]
    public async Task OnRunGenerateQrCommandAsync_PublicUrlTaskReturnsUriAndNullQrPath_ReturnsFailure()
    {
        var type = typeof(ExpoResourceBuilderExtensions);
        var method = type.GetMethod("OnRunGenerateQrCommandAsync", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("method not found");

        var publicUrlTask = Task.FromResult<Uri?>(new Uri("https://example.com"));

        var genericTask = (dynamic)method.Invoke(null, [publicUrlTask, null])!;
        var result = await genericTask;

        Assert.False(IsExecuteResultSuccess(result));
    }

    [Fact]
    public void OnUpdateResourceState_HealthyEnabled_UnhealthyDisabled()
    {
        var type = typeof(ExpoResourceBuilderExtensions);
        var method = type.GetMethod("OnUpdateResourceState", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("method not found");

        var ctxType = typeof(UpdateCommandStateContext);

        // find the ResourceSnapshot member and its exact type
        var snapMemberInfo = (MemberInfo?)ctxType.GetProperty("ResourceSnapshot", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (snapMemberInfo == null) snapMemberInfo = ctxType.GetField("ResourceSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(snapMemberInfo);

        Type snapExactType = snapMemberInfo is PropertyInfo pInfo ? pInfo.PropertyType : ((FieldInfo)snapMemberInfo).FieldType;
        // create instances without invoking constructors
        var ctx = (UpdateCommandStateContext)RuntimeHelpers.GetUninitializedObject(ctxType);
        var snap = RuntimeHelpers.GetUninitializedObject(snapExactType);

        var healthProp = snapExactType.GetProperty("HealthStatus", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.NotNull(healthProp);

        void SetHealth(object target, HealthStatus hs)
        {
            if (healthProp.PropertyType == typeof(string))
                healthProp.SetValue(target, hs.ToString());
            else
                healthProp.SetValue(target, hs);
        }

        // Healthy -> Enabled
        SetHealth(snap, HealthStatus.Healthy);
        if (snapMemberInfo is PropertyInfo spi) spi.SetValue(ctx, snap);
        else ((FieldInfo)snapMemberInfo).SetValue(ctx, snap);

        var resultHealthy = method.Invoke(null, [ctx]);
        Assert.Equal(ResourceCommandState.Enabled, resultHealthy);

        // Unhealthy -> Disabled
        SetHealth(snap, HealthStatus.Unhealthy);
        if (snapMemberInfo is PropertyInfo spi2) spi2.SetValue(ctx, snap);
        else ((FieldInfo)snapMemberInfo).SetValue(ctx, snap);

        var resultUnhealthy = method.Invoke(null, [ctx]);
        Assert.Equal(ResourceCommandState.Disabled, resultUnhealthy);
    }
}
