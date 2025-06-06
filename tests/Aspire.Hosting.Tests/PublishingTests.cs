// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREPUBLISHERS001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using Aspire.Hosting.Utils;
using Xunit;

namespace Aspire.Hosting.Tests;

public class PublishingTests
{
    [Fact]
    public void PublishCallsPublishingCallback()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish, publisher: "default");

        var publishedCalled = false;

        builder.AddContainer("cache", "redis")
               .WithPublishingCallback(context =>
                {
                    Assert.NotNull(context);
                    Assert.NotNull(context.Services);
                    Assert.True(context.CancellationToken.CanBeCanceled);
                    Assert.Equal(DistributedApplicationOperation.Publish, context.ExecutionContext.Operation);
                    Assert.Equal("default", context.ExecutionContext.PublisherName);
                    Assert.True(Path.IsPathFullyQualified(context.OutputPath));
                    publishedCalled = true;
                    return Task.CompletedTask;
                });

        using var app = builder.Build();
        app.Run();

        Assert.True(publishedCalled, "Publishing callback was not called.");
    }
}
