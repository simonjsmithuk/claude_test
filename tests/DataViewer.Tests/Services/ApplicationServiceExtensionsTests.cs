using DataViewer.Application.DependencyInjection;
using DataViewer.Application.Interfaces;
using DataViewer.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataViewer.Tests.Services;

/// <summary>
/// Integration tests for <see cref="ApplicationServiceExtensions.AddApplicationServices"/>.
/// Verifies that each service is registered with the correct lifetime and interface binding,
/// matching the TASK-018 acceptance criterion "all three services are registered as Singleton".
/// </summary>
[Trait("Category", "Integration")]
public sealed class ApplicationServiceExtensionsTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> with all Application services registered,
    /// plus the support services they need (logging, scope factory).
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // ISystemSettingsRepository is Scoped; register a stub so BodyTruncator can resolve it.
        services.AddScoped<ISystemSettingsRepository>(_ =>
        {
            var mock = new Moq.Mock<ISystemSettingsRepository>();
            mock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataViewer.Domain.Entities.SystemSettings { BodySizeCapMb = 5 });
            return mock.Object;
        });
        services.AddApplicationServices();
        return services.BuildServiceProvider();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Registration — interfaces are resolvable
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddApplicationServices_IGzipDecompressor_IsResolvable()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<IGzipDecompressor>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<GzipDecompressor>();
    }

    [Fact]
    public void AddApplicationServices_IContentTypeDetector_IsResolvable()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<IContentTypeDetector>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<ContentTypeDetector>();
    }

    [Fact]
    public void AddApplicationServices_IBodyTruncator_IsResolvable()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<IBodyTruncator>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<BodyTruncator>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Registration — Singleton lifetime (same instance across multiple resolves)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddApplicationServices_GzipDecompressor_IsRegisteredAsSingleton()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act — resolve twice from the same container
        var first  = provider.GetRequiredService<IGzipDecompressor>();
        var second = provider.GetRequiredService<IGzipDecompressor>();

        // Assert
        first.Should().BeSameAs(second,
            because: "Singleton-registered services must return the exact same instance on every resolution");
    }

    [Fact]
    public void AddApplicationServices_ContentTypeDetector_IsRegisteredAsSingleton()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var first  = provider.GetRequiredService<IContentTypeDetector>();
        var second = provider.GetRequiredService<IContentTypeDetector>();

        // Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddApplicationServices_BodyTruncator_IsRegisteredAsSingleton()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var first  = provider.GetRequiredService<IBodyTruncator>();
        var second = provider.GetRequiredService<IBodyTruncator>();

        // Assert
        first.Should().BeSameAs(second);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Registration — Singleton survives scope boundaries
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddApplicationServices_GzipDecompressor_ReturnsSameInstanceAcrossScopes()
    {
        // Arrange
        using var provider = BuildProvider();

        IGzipDecompressor fromScope1, fromScope2;

        using (var scope1 = provider.CreateScope())
        {
            fromScope1 = scope1.ServiceProvider.GetRequiredService<IGzipDecompressor>();
        }

        using (var scope2 = provider.CreateScope())
        {
            fromScope2 = scope2.ServiceProvider.GetRequiredService<IGzipDecompressor>();
        }

        // Assert — singleton is shared across scope boundaries
        fromScope1.Should().BeSameAs(fromScope2);
    }

    [Fact]
    public void AddApplicationServices_ContentTypeDetector_ReturnsSameInstanceAcrossScopes()
    {
        using var provider = BuildProvider();

        IContentTypeDetector s1, s2;
        using (var scope = provider.CreateScope())
            s1 = scope.ServiceProvider.GetRequiredService<IContentTypeDetector>();
        using (var scope = provider.CreateScope())
            s2 = scope.ServiceProvider.GetRequiredService<IContentTypeDetector>();

        s1.Should().BeSameAs(s2);
    }

    [Fact]
    public void AddApplicationServices_BodyTruncator_ReturnsSameInstanceAcrossScopes()
    {
        using var provider = BuildProvider();

        IBodyTruncator t1, t2;
        using (var scope = provider.CreateScope())
            t1 = scope.ServiceProvider.GetRequiredService<IBodyTruncator>();
        using (var scope = provider.CreateScope())
            t2 = scope.ServiceProvider.GetRequiredService<IBodyTruncator>();

        t1.Should().BeSameAs(t2);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Registration — method chaining
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddApplicationServices_ReturnsTheSameServiceCollection_ForMethodChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var returned = services.AddApplicationServices();

        // Assert
        returned.Should().BeSameAs(services,
            because: "extension methods must return the same IServiceCollection to support fluent chaining");
    }
}
