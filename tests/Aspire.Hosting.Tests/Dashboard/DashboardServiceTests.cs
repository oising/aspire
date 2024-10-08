// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Dashboard;
using Aspire.Hosting.Tests.Utils;
using Aspire.Hosting.Tests.Utils.Grpc;
using Aspire.Hosting.Utils;
using Aspire.ResourceService.Proto.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DashboardService = Aspire.Hosting.Dashboard.DashboardService;
using Resource = Aspire.Hosting.ApplicationModel.Resource;

namespace Aspire.Hosting.Tests.Dashboard;

public class DashboardServiceTests
{
    [Fact]
    public async Task WatchResourceConsoleLogs_LargePendingData_BatchResults()
    {
        // Arrange
        const int LongLineCharacters = DashboardService.LogMaxBatchCharacters / 3;
        var resourceLoggerService = new ResourceLoggerService();
        var resourceNotificationService = new ResourceNotificationService(NullLogger<ResourceNotificationService>.Instance, new TestHostApplicationLifetime(), new ServiceCollection().BuildServiceProvider(), resourceLoggerService);
        await using var dashboardServiceData = new DashboardServiceData(resourceNotificationService, resourceLoggerService, NullLogger<DashboardServiceData>.Instance, new DashboardCommandExecutor(new ServiceCollection().BuildServiceProvider()));
        var dashboardService = new DashboardService(dashboardServiceData, new TestHostEnvironment(), new TestHostApplicationLifetime(), NullLogger<DashboardService>.Instance);

        var logger = resourceLoggerService.GetLogger("test-resource");

        // Exceed limit line
        logger.LogInformation(new string('1', DashboardService.LogMaxBatchCharacters));
        // Three long lines
        logger.LogInformation(new string('2', LongLineCharacters));
        logger.LogInformation(new string('3', LongLineCharacters));
        logger.LogInformation(new string('4', LongLineCharacters));

        var context = TestServerCallContext.Create();
        var writer = new TestServerStreamWriter<WatchResourceConsoleLogsUpdate>(context);

        // Act
        var task = dashboardService.WatchResourceConsoleLogs(
            new WatchResourceConsoleLogsRequest { ResourceName = "test-resource" },
            writer,
            context);

        // Assert
        var exceedLimitUpdate = await writer.ReadNextAsync();
        Assert.Collection(exceedLimitUpdate.LogLines,
            l => Assert.Equal(DashboardService.LogMaxBatchCharacters, l.Text.Length));

        var longLinesUpdate1 = await writer.ReadNextAsync();
        Assert.Collection(longLinesUpdate1.LogLines,
            l => Assert.Equal(LongLineCharacters, l.Text.Split(' ')[1].Length),
            l => Assert.Equal(LongLineCharacters, l.Text.Split(' ')[1].Length));

        var longLinesUpdate2 = await writer.ReadNextAsync();
        Assert.Collection(longLinesUpdate2.LogLines,
            l => Assert.Equal(LongLineCharacters, l.Text.Split(' ')[1].Length));

        resourceLoggerService.Complete("test-resource");
        await task;
    }

    [Fact]
    public async Task WatchResources_ResourceHasCommands_CommandsSentWithResponse()
    {
        // Arrange
        var resourceLoggerService = new ResourceLoggerService();
        var resourceNotificationService = new ResourceNotificationService(NullLogger<ResourceNotificationService>.Instance, new TestHostApplicationLifetime(), new ServiceCollection().BuildServiceProvider(), resourceLoggerService);
        await using var dashboardServiceData = new DashboardServiceData(resourceNotificationService, resourceLoggerService, NullLogger<DashboardServiceData>.Instance, new DashboardCommandExecutor(new ServiceCollection().BuildServiceProvider()));
        var dashboardService = new DashboardService(dashboardServiceData, new TestHostEnvironment(), new TestHostApplicationLifetime(), NullLogger<DashboardService>.Instance);

        var testResource = new TestResource("test-resource");
        using var applicationBuilder = TestDistributedApplicationBuilder.Create();
        var builder = applicationBuilder.AddResource(testResource);
        builder.WithCommand(
            type: "TestType",
            displayName: "Display name!",
            executeCommand: c => Task.FromResult(CommandResults.Success()),
            updateState: c => ApplicationModel.ResourceCommandState.Enabled,
            displayDescription: "Display description!",
            parameter: new [] {"One", "Two"},
            confirmationMessage: "Confirmation message!",
            iconName: "Icon name!",
            iconVariant: ApplicationModel.IconVariant.Filled,
            isHighlighted: true);

        await resourceNotificationService.PublishUpdateAsync(testResource, s => s);

        var cts = new CancellationTokenSource();
        var context = TestServerCallContext.Create(cancellationToken: cts.Token);
        var writer = new TestServerStreamWriter<WatchResourcesUpdate>(context);

        // Act
        var task = dashboardService.WatchResources(
            new WatchResourcesRequest(),
            writer,
            context);

        // Assert
        var update = await writer.ReadNextAsync();

        var resourceData = Assert.Single(update.InitialData.Resources);
        var commandData = Assert.Single(resourceData.Commands);

        Assert.Equal("TestType", commandData.CommandType);
        Assert.Equal("Display name!", commandData.DisplayName);
        Assert.Equal("Display description!", commandData.DisplayDescription);
        Assert.Equal(Value.ForList(Value.ForString("One"), Value.ForString("Two")), commandData.Parameter);
        Assert.Equal("Confirmation message!", commandData.ConfirmationMessage);
        Assert.Equal("Icon name!", commandData.IconName);
        Assert.Equal(ResourceService.Proto.V1.IconVariant.Filled, commandData.IconVariant);
        Assert.True(commandData.IsHighlighted);

        cts.Cancel();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Ok if this error is thrown.
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = default!;
        public IFileProvider ContentRootFileProvider { get; set; } = default!;
        public string ContentRootPath { get; set; } = default!;
        public string EnvironmentName { get; set; } = default!;
    }

    private sealed class TestResource(string name) : Resource(name)
    {
    }
}
