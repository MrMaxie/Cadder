use cadder_protocol::{
    EntrypointRegistration, GuiStateSnapshot, LogSeverity, LogStreamIdentity, RegisteredDomain,
};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum View {
    Overview,
    Entrypoints,
    Domains,
    Logs,
    Diagnostics,
}

impl View {
    pub const ALL: [Self; 5] = [
        Self::Overview,
        Self::Entrypoints,
        Self::Domains,
        Self::Logs,
        Self::Diagnostics,
    ];

    pub fn title(self) -> &'static str {
        match self {
            Self::Overview => "Overview",
            Self::Entrypoints => "Entrypoints",
            Self::Domains => "Domains",
            Self::Logs => "Logs",
            Self::Diagnostics => "Diagnostics",
        }
    }

    pub fn index(self) -> usize {
        Self::ALL.iter().position(|view| *view == self).unwrap_or(0)
    }
}

#[derive(Debug, Clone)]
pub struct TuiModel {
    pub view: View,
    pub search: String,
    pub search_mode: bool,
    pub selected: usize,
    pub logs_paused: bool,
    pub minimum_log_severity: Option<LogSeverity>,
    snapshot: GuiStateSnapshot,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Summary {
    pub runtime: String,
    pub config: String,
    pub entrypoints: usize,
    pub domains: usize,
    pub active_domains: usize,
}

impl Default for TuiModel {
    fn default() -> Self {
        Self {
            view: View::Overview,
            search: String::new(),
            search_mode: false,
            selected: 0,
            logs_paused: false,
            minimum_log_severity: None,
            snapshot: GuiStateSnapshot {
                captured_at_utc: chrono::Utc::now(),
                registrations: Vec::new(),
                runtime: cadder_protocol::RuntimeState::idle(),
                config: cadder_protocol::ConfigState::idle(),
            },
        }
    }
}

impl TuiModel {
    pub fn set_snapshot(&mut self, snapshot: GuiStateSnapshot) {
        self.snapshot = snapshot;
        self.selected = self.selected.min(self.visible_len().saturating_sub(1));
    }

    pub fn snapshot(&self) -> &GuiStateSnapshot {
        &self.snapshot
    }

    pub fn next_view(&mut self) {
        let next = (self.view.index() + 1) % View::ALL.len();
        self.view = View::ALL[next];
        self.selected = 0;
    }

    pub fn previous_view(&mut self) {
        let index = self.view.index();
        self.view = View::ALL[(index + View::ALL.len() - 1) % View::ALL.len()];
        self.selected = 0;
    }

    pub fn move_selection(&mut self, delta: isize) {
        let len = self.visible_len();
        if len == 0 {
            self.selected = 0;
            return;
        }
        self.selected =
            (self.selected as isize + delta).clamp(0, len.saturating_sub(1) as isize) as usize;
    }

    pub fn summary(&self) -> Summary {
        let domains = self
            .snapshot
            .registrations
            .iter()
            .map(|registration| registration.registered_domains.len())
            .sum();
        let active_domains = self
            .snapshot
            .registrations
            .iter()
            .flat_map(|registration| &registration.registered_domains)
            .filter(|domain| domain.activation_state.is_enabled())
            .count();
        Summary {
            runtime: format!("{:?}", self.snapshot.runtime.status),
            config: format!("{:?}", self.snapshot.config.status),
            entrypoints: self.snapshot.registrations.len(),
            domains,
            active_domains,
        }
    }

    pub fn filtered_registrations(&self) -> Vec<&EntrypointRegistration> {
        self.snapshot
            .registrations
            .iter()
            .filter(|registration| {
                self.search.is_empty()
                    || registration.registration_id.contains(&self.search)
                    || registration
                        .source_working_directory
                        .raw
                        .contains(&self.search)
                    || registration.source_config_path.raw.contains(&self.search)
                    || registration
                        .registered_domains
                        .iter()
                        .any(|domain| domain.name.canonical.contains(&self.search))
            })
            .collect()
    }

    pub fn filtered_domains(&self) -> Vec<(&EntrypointRegistration, &RegisteredDomain)> {
        self.snapshot
            .registrations
            .iter()
            .flat_map(|registration| {
                registration
                    .registered_domains
                    .iter()
                    .map(move |domain| (registration, domain))
            })
            .filter(|(registration, domain)| {
                self.search.is_empty()
                    || registration.registration_id.contains(&self.search)
                    || domain.name.canonical.contains(&self.search)
            })
            .collect()
    }

    pub fn selected_entrypoint(&self) -> Option<&EntrypointRegistration> {
        self.filtered_registrations().get(self.selected).copied()
    }

    pub fn selected_domain(&self) -> Option<(String, RegisteredDomain)> {
        self.filtered_domains()
            .get(self.selected)
            .map(|(registration, domain)| (registration.registration_id.clone(), (*domain).clone()))
    }

    pub fn selected_log_stream(&self) -> Option<LogStreamIdentity> {
        self.selected_domain()
            .map(|(_, domain)| domain.log_stream)
            .or_else(|| {
                self.selected_entrypoint()
                    .map(|registration| registration.log_stream.clone())
            })
    }

    fn visible_len(&self) -> usize {
        match self.view {
            View::Entrypoints => self.filtered_registrations().len(),
            View::Domains | View::Logs => self.filtered_domains().len(),
            _ => 0,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use cadder_protocol::{
        ActivationState, ConfigState, EntrypointInstanceIdentity, LogStreamIdentity,
        OwnerProcessIdentity, RegisteredDomain, RuntimeState, SourcePath,
    };
    use chrono::Utc;

    fn snapshot() -> GuiStateSnapshot {
        let now = Utc::now();
        let identity = EntrypointInstanceIdentity {
            instance_id: "shim-1".to_string(),
            started_at_utc: now,
            shim_session_nonce: "nonce".to_string(),
        };
        GuiStateSnapshot {
            captured_at_utc: now,
            registrations: vec![EntrypointRegistration {
                registration_id: "shim-1".to_string(),
                entrypoint_instance: identity.clone(),
                source_working_directory: SourcePath::new("/work/app", None),
                source_config_path: SourcePath::new("/work/app/Caddyfile", None),
                registered_domains: vec![
                    RegisteredDomain::active("app.localhost"),
                    RegisteredDomain {
                        activation_state: ActivationState::Inactive,
                        ..RegisteredDomain::active("api.localhost")
                    },
                ],
                activation_state: ActivationState::Active,
                owner_process: OwnerProcessIdentity {
                    process_id: 1,
                    process_start_time_utc: now,
                    shim_session_nonce: identity.shim_session_nonce,
                    executable_path: None,
                },
                log_stream: LogStreamIdentity::entrypoint("shim-1"),
                shim_run: None,
                created_at_utc: now,
                last_heartbeat_utc: now,
            }],
            runtime: RuntimeState::idle(),
            config: ConfigState::idle(),
        }
    }

    #[test]
    fn summary_counts_domains() {
        let mut model = TuiModel::default();
        model.set_snapshot(snapshot());

        let summary = model.summary();

        assert_eq!(summary.entrypoints, 1);
        assert_eq!(summary.domains, 2);
        assert_eq!(summary.active_domains, 1);
    }

    #[test]
    fn filters_domains_by_search() {
        let mut model = TuiModel::default();
        model.set_snapshot(snapshot());
        model.search = "api".to_string();

        let domains = model.filtered_domains();

        assert_eq!(domains.len(), 1);
        assert_eq!(domains[0].1.name.canonical, "api.localhost");
    }
}
