using Cadder.Contracts;

namespace Cadder.CaddyShim;

public static class ShimEntrypoint
{
  public static int Run(string[] args)
  {
    return RunAsync(args).GetAwaiter().GetResult();
  }

  public static async Task<int> RunAsync(
      string[] args,
      ShimRuntimeDependencies? dependencies = null,
      CancellationToken cancellationToken = default)
  {
    dependencies ??= new ShimRuntimeDependencies();

    var parseResult = ShimCommandParser.Parse(args, dependencies.CurrentDirectoryProvider());
    if (!parseResult.Succeeded)
    {
      await dependencies.Error.WriteLineAsync(parseResult.ErrorMessage).ConfigureAwait(false);
      return 2;
    }

    if (parseResult.CommandKind == ShimCommandKind.Info)
    {
      await dependencies.Output
          .WriteLineAsync($"{CadderRoles.CaddyShimEntrypoint}: PATH-facing caddy.exe shim")
          .ConfigureAwait(false);
      return 0;
    }

    if (parseResult.RunCommand is null)
    {
      await dependencies.Error.WriteLineAsync("The parsed caddy run command was empty.").ConfigureAwait(false);
      return 2;
    }

    return await RunShimSessionAsync(parseResult.RunCommand, dependencies, cancellationToken)
        .ConfigureAwait(false);
  }

  private static async Task<int> RunShimSessionAsync(
      ShimRunCommand command,
      ShimRuntimeDependencies dependencies,
      CancellationToken cancellationToken)
  {
    var shimSessionNonce = dependencies.NonceFactory();
    var registration = ShimRegistrationFactory.Create(
        command,
        dependencies.ProcessIdentityProvider.GetCurrentProcessIdentity(),
        dependencies.TimeProvider.GetUtcNow(),
        shimSessionNonce);
    var registerRequest = new RegisterEntrypointRequest(
        Guid.NewGuid().ToString("N"),
        registration);

    ICadderDaemonConnection connection;
    try
    {
      connection = await ConnectOrStartDaemonAsync(dependencies, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or FileNotFoundException or InvalidOperationException)
    {
      await dependencies.Error
          .WriteLineAsync($"Cadder daemon startup/readiness failed: {ex.Message}")
          .ConfigureAwait(false);
      return 1;
    }

    await using (connection.ConfigureAwait(false))
    {
      RegisterEntrypointResponse registerResponse;
      try
      {
        registerResponse = await connection.RegisterAsync(registerRequest, cancellationToken)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is IOException or InvalidOperationException)
      {
        await dependencies.Error
            .WriteLineAsync($"Cadder daemon registration failed: {ex.Message}")
            .ConfigureAwait(false);
        return 1;
      }

      if (!registerResponse.Accepted)
      {
        await dependencies.Error
            .WriteLineAsync(registerResponse.Message ?? "Cadder daemon rejected the shim registration.")
            .ConfigureAwait(false);
        return 1;
      }

      var registeredId = registerResponse.RegistrationId ?? registration.RegistrationId;
      using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      var heartbeatTask = RunHeartbeatLoopAsync(
          connection,
          registeredId,
          registration.EntrypointInstance.ShimSessionNonce,
          dependencies.HeartbeatInterval,
          heartbeatStop.Token);

      try
      {
        var lifetimeTask = dependencies.LifetimeWaiter.WaitAsync(cancellationToken).AsTask();
        var completedTask = await Task.WhenAny(lifetimeTask, heartbeatTask).ConfigureAwait(false);
        if (completedTask == heartbeatTask)
        {
          await heartbeatTask.ConfigureAwait(false);
        }

        await lifetimeTask.ConfigureAwait(false);
        return 0;
      }
      catch (Exception ex) when (ex is IOException or InvalidOperationException)
      {
        await dependencies.Error
            .WriteLineAsync($"Cadder daemon heartbeat failed: {ex.Message}")
            .ConfigureAwait(false);
        return 1;
      }
      finally
      {
        await heartbeatStop.CancelAsync().ConfigureAwait(false);
        try
        {
          await heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
        }

        try
        {
          await connection.UnregisterAsync(
              new UnregisterEntrypointRequest(
                  Guid.NewGuid().ToString("N"),
                  registeredId,
                  registration.EntrypointInstance.ShimSessionNonce),
              CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
          await dependencies.Error
              .WriteLineAsync($"Cadder daemon unregister failed after session end: {ex.Message}")
              .ConfigureAwait(false);
        }
      }
    }
  }

  private static async Task RunHeartbeatLoopAsync(
      ICadderDaemonConnection connection,
      string registrationId,
      string shimSessionNonce,
      TimeSpan heartbeatInterval,
      CancellationToken cancellationToken)
  {
    while (true)
    {
      await Task.Delay(heartbeatInterval, cancellationToken).ConfigureAwait(false);

      var response = await connection.HeartbeatAsync(
          new HeartbeatEntrypointRequest(
              Guid.NewGuid().ToString("N"),
              registrationId,
              shimSessionNonce),
          cancellationToken).ConfigureAwait(false);

      if (!response.Accepted)
      {
        throw new InvalidOperationException(response.Message ?? "The daemon rejected the shim heartbeat.");
      }
    }
  }

  private static async ValueTask<ICadderDaemonConnection> ConnectOrStartDaemonAsync(
      ShimRuntimeDependencies dependencies,
      CancellationToken cancellationToken)
  {
    if (await TryConnectAsync(dependencies, cancellationToken).ConfigureAwait(false) is { } initialConnection)
    {
      return initialConnection;
    }

    await dependencies.DaemonLauncher.StartAsync(cancellationToken).ConfigureAwait(false);

    using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    readiness.CancelAfter(dependencies.DaemonReadyTimeout);

    try
    {
      while (!readiness.IsCancellationRequested)
      {
        if (await TryConnectAsync(dependencies, readiness.Token).ConfigureAwait(false) is { } connection)
        {
          return connection;
        }

        await Task.Delay(dependencies.DaemonReadyPollInterval, readiness.Token).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) when (readiness.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
    {
    }

    throw new TimeoutException(
        $"Cadder daemon IPC was not ready within {dependencies.DaemonReadyTimeout.TotalSeconds:0.#} seconds after startup.");
  }

  private static async ValueTask<ICadderDaemonConnection?> TryConnectAsync(
      ShimRuntimeDependencies dependencies,
      CancellationToken cancellationToken)
  {
    try
    {
      return await dependencies.DaemonConnector.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (IOException)
    {
      return null;
    }
    catch (TimeoutException)
    {
      return null;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      return null;
    }
  }
}
