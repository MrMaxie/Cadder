use anyhow::Result;
use cadder_daemon::{DaemonOptions, run_daemon};
use clap::Parser;
use std::path::PathBuf;
use tokio::sync::watch;

#[derive(Debug, Parser)]
#[command(
  name = "cadderd",
  version,
  about = "Cadder per-user Caddy coordinator daemon",
  long_about = "Runs the per-user Cadder daemon that owns local IPC, project registrations, generated Caddy config, the Cadder-owned real Caddy process, diagnostics, and bounded logs."
)]
struct Args {
  #[arg(
    long,
    help = "Override the Cadder runtime directory for IPC, lock, config, metadata, and logs"
  )]
  runtime_dir: Option<PathBuf>,

  #[arg(
    long,
    help = "Command or path used when Cadder starts the real Caddy binary"
  )]
  real_caddy_command: Option<String>,

  #[arg(long, hide = true)]
  detach_ready: bool,
}

#[tokio::main]
async fn main() -> Result<()> {
  tracing_subscriber::fmt::init();
  let args = Args::parse();
  let (shutdown_tx, shutdown_rx) = watch::channel(false);

  tokio::spawn(async move {
    let _ = tokio::signal::ctrl_c().await;
    let _ = shutdown_tx.send(true);
  });

  run_daemon(
    DaemonOptions {
      runtime_dir: args.runtime_dir,
      real_caddy_command: args.real_caddy_command,
    },
    shutdown_rx,
  )
  .await
}

#[cfg(test)]
mod tests {
  use super::*;
  use clap::CommandFactory;

  #[test]
  fn command_metadata_matches_release_identity() {
    let command = Args::command();

    assert_eq!(command.get_name(), "cadderd");
    assert_eq!(command.get_version(), Some(env!("CARGO_PKG_VERSION")));
    assert_eq!(
      command.get_about().map(ToString::to_string),
      Some(env!("CARGO_PKG_DESCRIPTION").to_string())
    );
  }

  #[test]
  fn short_help_uses_package_description() {
    let help = Args::command().render_help().to_string();

    assert!(
      help.contains(env!("CARGO_PKG_DESCRIPTION")),
      "short help output should include the package description: {help}"
    );
  }

  #[test]
  fn long_help_describes_daemon_options() {
    let help = Args::command().render_long_help().to_string();

    assert!(
      help.contains("Override the Cadder runtime directory"),
      "long help output should describe --runtime-dir: {help}"
    );
    assert!(
      help.contains("Command or path used when Cadder starts the real Caddy binary"),
      "long help output should describe --real-caddy-command: {help}"
    );
  }
}
