mod model;

use anyhow::{Context, Result};
use cadder_daemon::{
  CadderClient, DaemonLaunchOptions, RuntimePaths, ensure_daemon_running_with_options,
};
use cadder_protocol::{
  BasicResponse, GuiStateSnapshot, LogEntry, LogSeverity, LogStreamIdentity, LogStreamStatus,
  QueryLogsRequest, QueryLogsResponse, QueryStateRequest, QueryStateResponse,
  SetDomainEnabledRequest, SetEntrypointEnabledRequest, ShutdownDaemonRequest, message_types,
  new_request_id,
};
use clap::Parser;
use crossterm::event::{self, Event, KeyCode, KeyEventKind};
use model::{TuiModel, View};
use ratatui::{
  DefaultTerminal, Frame,
  layout::{Constraint, Layout},
  style::{Color, Modifier, Style, Stylize},
  text::Line,
  widgets::{Block, Borders, Cell, List, ListItem, Paragraph, Row, Table, Tabs, Wrap},
};
use std::{
  path::PathBuf,
  time::{Duration, Instant},
};
use tokio::sync::mpsc;

#[derive(Debug, Parser)]
#[command(
  name = "cadder-tui",
  version,
  about = "Cadder terminal UI for daemon state, domains, logs, and diagnostics",
  long_about = "Opens the Cadder terminal UI. It connects to cadderd, starts it by default, and displays entrypoints, domains, logs, diagnostics, activation controls, and daemon shutdown."
)]
struct Args {
  #[arg(
    long,
    help = "Override the Cadder runtime directory used to find daemon IPC and state"
  )]
  runtime_dir: Option<PathBuf>,

  #[arg(long, help = "Path to a cadderd executable to start when needed")]
  daemon_path: Option<PathBuf>,

  #[arg(
    long,
    help = "Command or path passed to cadderd for starting the real Caddy binary"
  )]
  real_caddy_command: Option<String>,

  #[arg(
    long,
    help = "Connect to an existing daemon instead of starting cadderd"
  )]
  no_start: bool,
}

#[tokio::main]
async fn main() -> Result<()> {
  tracing_subscriber::fmt::init();
  let args = Args::parse();
  let paths = RuntimePaths::resolve(args.runtime_dir)?;
  if !args.no_start {
    ensure_daemon_running_with_options(
      &paths,
      DaemonLaunchOptions {
        explicit_daemon: args.daemon_path,
        real_caddy_command: args.real_caddy_command,
        shim_path: None,
      },
    )
    .await?;
  }
  let client = CadderClient::new(paths);
  let (responses_tx, responses_rx) = mpsc::unbounded_channel();
  let mut app = TuiApp {
    client,
    model: TuiModel::default(),
    message: String::new(),
    responses_tx,
    responses_rx,
    state_request_in_flight: false,
    toggle_request_in_flight: false,
    shutdown_request_in_flight: false,
    last_log_refresh: Instant::now(),
    log_request_serial: 0,
  };
  app.refresh().await?;

  let terminal = ratatui::init();
  let result = app.run(terminal).await;
  ratatui::restore();
  result
}

struct TuiApp {
  client: CadderClient,
  model: TuiModel,
  message: String,
  responses_tx: mpsc::UnboundedSender<AppResponse>,
  responses_rx: mpsc::UnboundedReceiver<AppResponse>,
  state_request_in_flight: bool,
  toggle_request_in_flight: bool,
  shutdown_request_in_flight: bool,
  last_log_refresh: Instant,
  log_request_serial: u64,
}

#[derive(Debug)]
enum AppResponse {
  State(Result<QueryStateResponse, String>),
  Toggle(Result<BasicResponse, String>),
  Logs {
    serial: u64,
    stream: LogStreamIdentity,
    minimum_severity: Option<LogSeverity>,
    result: Result<QueryLogsResponse, String>,
  },
  Shutdown(Result<BasicResponse, String>),
}

