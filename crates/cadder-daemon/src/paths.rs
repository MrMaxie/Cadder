use anyhow::{Context, Result, anyhow};
use directories::ProjectDirs;
use fs4::FileExt;
use sha2::{Digest, Sha256};
use std::{
  env,
  fs::{self, File, OpenOptions},
  path::{Path, PathBuf},
};

#[derive(Debug, Clone)]
pub struct RuntimePaths {
  runtime_dir: PathBuf,
  socket_name: String,
}

impl RuntimePaths {
  pub fn resolve(override_dir: Option<PathBuf>) -> Result<Self> {
    let runtime_dir = match override_dir {
      Some(path) => path,
      None if env::var_os("CADDER_RUNTIME_DIR").is_some() => {
        PathBuf::from(env::var_os("CADDER_RUNTIME_DIR").expect("checked"))
      }
      None => {
        let dirs = ProjectDirs::from("dev", "Cadder", "Cadder")
          .ok_or_else(|| anyhow!("could not resolve per-user project directories"))?;
        dirs
          .runtime_dir()
          .map(Path::to_path_buf)
          .unwrap_or_else(|| dirs.data_local_dir().join("run"))
      }
    };

    let mut hasher = Sha256::new();
    hasher.update(runtime_dir.to_string_lossy().as_bytes());
    let suffix = hex::encode(&hasher.finalize()[..8]);
    let socket_name = format!("cadder-{suffix}.sock");

    Ok(Self {
      runtime_dir,
      socket_name,
    })
  }

  pub fn ensure_dirs(&self) -> Result<()> {
    fs::create_dir_all(&self.runtime_dir)
      .with_context(|| format!("create runtime directory {}", self.runtime_dir.display()))
  }

  pub fn runtime_dir(&self) -> &Path {
    &self.runtime_dir
  }

  pub fn socket_name(&self) -> &str {
    &self.socket_name
  }

  pub fn lock_path(&self) -> PathBuf {
    self.runtime_dir.join("cadder.lock")
  }

  pub fn metadata_path(&self) -> PathBuf {
    self.runtime_dir.join("daemon.json")
  }

  pub fn effective_config_path(&self) -> PathBuf {
    self.runtime_dir.join("effective-caddy.json")
  }
}

#[derive(Debug)]
pub struct DaemonLock {
  _file: File,
}

impl DaemonLock {
  pub fn acquire(path: PathBuf) -> Result<Self> {
    if let Some(parent) = path.parent() {
      fs::create_dir_all(parent)
        .with_context(|| format!("create lock directory {}", parent.display()))?;
    }

    let file = OpenOptions::new()
      .read(true)
      .write(true)
      .create(true)
      .truncate(false)
      .open(&path)
      .with_context(|| format!("open daemon lock {}", path.display()))?;
    FileExt::try_lock(&file).with_context(|| format!("acquire daemon lock {}", path.display()))?;
    Ok(Self { _file: file })
  }
}

#[cfg(test)]
mod tests {
  use super::*;

  #[test]
  fn lock_rejects_second_owner() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("daemon.lock");
    let _first = DaemonLock::acquire(path.clone()).unwrap();
    assert!(DaemonLock::acquire(path).is_err());
  }

  #[test]
  fn resolve_override_derives_stable_socket_and_runtime_paths() {
    let dir = tempfile::tempdir().unwrap();

    let first = RuntimePaths::resolve(Some(dir.path().to_path_buf())).unwrap();
    let second = RuntimePaths::resolve(Some(dir.path().to_path_buf())).unwrap();

    assert_eq!(first.runtime_dir(), dir.path());
    assert_eq!(first.socket_name(), second.socket_name());
    assert!(first.socket_name().starts_with("cadder-"));
    assert_eq!(first.lock_path(), dir.path().join("cadder.lock"));
    assert_eq!(first.metadata_path(), dir.path().join("daemon.json"));
    assert_eq!(
      first.effective_config_path(),
      dir.path().join("effective-caddy.json")
    );
  }

  #[test]
  fn ensure_dirs_creates_runtime_directory() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().join("nested").join("runtime");
    let paths = RuntimePaths::resolve(Some(runtime_dir.clone())).unwrap();

    paths.ensure_dirs().unwrap();

    assert!(runtime_dir.is_dir());
  }
}
