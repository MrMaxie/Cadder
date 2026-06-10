use anyhow::Result;
use cadder_daemon::{DaemonOptions, run_daemon};
use clap::Parser;
use std::path::PathBuf;
use tokio::sync::watch;

#[derive(Debug, Parser)]
#[command(
  name = "cadderd",
  version,
  about = "Cadder per-user Caddy coordinator daemon"
)]
struct Args {
  #[arg(long)]
  runtime_dir: Option<PathBuf>,

  #[arg(long)]
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