impl TuiApp {
  async fn run(&mut self, mut terminal: DefaultTerminal) -> Result<()> {
    loop {
      self.drain_responses();
      self.maybe_tail_logs();
      terminal.draw(|frame| self.draw(frame))?;
      if event::poll(Duration::from_millis(200))?
        && let Event::Key(key) = event::read()?
      {
        if key.kind != KeyEventKind::Press {
          continue;
        }
        if self.model.search_mode {
          match key.code {
            KeyCode::Esc => {
              self.model.search_mode = false;
              self.model.search.clear();
            }
            KeyCode::Backspace => {
              self.model.search.pop();
            }
            KeyCode::Char(ch) => self.model.search.push(ch),
            _ => {}
          }
          continue;
        }
        match key.code {
          KeyCode::Char('q') => return Ok(()),
          KeyCode::Char('r') => self.start_state_refresh(),
          KeyCode::Tab => self.model.next_view(),
          KeyCode::BackTab => self.model.previous_view(),
          KeyCode::Down => self.model.move_selection(1),
          KeyCode::Up => self.model.move_selection(-1),
          KeyCode::Char('/') => self.model.search_mode = true,
          KeyCode::Char(' ') => self.start_toggle_selected(),
          KeyCode::Char('p') if self.model.view == View::Logs => {
            self.model.logs.paused = !self.model.logs.paused;
            self.message = if self.model.logs.paused {
              "Log tailing paused.".to_string()
            } else {
              "Log tailing resumed.".to_string()
            };
          }
          KeyCode::Char('0') if self.model.view == View::Logs => {
            self.set_log_severity(None);
          }
          KeyCode::Char('i') if self.model.view == View::Logs => {
            self.set_log_severity(Some(LogSeverity::Info));
          }
          KeyCode::Char('w') if self.model.view == View::Logs => {
            self.set_log_severity(Some(LogSeverity::Warn));
          }
          KeyCode::Char('e') if self.model.view == View::Logs => {
            self.set_log_severity(Some(LogSeverity::Error));
          }
          KeyCode::Char('x') if self.model.view == View::Logs => self.export_logs()?,
          KeyCode::Char('d') => self.start_shutdown_daemon(),
          KeyCode::Enter if self.model.view == View::Domains => self.open_selected_domain_logs(),
          KeyCode::Char('l') if self.model.view == View::Domains => {
            self.open_selected_domain_logs()
          }
          KeyCode::Enter if self.model.view == View::Logs => self.start_log_refresh(),
          _ => {}
        }
      }
    }
  }

  fn drain_responses(&mut self) {
    while let Ok(response) = self.responses_rx.try_recv() {
      match response {
        AppResponse::State(result) => {
          self.state_request_in_flight = false;
          match result {
            Ok(response) => self.apply_state_response(response),
            Err(error) => self.message = format!("State refresh failed: {error}"),
          }
        }
        AppResponse::Toggle(result) => {
          self.toggle_request_in_flight = false;
          match result {
            Ok(response) => {
              self.message = response.message;
              self.start_state_refresh();
            }
            Err(error) => self.message = format!("Toggle failed: {error}"),
          }
        }
        AppResponse::Logs {
          serial,
          stream,
          minimum_severity,
          result,
        } => {
          let current_matches = serial == self.log_request_serial
            && self
              .model
              .logs
              .active_stream()
              .is_some_and(|active| active == stream)
            && self.model.logs.minimum_severity == minimum_severity;
          if !current_matches {
            continue;
          }
          match result {
            Ok(response) => {
              self.message = response.message.clone();
              self.model.logs.apply_response(response);
            }
            Err(error) => {
              self.message = format!("Log refresh failed: {error}");
              self.model.logs.apply_read_error(error);
            }
          }
        }
        AppResponse::Shutdown(result) => {
          self.shutdown_request_in_flight = false;
          match result {
            Ok(response) => self.message = response.message,
            Err(error) => self.message = format!("Shutdown failed: {error}"),
          }
        }
      }
    }
  }

  fn maybe_tail_logs(&mut self) {
    if self.model.view != View::Logs
      || self.model.logs.paused
      || self.model.logs.request_in_flight
      || self.model.logs.target.is_none()
      || self.last_log_refresh.elapsed() < Duration::from_millis(750)
    {
      return;
    }
    self.start_log_refresh();
  }

  fn apply_state_response(&mut self, response: QueryStateResponse) {
    if let Some(snapshot) = response.snapshot {
      self.model.set_snapshot(snapshot);
      self.message = response.message;
    } else {
      self.message = "Daemon returned no state snapshot.".to_string();
    }
  }

  async fn refresh(&mut self) -> Result<()> {
    let response: QueryStateResponse = self
      .client
      .request(
        message_types::QUERY_STATE_REQUEST,
        message_types::QUERY_STATE_RESPONSE,
        &QueryStateRequest {
          request_id: new_request_id("tui-state"),
        },
      )
      .await?;
    self.apply_state_response(response);
    Ok(())
  }

