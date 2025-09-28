using FakeItEasy;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UnitTests;

public class CommandScopeBuilderTests
{
    private readonly ICommandScope _fakeScope;
    private readonly ICommand _fakeCommand;
    private readonly ICommand<string> _fakeCommandWithResponse;

    public CommandScopeBuilderTests()
    {
        _fakeScope = A.Fake<ICommandScope>();
        _fakeCommand = A.Fake<ICommand>();
        _fakeCommandWithResponse = A.Fake<ICommand<string>>();
    }

    [Fact]
    public async Task SendAsync_Should_CallScopeSendAsync_WithDefaultValues()
    {
        // Arrange
        var builder = new CommandScopeBuilder(_fakeScope, _fakeCommand);

        // Act
        await builder.SendAsync();

        // Assert
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommand, TimeSpan.FromSeconds(1), null, default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WithTimeout_Should_OverrideTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(5);
        var builder = new CommandScopeBuilder(_fakeScope, _fakeCommand)
            .WithTimeout(timeout);

        // Act
        await builder.SendAsync();

        // Assert
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommand, timeout, null, default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnTimeout_Should_PassHandler()
    {
        // Arrange
        Action<Exception, MeshContext> handler = (_, _) => { };
        var builder = new CommandScopeBuilder(_fakeScope, _fakeCommand)
            .OnTimeout(handler);

        // Act
        await builder.SendAsync();

        // Assert
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommand, TimeSpan.FromSeconds(1), handler, default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WithCancellation_Should_PassToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var builder = new CommandScopeBuilder(_fakeScope, _fakeCommand)
            .WithCancellation(cts.Token);

        // Act
        await builder.SendAsync();

        // Assert
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommand, TimeSpan.FromSeconds(1), null, cts.Token))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Generic_SendAsync_Should_CallScopeSendAsync()
    {
        // Arrange
        var builder = new CommandScopeBuilder<string>(_fakeScope, _fakeCommandWithResponse);

        // Act
        await builder.SendAsync();

        // Assert
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommandWithResponse, TimeSpan.FromSeconds(1), null, default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Generic_StreamAsync_Should_CallScopeStreamAsync()
    {
        // Arrange
        var builder = new CommandScopeBuilder<string>(_fakeScope, _fakeCommandWithResponse);

        // Act
        _ = builder.StreamAsync();

        // Assert
        A.CallTo(() => _fakeScope.StreamAsync(_fakeCommandWithResponse, TimeSpan.FromSeconds(1), null, default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Generic_StreamResultAsync_Should_CallScopeStreamResultAsync()
    {
        // Arrange
        var builder = new CommandScopeBuilder<string>(_fakeScope, _fakeCommandWithResponse);

        // Act
        _ = builder.StreamResultAsync();

        // Assert
        A.CallTo(() => _fakeScope.StreamResultAsync(_fakeCommandWithResponse, TimeSpan.FromSeconds(1), default))
         .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GenericBuilder_Should_ChainAllMethods_And_CallScope()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        Action<Exception, MeshContext> onTimeout = (_, _) => { };
        var timeout = TimeSpan.FromSeconds(10);

        var builder = new CommandScopeBuilder(_fakeScope, _fakeCommand)
            .WithTimeout(timeout)
            .OnTimeout(onTimeout)
            .WithCancellation(cts.Token);

        // Act
        await builder.SendAsync();  

        // Assert: SendAsync uses all configured options
        A.CallTo(() => _fakeScope.SendAsync(_fakeCommand, timeout, onTimeout, cts.Token))
         .MustHaveHappenedOnceExactly();    
    }
}