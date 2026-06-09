namespace Cadder.CaddyShim;

public interface IShimLifetimeWaiter
{
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}

public sealed class ConsoleShimLifetimeWaiter : IShimLifetimeWaiter
{
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            completed.TrySetResult();
        };
        EventHandler processExitHandler = (_, _) => completed.TrySetResult();

        Console.CancelKeyPress += cancelHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            await completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }
}

public sealed class ShimRuntimeDependencies
{
    public Func<string> CurrentDirectoryProvider { get; init; } = Directory.GetCurrentDirectory;

    public IShimProcessIdentityProvider ProcessIdentityProvider { get; init; }
        = new CurrentShimProcessIdentityProvider();

    public ICadderDaemonConnector DaemonConnector { get; init; } = new NamedPipeCadderDaemonConnector();

    public IDaemonProcessLauncher DaemonLauncher { get; init; } = new ProcessDaemonLauncher();

    public IShimLifetimeWaiter LifetimeWaiter { get; init; } = new ConsoleShimLifetimeWaiter();

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public TextWriter Output { get; init; } = Console.Out;

    public TextWriter Error { get; init; } = Console.Error;

    public Func<string> NonceFactory { get; init; } = static () => Guid.NewGuid().ToString("N");

    public TimeSpan DaemonReadyTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan DaemonReadyPollInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}