  fn start_state_refresh(&mut self) {
    if self.state_request_in_flight {
      return;
    }
    self.state_request_in_flight = true;
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    tokio::spawn(async move {
      let result = client
        .request(
          message_types::QUERY_STATE_REQUEST,
          message_types::QUERY_STATE_RESPONSE,
          &QueryStateRequest {
            request_id: new_request_id("tui-state"),
          },
        )
        .await
        .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::State(result));
    });
  }

  fn start_toggle_selected(&mut self) {
    if self.toggle_request_in_flight {
      return;
    }
    let request = match self.model.view {
      View::Entrypoints => self.model.selected_entrypoint().map(|entrypoint| {
        ToggleRequest::Entrypoint(SetEntrypointEnabledRequest {
          request_id: new_request_id("tui-entrypoint-toggle"),
          registration_id: entrypoint.registration_id.clone(),
          shim_session_nonce: None,
          enabled: !entrypoint.activation_state.is_enabled(),
        })
      }),
      View::Domains => self
        .model
        .selected_domain()
        .map(|(registration_id, domain)| {
          ToggleRequest::Domain(SetDomainEnabledRequest {
            request_id: new_request_id("tui-domain-toggle"),
            registration_id,
            domain_key: domain.name.canonical,
            enabled: !domain.activation_state.is_enabled(),
          })
        }),
      _ => None,
    };

    let Some(request) = request else {
      return;
    };
    self.toggle_request_in_flight = true;
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    tokio::spawn(async move {
      let result = match request {
        ToggleRequest::Entrypoint(request) => {
          client
            .request(
              message_types::SET_ENTRYPOINT_ENABLED_REQUEST,
              message_types::SET_ENTRYPOINT_ENABLED_RESPONSE,
              &request,
            )
            .await
        }
        ToggleRequest::Domain(request) => {
          client
            .request(
              message_types::SET_DOMAIN_ENABLED_REQUEST,
              message_types::SET_DOMAIN_ENABLED_RESPONSE,
              &request,
            )
            .await
        }
      }
      .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::Toggle(result));
    });
  }

  fn open_selected_domain_logs(&mut self) {
    if self.model.open_selected_domain_logs() {
      self.message = "Loading domain logs.".to_string();
      self.start_log_refresh();
    }
  }

  fn set_log_severity(&mut self, severity: Option<LogSeverity>) {
    if self.model.logs.minimum_severity == severity {
      return;
    }
    self.model.logs.reset_for_filter(severity);
    self.message = match severity {
      Some(severity) => format!("Log severity filter set to {severity:?}."),
      None => "Log severity filter cleared.".to_string(),
    };
    self.start_log_refresh();
  }

  fn start_log_refresh(&mut self) {
    if self.model.logs.request_in_flight {
      return;
    }
    let Some(stream) = self.model.logs.active_stream() else {
      self.message = "Select a domain and press Enter or l to view logs.".to_string();
      return;
    };
    let cursor = self.model.logs.next_cursor.clone();
    let minimum_severity = self.model.logs.minimum_severity;
    self.model.logs.mark_loading();
    self.last_log_refresh = Instant::now();
    self.log_request_serial += 1;
    let serial = self.log_request_serial;
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    let response_stream = stream.clone();
    tokio::spawn(async move {
      let result = client
        .request(
          message_types::QUERY_LOGS_REQUEST,
          message_types::QUERY_LOGS_RESPONSE,
          &QueryLogsRequest {
            request_id: new_request_id("tui-logs"),
            stream,
            limit: Some(100),
            cursor,
            minimum_severity,
          },
        )
        .await
        .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::Logs {
        serial,
        stream: response_stream,
        minimum_severity,
        result,
      });
    });
  }

  fn start_shutdown_daemon(&mut self) {
    if self.shutdown_request_in_flight {
      return;
    }
    self.shutdown_request_in_flight = true;
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    tokio::spawn(async move {
      let result = client
        .request(
          message_types::SHUTDOWN_DAEMON_REQUEST,
          message_types::SHUTDOWN_DAEMON_RESPONSE,
          &ShutdownDaemonRequest {
            request_id: new_request_id("tui-shutdown"),
          },
        )
        .await
        .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::Shutdown(result));
    });
  }

  fn export_logs(&mut self) -> Result<()> {
    let Some(target) = self.model.logs.target.as_ref() else {
      self.message = "No domain log stream is open.".to_string();
      return Ok(());
    };
    if self.model.logs.entries.is_empty() {
      self.message = "No log entries to export.".to_string();
      return Ok(());
    }
    let timestamp = chrono::Utc::now().format("%Y%m%dT%H%M%SZ");
    let filename = format!(
      "cadder-logs-{}-{timestamp}.txt",
      safe_filename(&target.domain_name)
    );
    let output_path = std::env::current_dir()
      .context("resolve current directory")?
      .join(filename);
    std::fs::write(
      &output_path,
      format_log_excerpt(target, &self.model.logs.entries),
    )
    .with_context(|| format!("write {}", output_path.display()))?;
    self.message = format!("Exported {}", output_path.display());
    Ok(())
  }
}

enum ToggleRequest {
  Entrypoint(SetEntrypointEnabledRequest),
  Domain(SetDomainEnabledRequest),
}

fn safe_filename(input: &str) -> String {
  let mut output = String::with_capacity(input.len());
  for ch in input.chars() {
    if ch.is_ascii_alphanumeric() || matches!(ch, '.' | '-' | '_') {
      output.push(ch);
    } else {
      output.push('-');
    }
  }
  output.trim_matches('-').to_string()
}

