using System.Security.Cryptography;
using System.Text;

namespace Cadder.Daemon;

public enum DaemonSingletonAcquisitionStatus
{
  Acquired = 0,
  AlreadyRunning = 1,
  AcquiredAfterAbandonedMutex = 2
}

public sealed record DaemonSingletonAcquisition(
    DaemonSingletonAcquisitionStatus Status,
    IDaemonSingletonLease? Lease)
{
  public bool HasOwnership => Lease is not null;
}

public interface IDaemonSingletonCoordinator
{
  string MutexName { get; }

  DaemonSingletonAcquisition TryAcquire();
}

public interface IDaemonSingletonLease : IDisposable
{
  string MutexName { get; }
}

public sealed class NamedMutexDaemonSingletonCoordinator : IDaemonSingletonCoordinator
{
  private readonly Func<string, IDaemonSingletonMutex> _mutexFactory;

  public NamedMutexDaemonSingletonCoordinator(string mutexName)
      : this(mutexName, static name => new SystemDaemonSingletonMutex(name))
  {
  }

  internal NamedMutexDaemonSingletonCoordinator(
      string mutexName,
      Func<string, IDaemonSingletonMutex> mutexFactory)
  {
    if (string.IsNullOrWhiteSpace(mutexName))
    {
      throw new ArgumentException("A daemon singleton mutex name is required.", nameof(mutexName));
    }

    MutexName = mutexName;
    _mutexFactory = mutexFactory ?? throw new ArgumentNullException(nameof(mutexFactory));
  }

  public string MutexName { get; }

  public DaemonSingletonAcquisition TryAcquire()
  {
    var mutex = _mutexFactory(MutexName);

    try
    {
      var acquired = false;
      var wasAbandoned = false;

      try
      {
        acquired = mutex.WaitOne(TimeSpan.Zero);
      }
      catch (AbandonedMutexException)
      {
        acquired = true;
        wasAbandoned = true;
      }

      if (!acquired)
      {
        mutex.Dispose();
        return new DaemonSingletonAcquisition(DaemonSingletonAcquisitionStatus.AlreadyRunning, null);
      }

      var status = wasAbandoned
          ? DaemonSingletonAcquisitionStatus.AcquiredAfterAbandonedMutex
          : DaemonSingletonAcquisitionStatus.Acquired;

      return new DaemonSingletonAcquisition(status, new NamedMutexDaemonSingletonLease(MutexName, mutex));
    }
    catch
    {
      mutex.Dispose();
      throw;
    }
  }
}

internal interface IDaemonSingletonMutex : IDisposable
{
  bool WaitOne(TimeSpan timeout);

  void ReleaseMutex();
}

public static class DaemonSingletonMutexNames
{
  public static string CreatePerUserName(string appKey = "Cadder.TrayDaemon")
  {
    if (string.IsNullOrWhiteSpace(appKey))
    {
      throw new ArgumentException("An app key is required.", nameof(appKey));
    }

    var userHash = CreatePerUserHash();

    return $@"Local\{appKey}.{userHash}";
  }

  public static string CreatePerUserAppInstanceKey(string appKey = "Cadder.TrayDaemon")
  {
    if (string.IsNullOrWhiteSpace(appKey))
    {
      throw new ArgumentException("An app key is required.", nameof(appKey));
    }

    return $"{appKey}.{CreatePerUserHash()}";
  }

  private static string CreatePerUserHash()
  {
    var userIdentity = $"{Environment.UserDomainName}\\{Environment.UserName}";

    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity)))[..16];
  }
}

internal sealed class NamedMutexDaemonSingletonLease : IDaemonSingletonLease
{
  private readonly IDaemonSingletonMutex _mutex;
  private bool _disposed;

  public NamedMutexDaemonSingletonLease(string mutexName, IDaemonSingletonMutex mutex)
  {
    MutexName = mutexName;
    _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));
  }

  public string MutexName { get; }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    try
    {
      _mutex.ReleaseMutex();
    }
    finally
    {
      _mutex.Dispose();
      _disposed = true;
    }
  }
}

internal sealed class SystemDaemonSingletonMutex : IDaemonSingletonMutex
{
  private readonly Mutex _mutex;

  public SystemDaemonSingletonMutex(string mutexName)
  {
    _mutex = new Mutex(false, mutexName);
  }

  public bool WaitOne(TimeSpan timeout)
  {
    return _mutex.WaitOne(timeout);
  }

  public void ReleaseMutex()
  {
    _mutex.ReleaseMutex();
  }

  public void Dispose()
  {
    _mutex.Dispose();
  }
}
