use anyhow::{Context, Result, bail};
use std::process::{Command, Stdio};

fn main() -> Result<()> {
  let command = std::env::args()
    .nth(1)
    .unwrap_or_else(|| "check".to_string());
  match command.as_str() {
    "check" => check(),
    other => bail!("unknown xtask command `{other}`"),
  }
}

fn check() -> Result<()> {
  run("cargo", &["fmt", "--check"])?;
  run(
    "cargo",
    &[
      "clippy",
      "--workspace",
      "--all-targets",
      "--",
      "-D",
      "warnings",
    ],
  )?;
  run("cargo", &["test", "--workspace"])?;
  Ok(())
}

fn run(program: &str, args: &[&str]) -> Result<()> {
  let status = Command::new(program)
    .args(args)
    .stdin(Stdio::inherit())
    .stdout(Stdio::inherit())
    .stderr(Stdio::inherit())
    .status()
    .with_context(|| format!("run {program} {}", args.join(" ")))?;
  if status.success() {
    Ok(())
  } else {
    bail!("{program} {} failed with {status}", args.join(" "))
  }
}