fn format_log_excerpt(target: &model::LogTarget, entries: &[LogEntry]) -> String {
  let mut lines = vec![
    format!("Cadder diagnostic log excerpt for {}", target.domain_name),
    format!("Registration: {}", target.registration_id),
    format!("Stream: {}", target.stream.stream_id),
    "Messages are daemon-redacted before export.".to_string(),
    String::new(),
  ];
  lines.extend(entries.iter().map(|entry| {
    format!(
      "{} {:?} domain={} source={:?} {}",
      entry.timestamp_utc.to_rfc3339(),
      entry.severity,
      entry
        .domain_key
        .as_deref()
        .unwrap_or(target.domain_name.as_str()),
      entry.source_registration_id,
      entry.raw_message
    )
  }));
  lines.push(String::new());
  lines.join("\n")
}

impl TuiApp {
  fn draw(&self, frame: &mut Frame<'_>) {
    let [tabs_area, body_area, status_area] = Layout::vertical([
      Constraint::Length(3),
      Constraint::Min(3),
      Constraint::Length(2),
    ])
    .areas(frame.area());

    let tabs = Tabs::new(
      View::ALL
        .iter()
        .map(|view| view.title())
        .collect::<Vec<_>>(),
    )
    .select(self.model.view.index())
    .block(Block::new().borders(Borders::BOTTOM).title("Cadder"))
    .highlight_style(Style::new().fg(Color::Cyan).add_modifier(Modifier::BOLD));
    frame.render_widget(tabs, tabs_area);

    match self.model.view {
      View::Overview => self.draw_overview(frame, body_area),
      View::Entrypoints => self.draw_entrypoints(frame, body_area),
      View::Domains => self.draw_domains(frame, body_area),
      View::Logs => self.draw_logs(frame, body_area),
      View::Diagnostics => self.draw_diagnostics(frame, body_area),
    }

    let search = if self.model.search_mode {
      format!(" search: {}", self.model.search)
    } else if self.model.search.is_empty() {
      String::new()
    } else {
      format!(" filter: {}", self.model.search)
    };
    let status = format!(
      "{}{}{}  |  Tab view  r refresh  space toggle  / search  p pause logs  i/w/e/0 severity  x export  d shutdown  q quit",
      self.message,
      search,
      self
        .model
        .logs
        .minimum_severity
        .map(|severity| format!(" severity: {severity:?}"))
        .unwrap_or_default()
    );
    frame.render_widget(Paragraph::new(status), status_area);
  }

  fn draw_overview(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let summary = self.model.summary();
    let lines = vec![
      Line::from(vec!["Runtime: ".bold(), summary.runtime.into()]),
      Line::from(vec!["Config: ".bold(), summary.config.into()]),
      Line::from(vec![
        "Entrypoints: ".bold(),
        summary.entrypoints.to_string().into(),
      ]),
      Line::from(vec!["Domains: ".bold(), summary.domains.to_string().into()]),
      Line::from(vec![
        "Active domains: ".bold(),
        summary.active_domains.to_string().into(),
      ]),
    ];
    frame.render_widget(
      Paragraph::new(lines)
        .block(Block::bordered().title("Overview"))
        .wrap(Wrap { trim: true }),
      area,
    );
  }

  fn draw_entrypoints(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let rows = self
      .model
      .filtered_registrations()
      .into_iter()
      .map(|registration| {
        Row::new(vec![
          Cell::from(registration.registration_id.clone()),
          Cell::from(format!("{:?}", registration.activation_state)),
          Cell::from(registration.source_working_directory.raw.clone()),
          Cell::from(registration.registered_domains.len().to_string()),
        ])
      });
    let table = Table::new(
      rows,
      [
        Constraint::Length(24),
        Constraint::Length(12),
        Constraint::Percentage(60),
        Constraint::Length(8),
      ],
    )
    .header(Row::new(["ID", "State", "Source", "Domains"]).style(Style::new().bold()))
    .block(Block::bordered().title("Entrypoints"));
    frame.render_widget(table, area);
  }

  fn draw_domains(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let rows = self
      .model
      .filtered_domains()
      .into_iter()
      .map(|(registration, domain)| {
        Row::new(vec![
          Cell::from(registration.registration_id.clone()),
          Cell::from(domain.name.canonical.clone()),
          Cell::from(format!("{:?}", domain.activation_state)),
        ])
      });
    let table = Table::new(
      rows,
      [
        Constraint::Length(24),
        Constraint::Percentage(60),
        Constraint::Length(12),
      ],
    )
    .header(Row::new(["Entrypoint", "Domain", "State"]).style(Style::new().bold()))
    .block(Block::bordered().title("Domains"));
    frame.render_widget(table, area);
  }

  fn draw_logs(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let logs = &self.model.logs;
    let target = logs
      .target
      .as_ref()
      .map(|target| target.domain_name.as_str())
      .unwrap_or("no domain selected");
    let mode = if logs.paused { "paused" } else { "tailing" };
    let title = format!(
      "Logs: {target} | {mode} | {:?}{}",
      logs.status,
      logs
        .minimum_severity
        .map(|severity| format!(" | min {severity:?}"))
        .unwrap_or_default()
    );
    let mut items = Vec::new();
    items.extend(self.log_state_lines().into_iter().map(ListItem::new));
    items.extend(logs.entries.iter().map(|entry| {
      ListItem::new(format!(
        "{} {:?} {}",
        entry.timestamp_utc.format("%H:%M:%S"),
        entry.severity,
        entry.raw_message
      ))
    }));
    frame.render_widget(List::new(items).block(Block::bordered().title(title)), area);
  }

