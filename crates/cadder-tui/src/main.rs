mod model;

use anyhow::{Context, Result};
use cadder_daemon::{
  CadderClient, DaemonLaunchOptions, RuntimePaths, ensure_daemon_running_with_options,
};
use cadder_protocol::{
  ActivationState, BasicResponse, ConfigApplyStatus, GuiStateSnapshot, LogEntry, LogSeverity,
  LogStreamIdentity, LogStreamStatus, QueryIisBindingsRequest, QueryIisBindingsResponse,
  QueryLogsRequest, QueryLogsResponse, QueryStateRequest, QueryStateResponse, RuntimeStatus,
  SetDomainEnabledRequest, SetEntrypointEnabledRequest, SetIisHandoffRequest,
  SetIisHandoffResponse, ShutdownDaemonRequest, message_types, new_request_id,
};
use clap::Parser;
use crossterm::event::{self, Event, KeyCode, KeyEventKind};
use model::{SeverityFilter, TuiModel, View};
use ratatui::{
  DefaultTerminal, Frame,
  layout::{Constraint, Layout},
  style::{Color, Modifier, Style},
  text::{Line, Span},
  widgets::{Block, Borders, Cell, List, ListItem, Paragraph, Row, Table, TableState, Tabs, Wrap},
};
use std::{
  path::PathBuf,
  time::{Duration, Instant},
};
use tokio::sync::mpsc;

const STATE_REFRESH_INTERVAL: Duration = Duration::from_secs(2);
const LOG_REFRESH_INTERVAL: Duration = Duration::from_millis(750);

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
    state_refresh_pending: false,
    toggle_request_in_flight: false,
    shutdown_request_in_flight: false,
    last_state_refresh: Instant::now(),
    last_log_refresh: Instant::now(),
    log_refresh_pending: false,
    log_request_serial: 0,
  };
  app.refresh().await?;
  #[cfg(windows)]
  app.refresh_iis().await;

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
  state_refresh_pending: bool,
  toggle_request_in_flight: bool,
  shutdown_request_in_flight: bool,
  last_state_refresh: Instant,
  last_log_refresh: Instant,
  log_refresh_pending: bool,
  log_request_serial: u64,
}

