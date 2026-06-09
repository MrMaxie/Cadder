namespace Cadder.Contracts;

public static class CadderRoles
{
    public const string TrayDaemonSingleton = "tray-daemon-singleton";
    public const string CaddyShimEntrypoint = "caddy-shim-entrypoint";
    public const string RealCaddyRuntimeAdapter = "real-caddy-runtime-adapter";
    public const string IpcContract = "ipc-contract";
    public const string RegistrationStore = "registration-store";
    public const string GuiStateProjection = "gui-state-projection";
}