  fn log_state_lines(&self) -> Vec<String> {
    let logs = &self.model.logs;
    let mut lines = Vec::new();
    if logs.target.is_none() {
      lines.push("Select a domain row and press Enter or l to view logs.".to_string());
      return lines;
    }
    if logs.loading {
      lines.push("Loading log entries...".to_string());
    }
    if logs.paused {
      lines.push("Auto-scroll paused. Press p to resume or Enter to refresh.".to_string());
    }
    if let Some(error) = &logs.read_error {
      lines.push(format!("Read error: {error}"));
    }
    match logs.status {
      LogStreamStatus::Empty if !logs.loading => {
        lines.push("No log entries for this domain.".to_string())
      }
      LogStreamStatus::Stale => {
        lines.push("The stream is stale because the domain is not active.".to_string())
      }
      LogStreamStatus::Removed => {
        lines.push("The domain was removed; retained entries may still be shown.".to_string())
      }
      LogStreamStatus::ReadError => {}
      _ => {}
    }
    if logs.has_gap {
      lines.push("Some entries were skipped before this page.".to_string());
    }
    if logs.has_more_before {
      lines.push("Older entries exist before this excerpt.".to_string());
    }
    if logs.truncated_by_retention {
      lines.push("Older entries were truncated by daemon retention.".to_string());
    }
    lines
  }

  fn draw_diagnostics(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let snapshot = self.model.snapshot();
    let mut lines = Vec::new();
    lines.extend(snapshot.config.diagnostics.iter().map(|diagnostic| {
      ListItem::new(format!("config:{} {}", diagnostic.code, diagnostic.message))
    }));
    lines.extend(snapshot.runtime.diagnostics.iter().map(|diagnostic| {
      ListItem::new(format!(
        "runtime:{} {}",
        diagnostic.code, diagnostic.message
      ))
    }));
    if lines.is_empty() {
      lines.push(ListItem::new("No diagnostics."));
    }
    frame.render_widget(
      List::new(lines).block(Block::bordered().title("Diagnostics")),
      area,
    );
  }
}

#[allow(dead_code)]
fn _assert_snapshot_send_sync(_: &GuiStateSnapshot) {}

#[cfg(test)]
mod tests {
  use super::*;
  use cadder_protocol::{
    ActivationState, ConfigApplyStatus, ConfigDiagnostic, ConfigState, EntrypointInstanceIdentity,
    EntrypointRegistration, LogAttributionKind, LogEntryKind, OwnerProcessIdentity,
    RegisteredDomain, RuntimeDiagnostic, RuntimeState, RuntimeStatus, SourcePath,
  };
  use chrono::Utc;
  use clap::CommandFactory;
  use ratatui::{Terminal, backend::TestBackend};

  fn test_app() -> TuiApp {
    let paths = RuntimePaths::resolve(Some(
      std::env::temp_dir().join(format!("cadder-tui-test-{}", std::process::id())),
    ))
    .unwrap();
    let (responses_tx, responses_rx) = mpsc::unbounded_channel();
    let mut model = TuiModel::default();
    model.set_snapshot(snapshot());
    TuiApp {
      client: CadderClient::new(paths),
      model,
      message: "ready".to_string(),
      responses_tx,
      responses_rx,
      state_request_in_flight: false,
      toggle_request_in_flight: false,
      shutdown_request_in_flight: false,
      last_log_refresh: Instant::now(),
      log_request_serial: 0,
    }
  }

  fn snapshot() -> GuiStateSnapshot {
    let mut snapshot = GuiStateSnapshot {
      captured_at_utc: Utc::now(),
      registrations: vec![
        registration(
          "shim-1",
          "D:/work/app",
          vec![
            RegisteredDomain::active("app.localhost"),
            RegisteredDomain {
              activation_state: ActivationState::Inactive,
              ..RegisteredDomain::active("api.localhost")
            },
          ],
        ),
        registration(
          "shim-2",
          "D:/work/admin",
          vec![RegisteredDomain::active("admin.localhost")],
        ),
      ],
      runtime: RuntimeState {
        status: RuntimeStatus::Unhealthy,
        binary_path: Some("D:/tools/caddy.exe".to_string()),
        version: Some("2.10.0".to_string()),
        process_id: Some(4242),
        admin_endpoint: Some("localhost:2019".to_string()),
        diagnostics: vec![RuntimeDiagnostic {
          code: "runtime-exited".to_string(),
          message: "process exited".to_string(),
          operation: Some("run".to_string()),
        }],
      },
      config: ConfigState {
        status: ConfigApplyStatus::Failed,
        last_attempted_at_utc: Some(Utc::now()),
        last_successful_reload_at_utc: None,
        effective_config_hash: Some("hash".to_string()),
        diagnostics: vec![ConfigDiagnostic {
          code: "reload-failed".to_string(),
          message: "reload failed".to_string(),
          domain_key: Some("app.localhost".to_string()),
          source_config_paths: vec!["D:/work/app/Caddyfile".to_string()],
        }],
      },
    };
    snapshot.registrations[0].shim_run = Some(cadder_protocol::ShimRunMetadata {
      adapter: Some("caddyfile".to_string()),
      raw_arguments: vec![
        "run".to_string(),
        "--adapter".to_string(),
        "caddyfile".to_string(),
      ],
      command_line: "run --adapter caddyfile".to_string(),
    });
    snapshot
  }

