use anyhow::{Context, Result, bail};
use std::{
  env, fs,
  path::{Path, PathBuf},
  process::{Command, Stdio},
};

const SAMPLE_CADDER_TOML: &str = r#"# Cadder portable configuration.
# Uncomment and set this when the real Caddy binary is not the first safe `caddy` on PATH.
#
# [caddy]
# real_command = "/absolute/path/to/caddy"
"#;

fn main() -> Result<()> {
  let mut args = env::args().skip(1);
  let command = args.next().unwrap_or_else(|| "check".to_string());
  match command.as_str() {
    "check" => check(),
    "dist" => dist(parse_path_option(args.collect(), "--out")?),
    "verify-dist" => verify_dist(&parse_path_option(args.collect(), "--dir")?),
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

fn dist(out_dir: PathBuf) -> Result<()> {
  run(
    "cargo",
    &[
      "build",
      "--release",
      "-p",
      "cadderd",
      "-p",
      "cadder-tui",
      "-p",
      "cadder-shim",
    ],
  )?;

  fs::create_dir_all(&out_dir).with_context(|| format!("create {}", out_dir.display()))?;
  for binary in portable_binaries() {
    let source = release_binary_path(binary);
    let target = out_dir.join(exe_name(binary));
    fs::copy(&source, &target)
      .with_context(|| format!("copy {} to {}", source.display(), target.display()))?;
  }
  fs::write(out_dir.join("cadder.toml"), SAMPLE_CADDER_TOML)
    .with_context(|| format!("write {}", out_dir.join("cadder.toml").display()))?;

  verify_dist(&out_dir)
}

fn verify_dist(dir: &Path) -> Result<()> {
  for binary in portable_binaries() {
    let path = dir.join(exe_name(binary));
    if !path.is_file() {
      bail!("portable binary missing: {}", path.display());
    }
  }

  let config = dir.join("cadder.toml");
  if !config.is_file() {
    bail!(
      "portable sample configuration missing: {}",
      config.display()
    );
  }

  let output = Command::new(dir.join(exe_name("caddy")))
    .arg("--cadder-shim-info")
    .stdin(Stdio::null())
    .output()
    .with_context(|| format!("run {}", dir.join(exe_name("caddy")).display()))?;
  if !output.status.success() {
    bail!("caddy --cadder-shim-info failed with {}", output.status);
  }
  let stdout = String::from_utf8_lossy(&output.stdout);
  if !stdout.contains("\"role\":\"caddy-shim\"") && !stdout.contains("\"role\": \"caddy-shim\"") {
    bail!("caddy --cadder-shim-info did not report the Cadder shim role");
  }

  Ok(())
}

fn parse_path_option(args: Vec<String>, option: &str) -> Result<PathBuf> {
  let mut iter = args.into_iter();
  while let Some(arg) = iter.next() {
    if arg == option {
      return iter
        .next()
        .map(PathBuf::from)
        .ok_or_else(|| anyhow::anyhow!("{option} requires a path"));
    }
  }
  bail!("{option} is required")
}

fn portable_binaries() -> [&'static str; 3] {
  ["cadderd", "cadder-tui", "caddy"]
}

fn release_binary_path(name: &str) -> PathBuf {
  PathBuf::from("target").join("release").join(exe_name(name))
}

fn exe_name(name: &str) -> String {
  #[cfg(windows)]
  {
    format!("{name}.exe")
  }
  #[cfg(not(windows))]
  {
    name.to_string()
  }
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

#[cfg(test)]
mod tests {
  use super::*;

  #[test]
  fn parse_path_option_reads_value_after_option() {
    let path = parse_path_option(
      vec!["--out".to_string(), "target/dist".to_string()],
      "--out",
    )
    .unwrap();

    assert_eq!(path, PathBuf::from("target/dist"));
  }

  #[test]
  fn parse_path_option_rejects_missing_option() {
    let error = parse_path_option(Vec::new(), "--out").unwrap_err();

    assert!(error.to_string().contains("--out is required"));
  }

  #[test]
  fn portable_layout_includes_expected_binaries() {
    assert_eq!(portable_binaries(), ["cadderd", "cadder-tui", "caddy"]);
  }

  #[test]
  fn parse_path_option_reads_value_after_unrelated_arguments() {
    let path = parse_path_option(
      vec![
        "--verbose".to_string(),
        "--dir".to_string(),
        "target/dist".to_string(),
      ],
      "--dir",
    )
    .unwrap();

    assert_eq!(path, PathBuf::from("target/dist"));
  }

  #[test]
  fn parse_path_option_rejects_option_without_value() {
    let error = parse_path_option(vec!["--dir".to_string()], "--dir").unwrap_err();

    assert!(error.to_string().contains("--dir requires a path"));
  }

  #[test]
  fn release_binary_path_uses_release_directory_and_executable_name() {
    assert_eq!(
      release_binary_path("cadderd"),
      PathBuf::from("target")
        .join("release")
        .join(exe_name("cadderd"))
    );
  }

  #[test]
  fn verify_dist_rejects_missing_portable_files() {
    let dir = unique_temp_dir("missing-portable-files");
    fs::create_dir_all(&dir).unwrap();

    let error = verify_dist(&dir).unwrap_err();

    assert!(error.to_string().contains("portable binary missing"));
    fs::remove_dir_all(&dir).unwrap();
  }

  #[test]
  fn verify_dist_rejects_missing_sample_config_after_binaries_exist() {
    let dir = unique_temp_dir("missing-sample-config");
    fs::create_dir_all(&dir).unwrap();
    for binary in portable_binaries() {
      fs::write(dir.join(exe_name(binary)), b"not executable").unwrap();
    }

    let error = verify_dist(&dir).unwrap_err();

    assert!(
      error
        .to_string()
        .contains("portable sample configuration missing")
    );
    fs::remove_dir_all(&dir).unwrap();
  }

  fn unique_temp_dir(name: &str) -> PathBuf {
    let unique = std::time::SystemTime::now()
      .duration_since(std::time::UNIX_EPOCH)
      .unwrap()
      .as_nanos();
    env::temp_dir().join(format!(
      "cadder-xtask-{name}-{}-{unique}",
      std::process::id()
    ))
  }
}