#[derive(Debug)]
enum AppResponse {
  State(Result<QueryStateResponse, String>),
  Toggle(Result<BasicResponse, String>),
  #[cfg(windows)]
  IisBindings(Result<QueryIisBindingsResponse, String>),
  #[cfg(windows)]
  IisHandoff(Result<SetIisHandoffResponse, String>),
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
      self.maybe_refresh_state();
      self.maybe_tail_logs();
      terminal.draw(|frame| self.draw(frame))?;
      if event::poll(Duration::from_millis(200))?
        && let Event::Key(key) = event::read()?
      {
        if key.kind != KeyEventKind::Press {
          continue;
        }
        if self.handle_key_code(key.code)? {
          return Ok(());
        }
      }
    }
  }

  fn handle_key_code(&mut self, code: KeyCode) -> Result<bool> {
    #[cfg(windows)]
    if self.model.iis.route_host_input_mode {
      match code {
        KeyCode::Esc => self.model.iis.finish_route_host_input(),
        KeyCode::Enter => self.model.iis.finish_route_host_input(),
        KeyCode::Backspace => self.model.iis.pop_route_host_char(),
        KeyCode::Char(ch) => self.model.iis.push_route_host_char(ch),
        _ => {}
      }
      return Ok(false);
    }

    if self.model.search_mode {
      match code {
        KeyCode::Esc => {
          self.model.search_mode = false;
          self.model.search.clear();
        }
        KeyCode::Enter => {
          self.model.search_mode = false;
        }
        KeyCode::Backspace => {
          self.model.search.pop();
        }
        KeyCode::Char(ch) => self.model.search.push(ch),
        _ => {}
      }
      self.model.clamp_selections();
      return Ok(false);
    }

    match code {
      KeyCode::Char('q') => return Ok(true),
      KeyCode::Char('r') => {
        self.start_state_refresh();
        #[cfg(windows)]
        if self.model.view == View::IisHandoff {
          self.start_iis_refresh();
        }
      }
      KeyCode::Tab | KeyCode::Right => self.model.next_view(),
      KeyCode::BackTab | KeyCode::Left => self.model.previous_view(),
      KeyCode::Down => self.model.move_selection(1),
      KeyCode::Up => self.model.move_selection(-1),
      #[cfg(windows)]
      KeyCode::Char('/') if self.model.view == View::IisHandoff => {
        self.model.iis.begin_route_host_input();
      }
      KeyCode::Char('/') => self.model.search_mode = true,
      KeyCode::Char(' ') if self.model.view == View::Settings => self.apply_settings_log_severity(),
      #[cfg(windows)]
      KeyCode::Char(' ') if self.model.view == View::IisHandoff => self.start_iis_handoff_toggle(),
      #[cfg(windows)]
      KeyCode::Enter if self.model.view == View::IisHandoff => self.start_iis_refresh(),
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
      KeyCode::Enter if self.model.view == View::Settings => self.apply_settings_log_severity(),
      KeyCode::Enter if self.model.view == View::Domains => self.open_selected_domain_logs(),
      KeyCode::Char('l') if self.model.view == View::Domains => self.open_selected_domain_logs(),
      KeyCode::Enter if self.model.view == View::Logs => self.start_log_refresh(),
      _ => {}
    }

    Ok(false)
  }

  fn drain_responses(&mut self) {
    while let Ok(response) = self.responses_rx.try_recv() {
      match response {
        AppResponse::State(result) => {
          self.state_request_in_flight = false;
          self.last_state_refresh = Instant::now();
          match result {
            Ok(response) => self.apply_state_response(response),
            Err(error) => self.message = format!("State refresh failed: {error}"),
          }
          self.start_pending_state_refresh();
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
        #[cfg(windows)]
        AppResponse::IisBindings(result) => {
          self.model.iis.request_in_flight = false;
          match result {
            Ok(response) => {
              let message = response.message.clone();
              self.model.iis.apply_response(response);
              self.message = if let Some(error) = &self.model.iis.last_error {
                format!("IIS discovery warning: {error}")
              } else {
                message
              };
            }
            Err(error) => {
              self.model.iis.apply_error(error.clone());
              self.message = format!("IIS discovery failed: {error}");
            }
          }
        }
        #[cfg(windows)]
        AppResponse::IisHandoff(result) => {
          self.model.iis.action_in_flight = false;
          match result {
            Ok(response) => {
              self.message = response.message;
              self.start_iis_refresh();
              self.start_state_refresh();
            }
            Err(error) => self.message = format!("IIS handoff failed: {error}"),
          }
        }
        AppResponse::Logs {
          serial,
          stream,
          minimum_severity,
          result,
        } => {
          let current_request = serial == self.log_request_serial
            && self
              .model
              .logs
              .active_stream()
              .is_some_and(|active| active == stream);
          if !current_request {
            continue;
          }
          if self.model.logs.minimum_severity != minimum_severity {
            self.model.logs.request_in_flight = false;
            self.finish_stale_log_response();
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
          self.start_pending_log_refresh();
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
      || self.last_log_refresh.elapsed() < LOG_REFRESH_INTERVAL
    {
      return;
    }
    self.start_log_refresh();
  }

  fn maybe_refresh_state(&mut self) {
    if self.state_request_in_flight || self.last_state_refresh.elapsed() < STATE_REFRESH_INTERVAL {
      return;
    }
    self.start_state_refresh();
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
    self.last_state_refresh = Instant::now();
    Ok(())
  }

  #[cfg(windows)]
  async fn refresh_iis(&mut self) {
    match self
      .client
      .request(
        message_types::QUERY_IIS_BINDINGS_REQUEST,
        message_types::QUERY_IIS_BINDINGS_RESPONSE,
        &QueryIisBindingsRequest {
          request_id: new_request_id("tui-iis"),
        },
      )
      .await
      .map_err(|error| error.to_string())
    {
      Ok(response) => self.model.iis.apply_response(response),
      Err(error) => self.model.iis.apply_error(error),
    }
  }

  fn start_state_refresh(&mut self) {
    if self.state_request_in_flight {
      self.state_refresh_pending = true;
      return;
    }
    self.state_request_in_flight = true;
    self.state_refresh_pending = false;
    self.last_state_refresh = Instant::now();
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

  fn start_pending_state_refresh(&mut self) {
    if !self.state_refresh_pending {
      return;
    }
    self.state_refresh_pending = false;
    self.start_state_refresh();
  }

  #[cfg(windows)]
  fn start_iis_refresh(&mut self) {
    if self.model.iis.request_in_flight {
      return;
    }
    self.model.iis.mark_loading();
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    tokio::spawn(async move {
      let result = client
        .request(
          message_types::QUERY_IIS_BINDINGS_REQUEST,
          message_types::QUERY_IIS_BINDINGS_RESPONSE,
          &QueryIisBindingsRequest {
            request_id: new_request_id("tui-iis"),
          },
        )
        .await
        .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::IisBindings(result));
    });
  }

  #[cfg(windows)]
  fn start_iis_handoff_toggle(&mut self) {
    if self.model.iis.action_in_flight {
      return;
    }
    let Some(binding) = self.model.iis.selected_binding() else {
      self.message = "No IIS binding selected.".to_string();
      return;
    };
    let enabled = binding.handoff_state != cadder_protocol::IisHandoffState::HandedOff;
    let route_host = if enabled && binding.domain_key.is_none() {
      let host = self.model.iis.route_host_input.trim();
      (!host.is_empty()).then(|| host.to_string())
    } else {
      None
    };
    if matches!(
      binding.handoff_state,
      cadder_protocol::IisHandoffState::Unsupported
        | cadder_protocol::IisHandoffState::Conflict
        | cadder_protocol::IisHandoffState::Unavailable
        | cadder_protocol::IisHandoffState::Busy
    ) && enabled
    {
      self.message = binding
        .issue
        .map(|issue| issue.message)
        .unwrap_or_else(|| "Selected IIS binding cannot be handed off safely.".to_string());
      return;
    }
    if binding.handoff_state == cadder_protocol::IisHandoffState::MissingRoute
      && enabled
      && route_host.is_none()
    {
      self.message = binding.issue.map(|issue| issue.message).unwrap_or_else(|| {
        "Enter a route host with / before handing off this IIS binding.".to_string()
      });
      return;
    }
    self.model.iis.action_in_flight = true;
    let client = self.client.clone();
    let responses = self.responses_tx.clone();
    tokio::spawn(async move {
      let result = client
        .request(
          message_types::SET_IIS_HANDOFF_REQUEST,
          message_types::SET_IIS_HANDOFF_RESPONSE,
          &SetIisHandoffRequest {
            request_id: new_request_id("tui-iis-handoff"),
            binding_id: binding.identity.binding_id,
            enabled,
            route_host,
          },
        )
        .await
        .map_err(|error| error.to_string());
      let _ = responses.send(AppResponse::IisHandoff(result));
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
      self.message = format!(
        "Log severity filter already set to {}.",
        describe_log_severity(severity)
      );
      return;
    }
    self.model.logs.reset_for_filter(severity);
    self.model.sync_settings_severity_from_logs();
    self.message = match severity {
      Some(severity) => format!("Log severity filter set to {severity:?}."),
      None => "Log severity filter cleared.".to_string(),
    };
    if self.model.logs.target.is_some() {
      self.start_log_refresh();
    }
  }

  fn apply_settings_log_severity(&mut self) {
    let filter = self.model.settings.selected_filter();
    self.set_log_severity(filter.minimum_severity());
  }

  fn start_log_refresh(&mut self) {
    if self.model.logs.request_in_flight {
      self.log_refresh_pending = true;
      return;
    }
    let Some(stream) = self.model.logs.active_stream() else {
      self.message = "Select a domain and press Enter or l to view logs.".to_string();
      return;
    };
    let cursor = self.model.logs.next_cursor.clone();
    let minimum_severity = self.model.logs.minimum_severity;
    self.model.logs.mark_loading();
    self.log_refresh_pending = false;
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

  fn start_pending_log_refresh(&mut self) {
    if !self.log_refresh_pending {
      return;
    }
    self.log_refresh_pending = false;
    self.start_log_refresh();
  }

  fn finish_stale_log_response(&mut self) {
    if self.log_refresh_pending {
      self.start_pending_log_refresh();
    } else {
      self.model.logs.loading = false;
    }
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

fn describe_log_severity(severity: Option<LogSeverity>) -> &'static str {
  match severity {
    Some(LogSeverity::Info) => "Info and higher",
    Some(LogSeverity::Warn) => "Warnings and errors",
    Some(LogSeverity::Error | LogSeverity::Fatal) => "Errors only",
    _ => "All",
  }
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

struct UiStyles;

impl UiStyles {
  fn tab_selected() -> Style {
    Style::new().fg(Color::Cyan).add_modifier(Modifier::BOLD)
  }

  fn block_border() -> Style {
    Style::new().fg(Color::Blue)
  }

  fn title() -> Style {
    Style::new().fg(Color::Cyan).add_modifier(Modifier::BOLD)
  }

  fn header() -> Style {
    Style::new()
      .fg(Color::LightCyan)
      .add_modifier(Modifier::BOLD)
  }

  fn selected_row() -> Style {
    Style::new()
      .fg(Color::White)
      .bg(Color::Blue)
      .add_modifier(Modifier::BOLD | Modifier::REVERSED)
  }

  fn active() -> Style {
    Style::new().fg(Color::Green).add_modifier(Modifier::BOLD)
  }

  fn disabled() -> Style {
    Style::new().fg(Color::DarkGray)
  }

  fn warning() -> Style {
    Style::new().fg(Color::Yellow).add_modifier(Modifier::BOLD)
  }

  fn error() -> Style {
    Style::new().fg(Color::Red).add_modifier(Modifier::BOLD)
  }

  fn info() -> Style {
    Style::new().fg(Color::LightBlue)
  }

  fn muted() -> Style {
    Style::new().fg(Color::DarkGray)
  }
}

fn app_block(title: impl Into<String>) -> Block<'static> {
  Block::bordered()
    .title(title.into())
    .border_style(UiStyles::block_border())
    .title_style(UiStyles::title())
}

fn activation_marker(state: ActivationState) -> String {
  let marker = if state.is_enabled() { "[x]" } else { "[ ]" };
  format!("{marker} {state:?}")
}

fn activation_style(state: ActivationState) -> Style {
  match state {
    ActivationState::Active | ActivationState::Registered | ActivationState::Activating => {
      UiStyles::active()
    }
    ActivationState::Faulted => UiStyles::error(),
    ActivationState::Unknown => UiStyles::warning(),
    ActivationState::Inactive => UiStyles::disabled(),
  }
}

fn runtime_status_style(status: RuntimeStatus) -> Style {
  match status {
    RuntimeStatus::Running | RuntimeStatus::Resolved => UiStyles::active(),
    RuntimeStatus::Idle => UiStyles::muted(),
    RuntimeStatus::Unhealthy | RuntimeStatus::NotResolved => UiStyles::error(),
    RuntimeStatus::Unknown => UiStyles::warning(),
  }
}

fn config_status_style(status: ConfigApplyStatus) -> Style {
  match status {
    ConfigApplyStatus::Applied => UiStyles::active(),
    ConfigApplyStatus::Idle | ConfigApplyStatus::NotApplied => UiStyles::muted(),
    ConfigApplyStatus::Failed => UiStyles::error(),
    ConfigApplyStatus::Unknown => UiStyles::warning(),
  }
}

fn severity_style(severity: LogSeverity) -> Style {
  match severity {
    LogSeverity::Warn => UiStyles::warning(),
    LogSeverity::Error | LogSeverity::Fatal => UiStyles::error(),
    LogSeverity::Info => UiStyles::info(),
    LogSeverity::Unknown | LogSeverity::Trace | LogSeverity::Debug => UiStyles::muted(),
  }
}

#[cfg(windows)]
fn iis_state_style(state: cadder_protocol::IisHandoffState) -> Style {
  match state {
    cadder_protocol::IisHandoffState::Available => UiStyles::active(),
    cadder_protocol::IisHandoffState::HandedOff => UiStyles::info(),
    cadder_protocol::IisHandoffState::Unsupported
    | cadder_protocol::IisHandoffState::MissingRoute
    | cadder_protocol::IisHandoffState::Unavailable => UiStyles::warning(),
    cadder_protocol::IisHandoffState::Conflict => UiStyles::error(),
    cadder_protocol::IisHandoffState::Busy => UiStyles::muted(),
  }
}

impl TuiApp {
  fn draw(&self, frame: &mut Frame<'_>) {
    let [tabs_area, body_area, status_area] = Layout::vertical([
      Constraint::Length(3),
      Constraint::Min(3),
      Constraint::Length(3),
    ])
    .areas(frame.area());

    let tabs = Tabs::new(
      View::ALL
        .iter()
        .map(|view| view.title())
        .collect::<Vec<_>>(),
    )
    .select(self.model.view.index())
    .block(
      Block::new()
        .borders(Borders::BOTTOM)
        .title("Cadder")
        .border_style(UiStyles::block_border())
        .title_style(UiStyles::title()),
    )
    .highlight_style(UiStyles::tab_selected());
    frame.render_widget(tabs, tabs_area);

    match self.model.view {
      View::Overview => self.draw_overview(frame, body_area),
      View::Entrypoints => self.draw_entrypoints(frame, body_area),
      View::Domains => self.draw_domains(frame, body_area),
      #[cfg(windows)]
      View::IisHandoff => self.draw_iis_handoff(frame, body_area),
      View::Logs => self.draw_logs(frame, body_area),
      View::Settings => self.draw_settings(frame, body_area),
      View::Diagnostics => self.draw_diagnostics(frame, body_area),
    }

    #[cfg(windows)]
    let iis_route_host = if self.model.view == View::IisHandoff {
      if self.model.iis.route_host_input_mode {
        format!(" IIS route host: {}", self.model.iis.route_host_input)
      } else if self.model.iis.route_host_input.is_empty() {
        String::new()
      } else {
        format!(" IIS route host set: {}", self.model.iis.route_host_input)
      }
    } else {
      String::new()
    };
    #[cfg(not(windows))]
    let iis_route_host = String::new();
    let search = if self.model.search_mode {
      format!(" search: {}", self.model.search)
    } else if self.model.search.is_empty() {
      String::new()
    } else {
      format!(" filter: {}", self.model.search)
    };
    let status_context = format!(
      "{}{}{}  severity: {}",
      self.message,
      search,
      iis_route_host,
      describe_log_severity(self.model.logs.minimum_severity)
    );
    let navigation_help = "Tab/Shift+Tab/Left/Right views  r refresh  / search  Space toggle/apply";
    #[cfg(windows)]
    let action_help = "IIS: / route host Enter refresh Space handoff/restore  Settings: Up/Down severity Enter apply  Logs: p pause Enter refresh x export  d shutdown  q quit";
    #[cfg(not(windows))]
    let action_help = "Settings: Up/Down severity Enter apply  Logs: p pause Enter refresh x export  d shutdown  q quit";
    frame.render_widget(
      Paragraph::new(vec![
        Line::from(status_context),
        Line::styled(navigation_help, UiStyles::muted()),
        Line::styled(action_help, UiStyles::muted()),
      ])
      .wrap(Wrap { trim: true }),
      status_area,
    );
  }

  fn draw_overview(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let summary = self.model.summary();
    let snapshot = self.model.snapshot();
    let lines = vec![
      Line::from(vec![
        Span::styled("Runtime: ", UiStyles::header()),
        Span::styled(
          summary.runtime,
          runtime_status_style(snapshot.runtime.status),
        ),
      ]),
      Line::from(vec![
        Span::styled("Config: ", UiStyles::header()),
        Span::styled(summary.config, config_status_style(snapshot.config.status)),
      ]),
      Line::from(vec![
        Span::styled("Entrypoints: ", UiStyles::header()),
        Span::styled(summary.entrypoints.to_string(), UiStyles::info()),
      ]),
      Line::from(vec![
        Span::styled("Domains: ", UiStyles::header()),
        Span::styled(summary.domains.to_string(), UiStyles::info()),
      ]),
      Line::from(vec![
        Span::styled("Active domains: ", UiStyles::header()),
        Span::styled(summary.active_domains.to_string(), UiStyles::active()),
      ]),
    ];
    frame.render_widget(
      Paragraph::new(lines)
        .block(app_block("Overview"))
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
          Cell::from(activation_marker(registration.activation_state)),
          Cell::from(registration.source_working_directory.raw.clone()),
          Cell::from(registration.registered_domains.len().to_string()),
        ])
        .style(activation_style(registration.activation_state))
      });
    let table = Table::new(
      rows,
      [
        Constraint::Length(24),
        Constraint::Length(16),
        Constraint::Percentage(60),
        Constraint::Length(8),
      ],
    )
    .header(Row::new(["ID", "State", "Source", "Domains"]).style(UiStyles::header()))
    .block(app_block("Entrypoints"))
    .row_highlight_style(UiStyles::selected_row())
    .highlight_symbol(">> ");
    let mut state = TableState::default().with_selected(Some(self.model.entrypoint_selected));
    frame.render_stateful_widget(table, area, &mut state);
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
          Cell::from(activation_marker(domain.activation_state)),
        ])
        .style(activation_style(domain.activation_state))
      });
    let table = Table::new(
      rows,
      [
        Constraint::Length(24),
        Constraint::Percentage(60),
        Constraint::Length(16),
      ],
    )
    .header(Row::new(["Entrypoint", "Domain", "State"]).style(UiStyles::header()))
    .block(app_block("Domains"))
    .row_highlight_style(UiStyles::selected_row())
    .highlight_symbol(">> ");
    let mut state = TableState::default().with_selected(Some(self.model.domain_selected));
    frame.render_stateful_widget(table, area, &mut state);
  }

  #[cfg(windows)]
  fn draw_iis_handoff(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let rows = self.model.iis.bindings.iter().map(|binding| {
      let issue = binding
        .issue
        .as_ref()
        .map(|issue| issue.message.clone())
        .unwrap_or_else(|| {
          if binding.handoff_state == cadder_protocol::IisHandoffState::HandedOff {
            "Space restores the original IIS binding.".to_string()
          } else {
            "Space hands this host to Cadder.".to_string()
          }
        });
      Row::new(vec![
        Cell::from(binding.identity.site_name.clone()),
        Cell::from(binding.identity.protocol.clone()),
        Cell::from(binding.ip_address.clone()),
        Cell::from(binding.port.to_string()),
        Cell::from(binding.host_header.clone()),
        Cell::from(format!("{:?}", binding.handoff_state)),
        Cell::from(issue),
      ])
      .style(iis_state_style(binding.handoff_state))
    });
    let title = if self.model.iis.loading {
      "IIS Handoff: loading"
    } else if self.model.iis.last_error.is_some() {
      "IIS Handoff: unavailable"
    } else {
      "IIS Handoff"
    };
    let table = Table::new(
      rows,
      [
        Constraint::Length(22),
        Constraint::Length(8),
        Constraint::Length(12),
        Constraint::Length(6),
        Constraint::Length(28),
        Constraint::Length(14),
        Constraint::Percentage(40),
      ],
    )
    .header(
      Row::new(["Site", "Protocol", "IP", "Port", "Host", "State", "Safety"])
        .style(UiStyles::header()),
    )
    .block(app_block(title))
    .row_highlight_style(UiStyles::selected_row())
    .highlight_symbol(">> ");
    let mut state = TableState::default().with_selected(Some(self.model.iis.selected));
    frame.render_stateful_widget(table, area, &mut state);
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
      "Logs: {target} | {mode} | {:?} | {}",
      logs.status,
      describe_log_severity(logs.minimum_severity)
    );
    let mut items = Vec::new();
    items.extend(
      self
        .log_state_lines()
        .into_iter()
        .map(|line| ListItem::new(Line::styled(line, UiStyles::muted()))),
    );
    items.extend(logs.entries.iter().map(|entry| {
      ListItem::new(Line::from(vec![
        Span::styled(
          entry.timestamp_utc.format("%H:%M:%S").to_string(),
          UiStyles::muted(),
        ),
        Span::raw(" "),
        Span::styled(
          format!("{:?}", entry.severity),
          severity_style(entry.severity),
        ),
        Span::raw(" "),
        Span::raw(entry.raw_message.clone()),
      ]))
    }));
    frame.render_widget(List::new(items).block(app_block(title)), area);
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

  fn draw_settings(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let active_filter = SeverityFilter::from_minimum_severity(self.model.logs.minimum_severity);
    let rows = SeverityFilter::ALL.into_iter().map(|filter| {
      let applied = if filter == active_filter {
        "[x]"
      } else {
        "[ ]"
      };
      let style = if filter == active_filter {
        UiStyles::active()
      } else {
        Style::new()
      };
      Row::new(vec![
        Cell::from(applied),
        Cell::from(filter.label()),
        Cell::from(filter.description()),
      ])
      .style(style)
    });
    let table = Table::new(
      rows,
      [
        Constraint::Length(8),
        Constraint::Length(24),
        Constraint::Percentage(68),
      ],
    )
    .header(Row::new(["Applied", "Severity", "Effect"]).style(UiStyles::header()))
    .block(app_block("Settings"))
    .row_highlight_style(UiStyles::selected_row())
    .highlight_symbol(">> ");
    let mut state =
      TableState::default().with_selected(Some(self.model.settings.selected_severity));
    frame.render_stateful_widget(table, area, &mut state);
  }

  fn draw_diagnostics(&self, frame: &mut Frame<'_>, area: ratatui::layout::Rect) {
    let snapshot = self.model.snapshot();
    let mut lines = Vec::new();
    lines.extend(snapshot.config.diagnostics.iter().map(|diagnostic| {
      ListItem::new(Line::styled(
        format!("config:{} {}", diagnostic.code, diagnostic.message),
        UiStyles::error(),
      ))
    }));
    lines.extend(snapshot.runtime.diagnostics.iter().map(|diagnostic| {
      ListItem::new(Line::styled(
        format!("runtime:{} {}", diagnostic.code, diagnostic.message),
        UiStyles::warning(),
      ))
    }));
    if lines.is_empty() {
      lines.push(ListItem::new(Line::styled(
        "No diagnostics.",
        UiStyles::muted(),
      )));
    }
    frame.render_widget(List::new(lines).block(app_block("Diagnostics")), area);
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
      state_refresh_pending: false,
      toggle_request_in_flight: false,
      shutdown_request_in_flight: false,
      last_state_refresh: Instant::now(),
      last_log_refresh: Instant::now(),
      log_refresh_pending: false,
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

  #[cfg(windows)]
  fn iis_binding(
    id: &str,
    host: &str,
    state: cadder_protocol::IisHandoffState,
    issue: Option<cadder_protocol::IisIssue>,
  ) -> cadder_protocol::IisBinding {
    cadder_protocol::IisBinding {
      identity: cadder_protocol::IisBindingIdentity {
        binding_id: id.to_string(),
        site_name: "Default Web Site".to_string(),
        protocol: if host.starts_with("secure") {
          "https".to_string()
        } else {
          "http".to_string()
        },
        binding_information: format!("*:80:{host}"),
      },
      ip_address: "*".to_string(),
      port: 80,
      host_header: host.to_string(),
      domain_key: Some(host.to_string()),
      handoff_state: state,
      issue,
      restore_metadata: None,
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
    assert!(text.contains("severity: Errors only"));
    assert!(text.contains("Left/Right"));
    assert!(text.contains("Settings: Up/Down severity Enter apply"));
    assert!(!text.contains("i/w/e/0"));
    assert!(text.contains("no domain selected"));
  }

  #[test]
  fn draw_domains_includes_selection_and_activation_markers() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.domain_selected = 1;

    let text = render(&app);

    assert!(text.contains(">>"));
    assert!(text.contains("app.localhost"));
    assert!(text.contains("[x] Active"));
    assert!(text.contains("api.localhost"));
    assert!(text.contains("[ ] Inactive"));
  }

  #[test]
  fn draw_settings_shows_applied_filter_and_choices() {
    let mut app = test_app();
    app.model.view = View::Settings;
    app.model.logs.minimum_severity = Some(LogSeverity::Warn);
    app.model.sync_settings_severity_from_logs();

    let text = render(&app);

    assert!(text.contains("Settings"));
    assert!(text.contains("[x]"));
    assert!(text.contains("Warnings and errors"));
    assert!(text.contains("Errors only"));
  }

  #[cfg(windows)]
  #[test]
  fn draw_iis_handoff_shows_rows_and_safety_reasons() {
    let mut app = test_app();
    app.model.view = View::IisHandoff;
    app.model.iis.bindings = vec![
      iis_binding(
        "available",
        "app.localhost",
        cadder_protocol::IisHandoffState::Available,
        None,
      ),
      iis_binding(
        "unsupported",
        "secure.localhost",
        cadder_protocol::IisHandoffState::Unsupported,
        Some(cadder_protocol::IisIssue::new(
          cadder_protocol::IisIssueKind::UnsupportedBindingShape,
          "HTTPS unsupported.",
        )),
      ),
      iis_binding(
        "restore-failed",
        "restore.localhost",
        cadder_protocol::IisHandoffState::HandedOff,
        Some(cadder_protocol::IisIssue::new(
          cadder_protocol::IisIssueKind::RestoreFailed,
          "Restore failed.",
        )),
      ),
      iis_binding(
        "rollback-failed",
        "rollback.localhost",
        cadder_protocol::IisHandoffState::Conflict,
        Some(cadder_protocol::IisIssue::new(
          cadder_protocol::IisIssueKind::RollbackFailed,
          "Rollback failed.",
        )),
      ),
    ];

    let text = render(&app);

    assert!(text.contains("IIS Handoff"));
    assert!(text.contains("app.localhost"));
    assert!(text.contains("HTTPS unsupported."));
    assert!(text.contains("Restore failed."));
    assert!(text.contains("Rollback failed."));
  }

  #[test]
  fn style_helpers_use_high_contrast_terminal_styles() {
    assert_eq!(activation_marker(ActivationState::Active), "[x] Active");
    assert_eq!(activation_marker(ActivationState::Inactive), "[ ] Inactive");
    assert_eq!(
      activation_style(ActivationState::Active).fg,
      Some(Color::Green)
    );
    assert_eq!(
      activation_style(ActivationState::Inactive).fg,
      Some(Color::DarkGray)
    );
    assert_eq!(severity_style(LogSeverity::Error).fg, Some(Color::Red));
    assert!(
      UiStyles::selected_row()
        .add_modifier
        .contains(Modifier::REVERSED)
    );
  }

  #[test]
  fn keyboard_navigation_supports_tabs_arrows_and_selection_preservation() {
    let mut app = test_app();

    app.handle_key_code(KeyCode::Left).unwrap();
    assert_eq!(app.model.view, View::Diagnostics);
    app.handle_key_code(KeyCode::Right).unwrap();
    assert_eq!(app.model.view, View::Overview);
    app.handle_key_code(KeyCode::Tab).unwrap();
    assert_eq!(app.model.view, View::Entrypoints);
    app.handle_key_code(KeyCode::BackTab).unwrap();
    assert_eq!(app.model.view, View::Overview);

    app.model.view = View::Domains;
    app.handle_key_code(KeyCode::Down).unwrap();
    assert_eq!(app.model.domain_selected, 1);
    app.handle_key_code(KeyCode::Right).unwrap();
    app.handle_key_code(KeyCode::Right).unwrap();
    #[cfg(windows)]
    app.handle_key_code(KeyCode::Right).unwrap();
    assert_eq!(app.model.view, View::Settings);
    app.handle_key_code(KeyCode::Left).unwrap();
    app.handle_key_code(KeyCode::Left).unwrap();
    #[cfg(windows)]
    app.handle_key_code(KeyCode::Left).unwrap();
    assert_eq!(app.model.view, View::Domains);
    assert_eq!(app.model.domain_selected, 1);
  }

  #[test]
  fn settings_keyboard_applies_selected_log_severity() {
    let mut app = test_app();
    app.model.view = View::Settings;

    app.handle_key_code(KeyCode::Down).unwrap();
    app.handle_key_code(KeyCode::Down).unwrap();
    app.handle_key_code(KeyCode::Enter).unwrap();

    assert_eq!(app.model.logs.minimum_severity, Some(LogSeverity::Warn));
    assert_eq!(app.model.settings.selected_filter(), SeverityFilter::Warn);
    assert_eq!(app.message, "Log severity filter set to Warn.");
    assert!(!app.model.logs.request_in_flight);

    app.handle_key_code(KeyCode::Char(' ')).unwrap();
    assert_eq!(
      app.message,
      "Log severity filter already set to Warnings and errors."
    );
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

  #[cfg(windows)]
  #[tokio::test]
  async fn drain_responses_applies_iis_discovery_and_handoff_results() {
    let mut app = test_app();
    app.model.view = View::IisHandoff;
    app.model.iis.request_in_flight = true;
    app.model.iis.action_in_flight = true;

    app
      .responses_tx
      .send(AppResponse::IisBindings(Ok(QueryIisBindingsResponse {
        request_id: "iis".to_string(),
        accepted: true,
        message: "IIS bindings returned.".to_string(),
        bindings: vec![iis_binding(
          "available",
          "app.localhost",
          cadder_protocol::IisHandoffState::Available,
          None,
        )],
        issue: None,
      })))
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::IisHandoff(Ok(SetIisHandoffResponse {
        request_id: "handoff".to_string(),
        accepted: true,
        message: "IIS binding `app.localhost` handed off to Cadder.".to_string(),
        binding: None,
        issue: None,
      })))
      .unwrap();

    app.drain_responses();

    assert_eq!(app.model.iis.bindings.len(), 1);
    assert!(app.model.iis.request_in_flight);
    assert!(!app.model.iis.action_in_flight);
    assert_eq!(
      app.message,
      "IIS binding `app.localhost` handed off to Cadder."
    );
  }

  #[cfg(windows)]
  #[tokio::test]
  async fn iis_missing_route_requires_search_host_before_handoff() {
    let mut app = test_app();
    app.model.view = View::IisHandoff;
    app.model.iis.bindings = vec![cadder_protocol::IisBinding {
      identity: cadder_protocol::IisBindingIdentity {
        binding_id: "Default Web Site|https|*:443:".to_string(),
        site_name: "Default Web Site".to_string(),
        protocol: "https".to_string(),
        binding_information: "*:443:".to_string(),
      },
      ip_address: "*".to_string(),
      port: 443,
      host_header: String::new(),
      domain_key: None,
      handoff_state: cadder_protocol::IisHandoffState::MissingRoute,
      issue: Some(cadder_protocol::IisIssue::new(
        cadder_protocol::IisIssueKind::MissingRoute,
        "Wildcard IIS bindings need a route host.",
      )),
      restore_metadata: None,
    }];

    app.handle_key_code(KeyCode::Char(' ')).unwrap();
    assert!(!app.model.iis.action_in_flight);
    assert_eq!(app.message, "Wildcard IIS bindings need a route host.");

    app.handle_key_code(KeyCode::Char('/')).unwrap();
    for ch in "iis-app.localhost".chars() {
      app.handle_key_code(KeyCode::Char(ch)).unwrap();
    }
    app.handle_key_code(KeyCode::Enter).unwrap();
    app.handle_key_code(KeyCode::Char(' ')).unwrap();

    assert!(app.model.search.is_empty());
    assert_eq!(app.model.iis.route_host_input, "iis-app.localhost");
    assert!(!app.model.iis.route_host_input_mode);
    assert!(app.model.iis.action_in_flight);
  }

  #[tokio::test]
  async fn toggle_success_queues_state_refresh_when_poll_is_in_flight() {
    let mut app = test_app();
    app.state_request_in_flight = true;

    app
      .responses_tx
      .send(AppResponse::Toggle(Ok(BasicResponse {
        request_id: "toggle".to_string(),
        accepted: true,
        message: "domain toggled".to_string(),
      })))
      .unwrap();
    app
      .responses_tx
      .send(AppResponse::State(Ok(QueryStateResponse {
        request_id: "state".to_string(),
        accepted: true,
        message: "old snapshot".to_string(),
        snapshot: Some(snapshot()),
      })))
      .unwrap();

    app.drain_responses();

    assert!(app.state_request_in_flight);
    assert!(!app.state_refresh_pending);
    drain_until(&mut app, |app| !app.state_request_in_flight).await;
    assert!(app.message.starts_with("State refresh failed:"));
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
  async fn maybe_refresh_state_starts_after_interval_without_overlap() {
    let mut app = test_app();
    let elapsed_refresh = Instant::now() - STATE_REFRESH_INTERVAL - Duration::from_millis(10);

    app.state_request_in_flight = true;
    app.last_state_refresh = elapsed_refresh;
    app.maybe_refresh_state();
    assert_eq!(app.last_state_refresh, elapsed_refresh);

    app.state_request_in_flight = false;
    app.maybe_refresh_state();
    assert!(app.state_request_in_flight);
    let dispatched_at = app.last_state_refresh;

    app.maybe_refresh_state();
    assert_eq!(app.last_state_refresh, dispatched_at);

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
  async fn severity_change_waits_for_in_flight_log_request_before_refreshing() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.open_selected_domain_logs();
    let stream = app.model.logs.active_stream().unwrap();
    app.log_request_serial = 1;
    app.model.logs.request_in_flight = true;
    app.model.logs.loading = true;

    app.set_log_severity(Some(LogSeverity::Warn));

    assert_eq!(app.log_request_serial, 1);
    assert!(app.model.logs.request_in_flight);
    assert!(app.log_refresh_pending);
    assert_eq!(app.model.logs.minimum_severity, Some(LogSeverity::Warn));

    app
      .responses_tx
      .send(AppResponse::Logs {
        serial: 1,
        stream,
        minimum_severity: None,
        result: Ok(QueryLogsResponse {
          request_id: "old".to_string(),
          accepted: true,
          message: "old".to_string(),
          stream: LogStreamIdentity::domain("app.localhost"),
          stream_status: LogStreamStatus::Active,
          entries: vec![log_entry(1, LogSeverity::Info, "old info")],
          next_cursor: Some("seq:1".to_string()),
          has_gap: false,
          has_more_before: false,
          truncated_by_retention: false,
        }),
      })
      .unwrap();

    app.drain_responses();

    assert_eq!(app.log_request_serial, 2);
    assert!(!app.log_refresh_pending);
    assert!(app.model.logs.entries.is_empty());
    assert_eq!(app.model.logs.minimum_severity, Some(LogSeverity::Warn));
    drain_until(&mut app, |app| !app.model.logs.request_in_flight).await;
  }

  #[test]
  fn stale_log_response_without_pending_refresh_clears_loading_state() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.open_selected_domain_logs();
    let stream = app.model.logs.active_stream().unwrap();
    app.log_request_serial = 1;
    app.model.logs.minimum_severity = Some(LogSeverity::Warn);
    app.model.logs.request_in_flight = true;
    app.model.logs.loading = true;

    app
      .responses_tx
      .send(AppResponse::Logs {
        serial: 1,
        stream: stream.clone(),
        minimum_severity: None,
        result: Ok(QueryLogsResponse {
          request_id: "stale".to_string(),
          accepted: true,
          message: "stale".to_string(),
          stream,
          stream_status: LogStreamStatus::Active,
          entries: vec![log_entry(1, LogSeverity::Info, "old info")],
          next_cursor: Some("seq:1".to_string()),
          has_gap: false,
          has_more_before: false,
          truncated_by_retention: false,
        }),
      })
      .unwrap();

    app.drain_responses();

    assert_eq!(app.log_request_serial, 1);
    assert!(!app.log_refresh_pending);
    assert!(!app.model.logs.request_in_flight);
    assert!(!app.model.logs.loading);
    assert!(app.model.logs.entries.is_empty());
  }

  #[tokio::test]
  async fn maybe_tail_logs_starts_refresh_when_interval_elapsed() {
    let mut app = test_app();
    app.model.view = View::Domains;
    app.model.open_selected_domain_logs();
    app.model.logs.request_in_flight = false;
    app.last_log_refresh = Instant::now() - LOG_REFRESH_INTERVAL - Duration::from_millis(10);

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