  fn registration(
    registration_id: &str,
    working_directory: &str,
    registered_domains: Vec<RegisteredDomain>,
  ) -> EntrypointRegistration {
    let now = Utc::now();
    let identity = EntrypointInstanceIdentity {
      instance_id: registration_id.to_string(),
      started_at_utc: now,
      shim_session_nonce: format!("{registration_id}-nonce"),
    };
    EntrypointRegistration {
      registration_id: registration_id.to_string(),
      entrypoint_instance: identity.clone(),
      source_working_directory: SourcePath::new(working_directory, None),
      source_config_path: SourcePath::new(format!("{working_directory}/Caddyfile"), None),
      registered_domains,
      activation_state: ActivationState::Active,
      owner_process: OwnerProcessIdentity {
        process_id: 42,
        process_start_time_utc: now,
        shim_session_nonce: identity.shim_session_nonce,
        executable_path: Some("D:/bin/caddy.exe".to_string()),
      },
      log_stream: LogStreamIdentity::entrypoint(registration_id),
      shim_run: None,
      created_at_utc: now,
      last_heartbeat_utc: now,
    }
  }

  fn log_entry(sequence_number: u64, severity: LogSeverity, message: &str) -> LogEntry {
    LogEntry {
      sequence_number,
      timestamp_utc: Utc::now(),
      severity,
      stream: LogStreamIdentity::domain("app.localhost"),
      attribution_kind: LogAttributionKind::Domain,
      entry_kind: LogEntryKind::Normal,
      raw_message: message.to_string(),
      domain_key: Some("app.localhost".to_string()),
      source_registration_id: Some("shim-1".to_string()),
      source_instance_id: Some("shim-1".to_string()),
      operation: Some("run".to_string()),
    }
  }

  fn buffer_text(terminal: &Terminal<TestBackend>) -> String {
    terminal
      .backend()
      .buffer()
      .content()
      .iter()
      .map(|cell| cell.symbol())
      .collect()
  }

  fn render(app: &TuiApp) -> String {
    let backend = TestBackend::new(120, 32);
    let mut terminal = Terminal::new(backend).unwrap();
    terminal.draw(|frame| app.draw(frame)).unwrap();
    buffer_text(&terminal)
  }

  async fn drain_until<F>(app: &mut TuiApp, mut done: F)
  where
    F: FnMut(&TuiApp) -> bool,
  {
    for _ in 0..50 {
      app.drain_responses();
      if done(app) {
        return;
      }
      tokio::time::sleep(Duration::from_millis(10)).await;
    }
    panic!("timed out waiting for async TUI response");
  }

