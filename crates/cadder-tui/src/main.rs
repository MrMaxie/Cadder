mod model;

use anyhow::{Context, Result};
use cadder_daemon::{
  CadderClient, DaemonLaunchOptions, RuntimePaths, ensure_daemon_running_with_options,
};
use cadder_protocol::{
  BasicResponse, GuiStateSnapshot, LogSeverity, QueryLogsRequest, QueryLogsResponse,
  QueryStateRequest, QueryStateResponse, SetDomainEnabledRequest, SetEntrypointEnabledRequest,
  ShutdownDaemonRequest, message_types, new_request_id,
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
use std::{path::PathBuf, time::Duration};

#[derive(Debug, Parser)]
#[command(name = "cadder-tui", version, about = "Cadder terminal UI")]
struct Args {
  #[arg(long)]
  runtime_dir: Option<PathBuf>,

  #[arg(long)]
  daemon_path: Option<PathBuf>,

  #[arg(long)]
  real_caddy_command: Option<String>,

  #[arg(long)]
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
  let mut app = TuiApp {
    client,
    model: TuiModel::default(),
    logs: Vec::new(),
    message: String::new(),
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
  logs: Vec<String>,
  message: String,
}

impl TuiApp {
  async fn run(&mut self, mut terminal: DefaultTerminal) -> Result<()> {
    loop {
      terminal.draw(|frame| self.draw(frame))?;
      if event::poll(Duration::from_millis(200))?
        && let Event::Key(key) = event::read()?
      {
        if key.kind != KeyEventKind::Press {
          continue;
        }
        match key.code {
          KeyCode::Char('q') => return Ok(()),
          KeyCode::Char('r') => self.refresh().await?,
          KeyCode::Tab => self.model.next_view(),
          KeyCode::BackTab => self.model.previous_view(),
          KeyCode::Down => self.model.move_selection(1),
          KeyCode::Up => self.model.move_selection(-1),
          KeyCode::Char('/') => self.model.search_mode = true,
          KeyCode::Esc => {
            self.model.search_mode = false;
            self.model.search.clear();
          }
          KeyCode::Backspace if self.model.search_mode => {
            self.model.search.pop();
          }
          KeyCode::Char(' ') => self.toggle_selected().await?,
          KeyCode::Char('p') if self.model.view == View::Logs => {
            self.model.logs_paused = !self.model.logs_paused;
          }
          KeyCode::Char('0') if self.model.view == View::Logs => {
            self.model.minimum_log_severity = None;
            self.refresh_logs().await?;
          }
          KeyCode::Char('i') if self.model.view == View::Logs => {
            self.model.minimum_log_severity = Some(LogSeverity::Info);
            self.refresh_logs().await?;
          }
          KeyCode::Char('w') if self.model.view == View::Logs => {
            self.model.minimum_log_severity = Some(LogSeverity::Warn);
            self.refresh_logs().await?;
          }
          KeyCode::Char('e') if self.model.view == View::Logs => {
            self.model.minimum_log_severity = Some(LogSeverity::Error);
            self.refresh_logs().await?;
          }
          KeyCode::Char('x') if self.model.view == View::Logs => self.export_logs()?,
          KeyCode::Char('d') => self.shutdown_daemon().await?,
          KeyCode::Enter if self.model.view == View::Logs => self.refresh_logs().await?,
          KeyCode::Char(ch) if self.model.search_mode => self.model.search.push(ch),
          _ => {}
        }
      }
      if self.model.view == View::Logs && !self.model.logs_paused {
        let _ = self.refresh_logs().await;
      }
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
    if let Some(snapshot) = response.snapshot {
      self.model.set_snapshot(snapshot);
      self.message = response.message;
    } else {
      self.message = "Daemon returned no state snapshot.".to_string();
    }
    Ok(())
  }

  async fn toggle_selected(&mut self) -> Result<()> {
    match self.model.view {
      View::Entrypoints => {
        if let Some(entrypoint) = self.model.selected_entrypoint() {
          let response: BasicResponse = self
            .client
            .request(
              message_types::SET_ENTRYPOINT_ENABLED_REQUEST,
              message_types::SET_ENTRYPOINT_ENABLED_RESPONSE,
              &SetEntrypointEnabledRequest {
                request_id: new_request_id("tui-entrypoint-toggle"),
                registration_id: entrypoint.registration_id.clone(),
                shim_session_nonce: None,
                enabled: !entrypoint.activation_state.is_enabled(),
              },
            )
            .await?;
          self.message = response.message;
          self.refresh().await?;
        }
      }
      View::Domains => {
        if let Some((registration_id, domain)) = self.model.selected_domain() {
          let response: BasicResponse = self
            .client
            .request(
              message_types::SET_DOMAIN_ENABLED_REQUEST,
              message_types::SET_DOMAIN_ENABLED_RESPONSE,
              &SetDomainEnabledRequest {
                request_id: new_request_id("tui-domain-toggle"),
                registration_id,
                domain_key: domain.name.canonical.clone(),
                enabled: !domain.activation_state.is_enabled(),
              },
            )
            .await?;
          self.message = response.message;
          self.refresh().await?;
        }
      }
      _ => {}
    }
    Ok(())
  }

  async fn refresh_logs(&mut self) -> Result<()> {
    if let Some(stream) = self.model.selected_log_stream() {
      let response: QueryLogsResponse = self
        .client
        .request(
          message_types::QUERY_LOGS_REQUEST,
          message_types::QUERY_LOGS_RESPONSE,
          &QueryLogsRequest {
            request_id: new_request_id("tui-logs"),
            stream,
            limit: Some(100),
            cursor: None,
            minimum_severity: self.model.minimum_log_severity,
          },
        )
        .await?;
      self.logs = response
        .entries
        .into_iter()
        .map(|entry| {
          format!(
            "{} {:?} {}",
            entry.timestamp_utc.format("%H:%M:%S"),
            entry.severity,
            entry.raw_message
          )
        })
        .collect();
      self.message = response.message;
    }
    Ok(())
  }

  async fn shutdown_daemon(&mut self) -> Result<()> {
    let response: BasicResponse = self
      .client
      .request(
        message_types::SHUTDOWN_DAEMON_REQUEST,
        message_types::SHUTDOWN_DAEMON_RESPONSE,
        &ShutdownDaemonRequest {
          request_id: new_request_id("tui-shutdown"),
        },
      )
      .await?;
    self.message = response.message;
    Ok(())
  }

  fn export_logs(&mut self) -> Result<()> {
    let output_path = std::env::current_dir()
      .context("resolve current directory")?
      .join("cadder-logs-excerpt.txt");
    std::fs::write(&output_path, self.logs.join("\n"))
      .with_context(|| format!("write {}", output_path.display()))?;
    self.message = format!("Exported {}", output_path.display());
    Ok(())
  }

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
        .minimum_log_severity
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
    let title = if self.model.logs_paused {
      "Logs paused"
    } else {
      "Logs tailing"
    };
    let items = self
      .logs
      .iter()
      .map(|line| ListItem::new(line.clone()))
      .collect::<Vec<_>>();
    frame.render_widget(List::new(items).block(Block::bordered().title(title)), area);
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