  #[test]
  fn command_metadata_matches_release_identity() {
    let command = Args::command();

    assert_eq!(command.get_name(), "cadder-tui");
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
  fn long_help_describes_tui_launch_options() {
    let help = Args::command().render_long_help().to_string();

    assert!(
      help.contains("Override the Cadder runtime directory"),
      "long help output should describe --runtime-dir: {help}"
    );
    assert!(
      help.contains("Path to a cadderd executable"),
      "long help output should describe --daemon-path: {help}"
    );
    assert!(
      help.contains("Connect to an existing daemon"),
      "long help output should describe --no-start: {help}"
    );
  }

  #[test]
  fn draw_renders_each_view_with_snapshot_content() {
    for view in View::ALL {
      let mut app = test_app();
      app.model.view = view;
      if view == View::Logs {
        app.model.logs.reset_for_target(model::LogTarget {
          registration_id: "shim-1".to_string(),
          domain_name: "app.localhost".to_string(),
          stream: LogStreamIdentity::domain("app.localhost"),
        });
        app.model.logs.paused = true;
        app.model.logs.status = LogStreamStatus::Stale;
        app.model.logs.entries = vec![log_entry(1, LogSeverity::Warn, "upstream warning")];
        app.model.logs.has_gap = true;
        app.model.logs.has_more_before = true;
        app.model.logs.truncated_by_retention = true;
      }

      let text = render(&app);

      assert!(
        text.contains(view.title()),
        "rendered buffer should include the active view title {view:?}: {text}"
      );
      assert!(
        text.contains("Cadder"),
        "rendered buffer should include the app title: {text}"
      );
    }
  }

  #[test]
  fn draw_includes_search_and_severity_status_context() {
    let mut app = test_app();
    app.model.view = View::Logs;
    app.model.search_mode = true;
    app.model.search = "api".to_string();
    app.model.logs.minimum_severity = Some(LogSeverity::Error);

    let text = render(&app);

    assert!(text.contains("search: api"));
    assert!(text.contains("severity: Error"));
    assert!(text.contains("no domain selected"));
  }

  #[test]
  fn log_state_lines_cover_empty_paused_error_and_retention_messages() {
    let mut app = test_app();
    app.model.view = View::Logs;

    assert_eq!(
      app.log_state_lines(),
      vec!["Select a domain row and press Enter or l to view logs.".to_string()]
    );

    app.model.logs.reset_for_target(model::LogTarget {
      registration_id: "shim-1".to_string(),
      domain_name: "app.localhost".to_string(),
      stream: LogStreamIdentity::domain("app.localhost"),
    });
    app.model.logs.loading = false;
    app.model.logs.paused = true;
    app.model.logs.read_error = Some("socket closed".to_string());
    app.model.logs.status = LogStreamStatus::Removed;
    app.model.logs.has_gap = true;
    app.model.logs.has_more_before = true;
    app.model.logs.truncated_by_retention = true;

    let lines = app.log_state_lines();

    assert!(lines.iter().any(|line| line.contains("Auto-scroll paused")));
    assert!(lines.iter().any(|line| line.contains("Read error")));
    assert!(lines.iter().any(|line| line.contains("domain was removed")));
    assert!(
      lines
        .iter()
        .any(|line| line.contains("entries were skipped"))
    );
    assert!(
      lines
        .iter()
        .any(|line| line.contains("Older entries exist"))
    );
    assert!(
      lines
        .iter()
        .any(|line| line.contains("truncated by daemon retention"))
    );
  }

  #[test]
  fn drain_responses_applies_matching_results_and_ignores_stale_logs() {
    let mut app = test_app();
    app.log_request_serial = 2;
    app.model.open_selected_domain_logs();
    app.model.logs.minimum_severity = Some(LogSeverity::Warn);
    let stream = app.model.logs.active_stream().unwrap();

    app
      .responses_tx
      .send(AppResponse::Logs {
        serial: 1,
        stream: stream.clone(),
        minimum_severity: Some(LogSeverity::Warn),
        result: Ok(QueryLogsResponse {
          request_id: "old".to_string(),
          accepted: true,
          message: "old".to_string(),
          stream: stream.clone(),
          stream_status: LogStreamStatus::Active,
          entries: vec![log_entry(1, LogSeverity::Info, "stale")],
          next_cursor: Some("seq:1".to_string()),
          has_gap: false,
          has_more_before: false,
          truncated_by_retention: false,
        }),
      })
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::Logs {
        serial: 2,
        stream: stream.clone(),
        minimum_severity: Some(LogSeverity::Warn),
        result: Ok(QueryLogsResponse {
          request_id: "current".to_string(),
          accepted: true,
          message: "loaded".to_string(),
          stream: stream.clone(),
          stream_status: LogStreamStatus::Active,
          entries: vec![log_entry(2, LogSeverity::Warn, "current")],
          next_cursor: Some("seq:2".to_string()),
          has_gap: true,
          has_more_before: false,
          truncated_by_retention: false,
        }),
      })
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::Shutdown(Err("denied".to_string())))
      .unwrap();

    app.drain_responses();

    assert_eq!(app.model.logs.entries.len(), 1);
    assert_eq!(app.model.logs.entries[0].raw_message, "current");
    assert_eq!(app.model.logs.next_cursor.as_deref(), Some("seq:2"));
    assert!(app.model.logs.has_gap);
    assert_eq!(app.message, "Shutdown failed: denied");
    assert!(!app.shutdown_request_in_flight);
  }

  #[test]
  fn drain_responses_reports_state_toggle_and_log_errors() {
    let mut app = test_app();
    app.state_request_in_flight = true;
    app.toggle_request_in_flight = true;
    app.log_request_serial = 3;
    app.model.open_selected_domain_logs();
    let stream = app.model.logs.active_stream().unwrap();

    app
      .responses_tx
      .send(AppResponse::State(Ok(QueryStateResponse {
        request_id: "state".to_string(),
        accepted: true,
        message: "no snapshot".to_string(),
        snapshot: None,
      })))
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::Toggle(Err("rejected".to_string())))
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::Logs {
        serial: 3,
        stream,
        minimum_severity: None,
        result: Err("read failed".to_string()),
      })
      .unwrap();

    app.drain_responses();

    assert_eq!(app.message, "Log refresh failed: read failed");
    assert_eq!(app.model.logs.status, LogStreamStatus::ReadError);
    assert_eq!(app.model.logs.read_error.as_deref(), Some("read failed"));
    assert!(!app.state_request_in_flight);
    assert!(!app.toggle_request_in_flight);
  }

  #[tokio::test]
  async fn start_state_refresh_reports_connection_error() {
    let mut app = test_app();

    app.start_state_refresh();
    assert!(app.state_request_in_flight);
    app.start_state_refresh();

    drain_until(&mut app, |app| !app.state_request_in_flight).await;

    assert!(app.message.starts_with("State refresh failed:"));
  }

  #[tokio::test]
  async fn start_toggle_selected_reports_entrypoint_connection_error() {
    let mut app = test_app();
    app.model.view = View::Entrypoints;

    app.start_toggle_selected();
    assert!(app.toggle_request_in_flight);
    app.start_toggle_selected();

    drain_until(&mut app, |app| !app.toggle_request_in_flight).await;

    assert!(app.message.starts_with("Toggle failed:"));
  }

  #[tokio::test]
  async fn start_toggle_selected_reports_domain_connection_error() {
    let mut app = test_app();
    app.model.view = View::Domains;

    app.start_toggle_selected();

    drain_until(&mut app, |app| !app.toggle_request_in_flight).await;

    assert!(app.message.starts_with("Toggle failed:"));
  }

  #[tokio::test]
  async fn log_refresh_and_severity_filter_report_connection_error() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.open_selected_domain_logs();

    app.set_log_severity(Some(LogSeverity::Error));
    assert_eq!(app.model.logs.minimum_severity, Some(LogSeverity::Error));
    assert!(app.model.logs.request_in_flight);
    app.start_log_refresh();

    drain_until(&mut app, |app| !app.model.logs.request_in_flight).await;

    assert_eq!(app.model.logs.status, LogStreamStatus::ReadError);
    assert!(app.message.starts_with("Log refresh failed:"));
  }

  #[tokio::test]
  async fn maybe_tail_logs_starts_refresh_when_interval_elapsed() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.open_selected_domain_logs();
    app.model.logs.request_in_flight = false;
    app.last_log_refresh = Instant::now() - Duration::from_millis(800);

    app.maybe_tail_logs();

    assert!(app.model.logs.request_in_flight);
    drain_until(&mut app, |app| !app.model.logs.request_in_flight).await;
    assert!(app.model.logs.read_error.is_some());
  }

  #[tokio::test]
  async fn start_shutdown_daemon_reports_connection_error() {
    let mut app = test_app();

    app.start_shutdown_daemon();
    assert!(app.shutdown_request_in_flight);
    app.start_shutdown_daemon();

    drain_until(&mut app, |app| !app.shutdown_request_in_flight).await;

    assert!(app.message.starts_with("Shutdown failed:"));
  }

  #[test]
  fn apply_state_response_uses_snapshot_or_guidance_message() {
    let mut app = test_app();

    app.apply_state_response(QueryStateResponse {
      request_id: "state".to_string(),
      accepted: true,
      message: "updated".to_string(),
      snapshot: Some(snapshot()),
    });
    assert_eq!(app.message, "updated");
    assert_eq!(app.model.summary().entrypoints, 2);

    app.apply_state_response(QueryStateResponse {
      request_id: "state".to_string(),
      accepted: true,
      message: "empty".to_string(),
      snapshot: None,
    });
    assert_eq!(app.message, "Daemon returned no state snapshot.");
  }

  #[test]
  fn start_log_refresh_without_target_sets_guidance_message() {
    let mut app = test_app();
    app.model.logs.target = None;

    app.start_log_refresh();

    assert_eq!(
      app.message,
      "Select a domain and press Enter or l to view logs."
    );
    assert_eq!(app.log_request_serial, 0);
  }

  #[tokio::test]
  async fn open_selected_domain_logs_switches_view_and_marks_loading() {
    let mut app = test_app();
    app.model.view = View::Domains;

    app.open_selected_domain_logs();

    assert_eq!(app.model.view, View::Logs);
    assert_eq!(app.message, "Loading domain logs.");
    assert!(app.model.logs.loading);
    drain_until(&mut app, |app| !app.model.logs.request_in_flight).await;
  }

  #[test]
  fn safe_filename_replaces_shell_sensitive_characters() {
    assert_eq!(
      safe_filename("../app localhost:443?token=value"),
      "..-app-localhost-443-token-value"
    );
    assert_eq!(safe_filename("app.localhost"), "app.localhost");
  }

  #[test]
  fn format_log_excerpt_includes_redacted_export_context() {
    let target = model::LogTarget {
      registration_id: "shim-1".to_string(),
      domain_name: "app.localhost".to_string(),
      stream: LogStreamIdentity::domain("app.localhost"),
    };

    let excerpt = format_log_excerpt(&target, &[log_entry(7, LogSeverity::Error, "redacted")]);

    assert!(excerpt.contains("Cadder diagnostic log excerpt for app.localhost"));
    assert!(excerpt.contains("Registration: shim-1"));
    assert!(excerpt.contains("Messages are daemon-redacted before export."));
    assert!(excerpt.contains("Error domain=app.localhost"));
    assert!(excerpt.contains("redacted"));
  }
}
